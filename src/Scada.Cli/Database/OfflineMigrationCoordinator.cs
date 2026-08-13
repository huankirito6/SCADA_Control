using System.Diagnostics;

namespace Scada.Cli.Database;

public enum ServiceControlState { Stopped, Running, StartPending, StopPending, Unknown }
public interface IServiceControlManager { ServiceControlState GetState(string serviceName); }
public interface IServiceMigrationRequester { void RequestMigration(string serviceName); }

public sealed class WindowsServiceControlManager : IServiceControlManager
{
    public ServiceControlState GetState(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows SCM migration coordination is only supported on Windows.");
        string output = Execute("query", serviceName);
        if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return ServiceControlState.Stopped;
        if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return ServiceControlState.Running;
        if (output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase)) return ServiceControlState.StartPending;
        if (output.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase)) return ServiceControlState.StopPending;
        return ServiceControlState.Unknown;
    }

    internal static string Execute(params string[] arguments)
    {
        ProcessStartInfo startInfo = new() { FileName = "sc.exe", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to invoke Windows SCM.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Windows SCM command failed: {string.Join(' ', arguments)}. {output}");
        return output;
    }
}

public sealed class WindowsServiceMigrationRequester : IServiceMigrationRequester
{
    public void RequestMigration(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows service migration requests are only supported on Windows.");
        WindowsServiceControlManager.Execute("start", serviceName);
    }
}

public sealed class OfflineMigrationCoordinator
{
    private const string WebService = "WebScada";
    private const string RuntimeService = "RuntimeScada";
    private readonly IServiceControlManager _services;
    private readonly IServiceMigrationRequester _requester;

    public OfflineMigrationCoordinator(IServiceControlManager services, IServiceMigrationRequester requester) { _services = services; _requester = requester; }

    public void RequestOwnerMigration()
    {
        VerifyStopped(WebService);
        VerifyStopped(RuntimeService);
        _requester.RequestMigration(WebService);
        _requester.RequestMigration(RuntimeService);
    }

    private void VerifyStopped(string serviceName)
    {
        if (_services.GetState(serviceName) != ServiceControlState.Stopped)
            throw new InvalidOperationException($"CLI refuses offline migration because Windows SCM reports '{serviceName}' is not stopped.");
    }
}

public static class OfflineMigrationCommand
{
    public static void RequestOwnerMigration() => new OfflineMigrationCoordinator(new WindowsServiceControlManager(), new WindowsServiceMigrationRequester()).RequestOwnerMigration();
}
