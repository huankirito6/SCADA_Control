using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Scada.Infrastructure.Sqlite.Migrations;

namespace Scada.Hosting.Database;

public static class ServiceHostRunner
{
    public static void RunWindowsService(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows SCM hosting is only supported on Windows.");
        NativeWindowsService service = new(serviceName);
        SERVICE_TABLE_ENTRY[] table = [new() { ServiceName = serviceName, ServiceProc = service.Main }, new()];
        if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error(), "StartServiceCtrlDispatcher failed.");
    }

    public static void RunMigrationOnce(string serviceName, string operationId, string pipeName)
    {
        try
        {
            string root = Environment.GetEnvironmentVariable("SCADA_DATABASE_ROOT") ?? AppContext.BaseDirectory;
            ServiceIdentityPolicy policy = ServiceIdentityEnvironment.FromEnvironment();
            if (string.Equals(serviceName, "WebScada", StringComparison.Ordinal)) WebServiceDatabaseStartup.MigrateOwnedDatabases(root, policy);
            else if (string.Equals(serviceName, "RuntimeScada", StringComparison.Ordinal)) RuntimeServiceDatabaseStartup.MigrateOwnedDatabases(root, policy);
            else throw new ArgumentException("Unknown owner service.", nameof(serviceName));
            SendResult(pipeName, operationId, "OK");
        }
        catch (Exception exception)
        {
            try { SendResult(pipeName, operationId, "ERROR: " + exception.GetType().Name); } catch { }
            throw;
        }
    }

    private static void SendResult(string pipeName, string operationId, string result)
    {
        using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.Out);
        pipe.Connect(10_000);
        using StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: false);
        writer.WriteLine(operationId);
        writer.WriteLine(result);
    }

    private sealed class NativeWindowsService
    {
        private readonly string _serviceName;
        private readonly HandlerEx _handler;
        private IntPtr _statusHandle;

        public NativeWindowsService(string serviceName) { _serviceName = serviceName; _handler = Handler; }

        public void Main(uint argc, IntPtr argv)
        {
            _statusHandle = RegisterServiceCtrlHandlerEx(_serviceName, _handler, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Report(ServiceState.StartPending, 30_000, 0);
            try
            {
                string[] args = ReadArguments(argc, argv);
                if (args.Length > 0 && string.Equals(args[0], _serviceName, StringComparison.OrdinalIgnoreCase)) args = args[1..];
                if (args.Length != 3 || !string.Equals(args[0], "migration-once", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("SCM service requires migration-once operationId pipeName arguments.");
                Report(ServiceState.Running, 0, 0);
                RunMigrationOnce(_serviceName, args[1], args[2]);
                Report(ServiceState.Stopped, 0, 0);
            }
            catch { Report(ServiceState.Stopped, 0, 1); }
        }

        private uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
        {
            if (control == 1) { Report(ServiceState.StopPending, 30_000, 0); Report(ServiceState.Stopped, 0, 0); }
            return 0;
        }

        private void Report(ServiceState state, uint waitHint, uint win32ExitCode)
        {
            SERVICE_STATUS status = new() { ServiceType = 0x10, CurrentState = (uint)state, ControlsAccepted = state == ServiceState.Running ? 1u : 0u, Win32ExitCode = win32ExitCode, CheckPoint = state is ServiceState.StartPending or ServiceState.StopPending ? 1u : 0u, WaitHint = waitHint };
            SetServiceStatus(_statusHandle, ref status);
        }

        private static string[] ReadArguments(uint count, IntPtr argv)
        {
            string[] result = new string[count];
            for (int i = 0; i < count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? string.Empty;
            return result;
        }
    }

    private enum ServiceState : uint { Stopped = 1, StartPending = 2, StopPending = 3, Running = 4 }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct SERVICE_TABLE_ENTRY { public string? ServiceName; public ServiceMainFunction? ServiceProc; }
    [StructLayout(LayoutKind.Sequential)] private struct SERVICE_STATUS { public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint; }
    private delegate void ServiceMainFunction(uint argc, IntPtr argv);
    private delegate uint HandlerEx(uint control, uint eventType, IntPtr eventData, IntPtr context);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool StartServiceCtrlDispatcher([In] SERVICE_TABLE_ENTRY[] table);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr RegisterServiceCtrlHandlerEx(string name, HandlerEx handler, IntPtr context);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetServiceStatus(IntPtr handle, ref SERVICE_STATUS status);
}