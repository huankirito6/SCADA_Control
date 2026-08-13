using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Scada.Cli.Database;

public enum ServiceControlState { Stopped, Running, StartPending, StopPending, Unknown }

public interface IServiceControlManager
{
    ServiceControlState GetState(string serviceName);
    bool WaitForStopped(string serviceName, TimeSpan timeout);
}

public interface IServiceMigrationRequester { void RequestMigration(string serviceName); }

public interface IAcknowledgingServiceMigrationRequester : IServiceMigrationRequester
{
    void PrepareMigration(string serviceName);
    bool WaitForMigration(string serviceName, TimeSpan timeout);
}

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

    public bool WaitForStopped(string serviceName, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        do
        {
            if (GetState(serviceName) == ServiceControlState.Stopped) return true;
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
        }
        while (DateTimeOffset.UtcNow < deadline);

        return GetState(serviceName) == ServiceControlState.Stopped;
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

public sealed class WindowsServiceMigrationRequester : IAcknowledgingServiceMigrationRequester, IDisposable
{
    private readonly Dictionary<string, PendingMigration> _pending = new(StringComparer.Ordinal);

    public void PrepareMigration(string serviceName)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows service migration requests are only supported on Windows.");
        if (_pending.ContainsKey(serviceName)) throw new InvalidOperationException($"A migration is already prepared for '{serviceName}'.");

        string operationId = Guid.NewGuid().ToString("N");
        string pipeName = $"ScadaMigration-{operationId}";
        SecurityIdentifier ownerSid = ResolveOwnerSid(serviceName);
        PipeSecurity security = CreateAcknowledgementPipeSecurity(ownerSid);
        NamedPipeServerStream pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        _pending.Add(serviceName, new PendingMigration(operationId, pipeName, pipe));
    }

    public void RequestMigration(string serviceName)
    {
        if (!_pending.TryGetValue(serviceName, out PendingMigration? pending)) throw new InvalidOperationException($"Prepare migration before requesting '{serviceName}'.");
        WindowsServiceControlManager.Execute("start", serviceName, "migration-once", pending.OperationId, pending.PipeName);
    }

    public bool WaitForMigration(string serviceName, TimeSpan timeout)
    {
        if (!_pending.Remove(serviceName, out PendingMigration? pending)) throw new InvalidOperationException($"No migration is prepared for '{serviceName}'.");
        using (pending)
        using (CancellationTokenSource cancellation = new(timeout))
        {
            try
            {
                pending.Pipe.WaitForConnectionAsync(cancellation.Token).GetAwaiter().GetResult();
                using StreamReader reader = new(pending.Pipe, Encoding.UTF8, leaveOpen: true);
                string? operationId = reader.ReadLine();
                string? result = reader.ReadLine();
                return string.Equals(operationId, pending.OperationId, StringComparison.Ordinal) && string.Equals(result, "OK", StringComparison.Ordinal);
            }
            catch (OperationCanceledException) { return false; }
        }
    }

    public void Dispose()
    {
        foreach (PendingMigration pending in _pending.Values) pending.Dispose();
        _pending.Clear();
    }

    private sealed record PendingMigration(string OperationId, string PipeName, NamedPipeServerStream Pipe) : IDisposable
    {
        public void Dispose() => Pipe.Dispose();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static PipeSecurity CreateAcknowledgementPipeSecurity(SecurityIdentifier ownerSid)
    {
        ArgumentNullException.ThrowIfNull(ownerSid);
        SecurityIdentifier cliSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current CLI identity has no Windows SID.");
        PipeSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(cliSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(ownerSid, PipeAccessRights.Read | PipeAccessRights.Write | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return security;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static SecurityIdentifier ResolveOwnerSid(string serviceName)
    {
        string variable = serviceName switch
        {
            "WebScada" => "SCADA_WEB_SERVICE_SID",
            "RuntimeScada" => "SCADA_RUNTIME_SERVICE_SID",
            _ => throw new ArgumentException("Unknown owner service.", nameof(serviceName))
        };
        string? configuredSid = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configuredSid)) throw new InvalidOperationException($"Required deployment configuration '{variable}' is missing.");
        try { return new SecurityIdentifier(configuredSid); }
        catch (ArgumentException) { throw new InvalidOperationException($"Deployment configuration '{variable}' is not a valid Windows SID."); }
    }
}

public sealed class OfflineMigrationCoordinator
{
    private const string WebService = "WebScada";
    private const string RuntimeService = "RuntimeScada";
    private readonly IServiceControlManager _services;
    private readonly IAcknowledgingServiceMigrationRequester _requester;
    private readonly TimeSpan _acknowledgementTimeout;

    public OfflineMigrationCoordinator(IServiceControlManager services, IServiceMigrationRequester requester, TimeSpan? acknowledgementTimeout = null)
    {
        _services = services;
        _requester = requester as IAcknowledgingServiceMigrationRequester ?? throw new ArgumentException("Offline migration transport must acknowledge completed owner migration.", nameof(requester));
        _acknowledgementTimeout = acknowledgementTimeout ?? TimeSpan.FromMinutes(2);
        if (_acknowledgementTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(acknowledgementTimeout));
    }

    public void RequestOwnerMigration()
    {
        VerifyStopped(WebService);
        VerifyStopped(RuntimeService);
        RequestAndVerify(WebService);
        RequestAndVerify(RuntimeService);
    }

    private void RequestAndVerify(string serviceName)
    {
        _requester.PrepareMigration(serviceName);
        _requester.RequestMigration(serviceName);
        if (!_requester.WaitForMigration(serviceName, _acknowledgementTimeout)) throw new TimeoutException($"Timed out waiting for '{serviceName}' to acknowledge successful offline migration.");
        if (!_services.WaitForStopped(serviceName, _acknowledgementTimeout)) throw new TimeoutException($"Timed out waiting for Windows SCM to stop '{serviceName}' after offline migration.");
    }

    private void VerifyStopped(string serviceName)
    {
        if (_services.GetState(serviceName) != ServiceControlState.Stopped) throw new InvalidOperationException($"CLI refuses offline migration because Windows SCM reports '{serviceName}' is not stopped.");
    }
}

public static class OfflineMigrationCommand
{
    public static void RequestOwnerMigration()
    {
        using WindowsServiceMigrationRequester requester = new();
        new OfflineMigrationCoordinator(new WindowsServiceControlManager(), requester).RequestOwnerMigration();
    }
}