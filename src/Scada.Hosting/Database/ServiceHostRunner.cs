using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Scada.Infrastructure.Sqlite.Migrations;
using Scada.Deployment;

namespace Scada.Hosting.Database;

public static class ServiceHostRunner
{
    public static string StartupDiagnosticPath(string deploymentPath, string serviceName) => deploymentPath + "." + serviceName + ".startup-diagnostic.txt";

    public static void RunWindowsService(string serviceName, string deploymentPath)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows SCM hosting is only supported on Windows.");
        using NativeWindowsService service = new(serviceName, deploymentPath);
        SERVICE_TABLE_ENTRY[] table = [new() { ServiceName = serviceName, ServiceProc = service.Main }, new()];
        if (!StartServiceCtrlDispatcher(table)) throw new Win32Exception(Marshal.GetLastWin32Error(), "StartServiceCtrlDispatcher failed.");
    }

    public static string ReadProcessDeploymentPath(string[] processArguments)
    {
        ArgumentNullException.ThrowIfNull(processArguments);
        int index = Array.FindIndex(processArguments, argument => string.Equals(argument, "--deployment", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= processArguments.Length || Array.FindIndex(processArguments, index + 1, argument => string.Equals(argument, "--deployment", StringComparison.OrdinalIgnoreCase)) >= 0 || !Path.IsPathFullyQualified(processArguments[index + 1])) throw new ArgumentException("Service process requires exactly one --deployment <absolute path> argument.", nameof(processArguments));
        return Path.GetFullPath(processArguments[index + 1]);
    }

    public static ServiceMigrationOnceArguments ParseMigrationOnceArguments(string[] serviceArguments)
    {
        ArgumentNullException.ThrowIfNull(serviceArguments);
        if (serviceArguments.Length != 5 || !string.Equals(serviceArguments[0], "migration-once", StringComparison.OrdinalIgnoreCase) || !string.Equals(serviceArguments[3], "--deployment", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(serviceArguments[1]) || string.IsNullOrWhiteSpace(serviceArguments[2]) || !Path.IsPathFullyQualified(serviceArguments[4])) throw new ArgumentException("SCM migration request requires migration-once <operation-id> <pipe-name> --deployment <absolute path>.", nameof(serviceArguments));
        return new ServiceMigrationOnceArguments(serviceArguments[1], serviceArguments[2], Path.GetFullPath(serviceArguments[4]));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void RunMigrationOnce(string serviceName, string operationId, string pipeName, DeploymentConfiguration deployment)
    {
        try
        {
            MigrateOwned(serviceName, deployment);
            SendResult(pipeName, operationId, "OK");
        }
        catch (Exception exception)
        {
            try { SendResult(pipeName, operationId, "ERROR: " + exception.GetType().Name); } catch { }
            throw;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void MigrateOwned(string serviceName, DeploymentConfiguration deployment)
    {
        if (deployment.OwnerOf(serviceName) == ServiceIdentity.Web) WebServiceDatabaseStartup.MigrateOwnedDatabases(deployment.DatabaseRoot, deployment.CreateIdentityPolicy());
        else if (deployment.OwnerOf(serviceName) == ServiceIdentity.Runtime) RuntimeServiceDatabaseStartup.MigrateOwnedDatabases(deployment.DatabaseRoot, deployment.CreateIdentityPolicy());
        else throw new ArgumentException("Unknown owner service.", nameof(serviceName));
    }

    private static void SendResult(string pipeName, string operationId, string result)
    {
        using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.Out);
        pipe.Connect(10_000);
        using StreamWriter writer = new(pipe, new UTF8Encoding(false), leaveOpen: false);
        writer.WriteLine(operationId);
        writer.WriteLine(result);
    }

    private sealed class NativeWindowsService : IDisposable
    {
        private readonly string _serviceName;
        private readonly string _deploymentPath;
        private readonly HandlerEx _handler;
        private IntPtr _statusHandle;
        private readonly ManualResetEventSlim _stop = new(false);

        public NativeWindowsService(string serviceName, string deploymentPath) { _serviceName = serviceName; _deploymentPath = deploymentPath; _handler = Handler; }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void Main(uint argc, IntPtr argv)
        {
            _statusHandle = RegisterServiceCtrlHandlerEx(_serviceName, _handler, IntPtr.Zero);
            if (_statusHandle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            Report(ServiceState.StartPending, 30_000, 0);
            try
            {
                string[] args = ReadArguments(argc, argv);
                if (args.Length > 0 && string.Equals(args[0], _serviceName, StringComparison.OrdinalIgnoreCase)) args = args[1..];
                if (args.Length > 0)
                {
                    ServiceMigrationOnceArguments migration = ParseMigrationOnceArguments(args);
                    if (!string.Equals(migration.DeploymentPath, _deploymentPath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("SCM migration deployment path does not match the configured service deployment path.");
                    RunMigrationOnce(_serviceName, migration.OperationId, migration.PipeName, DeploymentConfiguration.Load(_deploymentPath));
                    Report(ServiceState.Stopped, 0, 0);
                    return;
                }
                MigrateOwned(_serviceName, DeploymentConfiguration.Load(_deploymentPath));
                Report(ServiceState.Running, 0, 0);
                _stop.Wait();
                Report(ServiceState.Stopped, 0, 0);
            }
            catch (Exception exception)
            {
                try { File.WriteAllText(StartupDiagnosticPath(_deploymentPath, _serviceName), ServiceStartupFailureDiagnostic.Format(exception) + Environment.NewLine, new UTF8Encoding(false)); } catch { }
                Report(ServiceState.Stopped, 0, 1);
            }
        }

        private uint Handler(uint control, uint eventType, IntPtr eventData, IntPtr context)
        {
            if (control == 1) { Report(ServiceState.StopPending, 30_000, 0); _stop.Set(); }
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

        public void Dispose() => _stop.Dispose();
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

public sealed record ServiceMigrationOnceArguments(string OperationId, string PipeName, string DeploymentPath);

public static class ServiceStartupFailureDiagnostic
{
    private const int MaximumLength = 512;

    public static string Format(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string message = string.Concat(exception.Message.Select(character => char.IsControl(character) ? ' ' : character));
        message = string.Join(' ', message.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        string diagnostic = exception.GetType().Name + ": " + message;
        return diagnostic.Length <= MaximumLength ? diagnostic : diagnostic[..MaximumLength];
    }
}