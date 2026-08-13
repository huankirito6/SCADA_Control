using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Scada.Deployment;

public enum ServiceIdentity { Web, Runtime, Cli }
public interface IServiceIdentity { ServiceIdentity Identity { get; } }
public sealed class OsServiceIdentity : IServiceIdentity
{
    internal OsServiceIdentity(ServiceIdentity identity, string sid, bool enforceAccessControl) { Identity = identity; Sid = sid; EnforceAccessControl = enforceAccessControl; }
    public ServiceIdentity Identity { get; }
    public string Sid { get; }
    public bool EnforceAccessControl { get; }
}
public sealed class ServiceIdentityPolicy
{
    private readonly IReadOnlyDictionary<string, ServiceIdentity> _sidToIdentity;
    private readonly bool _enforceAccessControl;

    public ServiceIdentityPolicy(IReadOnlyDictionary<string, ServiceIdentity> sidToIdentity) : this(sidToIdentity, enforceAccessControl: true) { }

    private ServiceIdentityPolicy(IReadOnlyDictionary<string, ServiceIdentity> sidToIdentity, bool enforceAccessControl)
    {
        _sidToIdentity = sidToIdentity;
        _enforceAccessControl = enforceAccessControl;
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public OsServiceIdentity ResolveCurrentProcess()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Database owner identity verification is supported only on Windows release hosts.");
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new DeploymentConfigurationException("Current process has no Windows SID.");
        if (!_sidToIdentity.TryGetValue(sid, out ServiceIdentity identity)) throw new DeploymentConfigurationException($"Windows SID '{sid}' is not a configured database service identity.");
        return new OsServiceIdentity(identity, sid, _enforceAccessControl);
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static ServiceIdentityPolicy ForTestCurrentProcess(ServiceIdentity identity)
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new DeploymentConfigurationException("Current process has no Windows SID.");
        return new ServiceIdentityPolicy(new Dictionary<string, ServiceIdentity>(StringComparer.OrdinalIgnoreCase) { [sid] = identity }, enforceAccessControl: false);
    }
    public static OsServiceIdentity ForTest(ServiceIdentity identity) => new(identity, $"test-{identity}", false);
}
public sealed class DeploymentConfigurationException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public sealed record DeploymentConfiguration(string DatabaseRoot, string WebServiceSid, string RuntimeServiceSid, string? WebServiceName = null, string? RuntimeServiceName = null)
{
    public string? SourcePath { get; internal init; }
    public string EffectiveWebServiceName => string.IsNullOrWhiteSpace(WebServiceName) ? "WebScada" : WebServiceName;
    public string EffectiveRuntimeServiceName => string.IsNullOrWhiteSpace(RuntimeServiceName) ? "RuntimeScada" : RuntimeServiceName;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static DeploymentConfiguration Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException("A fully qualified deployment configuration path is required.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Deployment configuration validation is supported only on Windows.");
        if (WindowsAccessControlPolicy.GrantsUnexpectedMutation(new FileInfo(path).GetAccessControl(), WindowsAccessControlPolicy.FileMutationRights)) throw new InvalidOperationException("Deployment configuration grants write access to an unrelated identity.");
        string parent = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Deployment configuration path must have a parent directory.");
        if (WindowsAccessControlPolicy.GrantsUnexpectedMutation(new DirectoryInfo(parent).GetAccessControl(), WindowsAccessControlPolicy.DirectoryMutationRights)) throw new InvalidOperationException("Deployment configuration parent grants replacement authority to an unrelated identity.");
        try
        {
            DeploymentConfiguration configuration = JsonSerializer.Deserialize<DeploymentConfiguration>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Deployment configuration is empty.");
            _ = new SecurityIdentifier(configuration.WebServiceSid);
            _ = new SecurityIdentifier(configuration.RuntimeServiceSid);
            if (string.IsNullOrWhiteSpace(configuration.DatabaseRoot) || !Path.IsPathFullyQualified(configuration.DatabaseRoot)) throw new InvalidOperationException("Deployment database root must be absolute.");
            return configuration with { SourcePath = Path.GetFullPath(path) };
        }
        catch (JsonException exception) { throw new InvalidOperationException("Deployment configuration is invalid.", exception); }
        catch (ArgumentException exception) { throw new InvalidOperationException("Deployment configuration contains an invalid service SID.", exception); }
    }
    public ServiceIdentityPolicy CreateIdentityPolicy() => new(new Dictionary<string, ServiceIdentity>(StringComparer.OrdinalIgnoreCase) { [WebServiceSid] = ServiceIdentity.Web, [RuntimeServiceSid] = ServiceIdentity.Runtime });
    public string OwnerSid(string serviceName)
    {
        string configuredSid = serviceName switch
        {
            var name when string.Equals(name, EffectiveWebServiceName, StringComparison.OrdinalIgnoreCase) => WebServiceSid,
            var name when string.Equals(name, EffectiveRuntimeServiceName, StringComparison.OrdinalIgnoreCase) => RuntimeServiceSid,
            _ => throw new ArgumentException("Unknown owner service.", nameof(serviceName))
        };
        if (string.IsNullOrWhiteSpace(configuredSid)) throw new InvalidOperationException($"Deployment configuration for '{serviceName}' is missing the owner SID.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows service owner validation is supported only on Windows.");
        try { _ = new SecurityIdentifier(configuredSid); }
        catch (ArgumentException exception) { throw new InvalidOperationException($"Deployment configuration for '{serviceName}' contains an invalid owner SID.", exception); }
        return configuredSid;
    }
    public ServiceIdentity OwnerOf(string serviceName) => string.Equals(serviceName, EffectiveWebServiceName, StringComparison.OrdinalIgnoreCase)
        ? ServiceIdentity.Web
        : string.Equals(serviceName, EffectiveRuntimeServiceName, StringComparison.OrdinalIgnoreCase)
            ? ServiceIdentity.Runtime
            : throw new ArgumentException("Unknown owner service.", nameof(serviceName));
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class WindowsAccessControlPolicy
{
    public const FileSystemRights FileMutationRights = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
    public const FileSystemRights DirectoryMutationRights = FileMutationRights | FileSystemRights.DeleteSubdirectoriesAndFiles;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool GrantsUnexpectedMutation(FileSystemSecurity security, FileSystemRights mutationRights, params string?[] permittedSids)
    {
        SecurityIdentifier localSystem = new(WellKnownSidType.LocalSystemSid, null);
        SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
        HashSet<string> permitted = permittedSids.Where(sid => !string.IsNullOrWhiteSpace(sid)).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .OfType<FileSystemAccessRule>()
            .Any(rule => rule.AccessControlType == AccessControlType.Allow
                && rule.IdentityReference is SecurityIdentifier sid
                && sid != localSystem
                && sid != administrators
                && !permitted.Contains(sid.Value)
                && (rule.FileSystemRights & mutationRights) != 0);
    }
}
