using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Scada.Deployment;
using Xunit;

namespace Scada.IntegrationTests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class DeploymentConfigurationAccessControlTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scada-task7-deployment-acl-{Guid.NewGuid():N}");

    [Fact]
    public void LoadRejectsCurrentSidWithFullControl()
    {
        string path = CreateDeploymentFile();
        SetCurrentSidAccess(path, FileSystemRights.FullControl);

        Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.Load(path));
    }

    [Fact]
    public void LoadAllowsCurrentSidWithReadAndExecuteOnly()
    {
        string path = CreateDeploymentFile();
        SetCurrentSidAccess(path, FileSystemRights.ReadAndExecute);

        DeploymentConfiguration configuration = DeploymentConfiguration.Load(path);

        Assert.Equal(path, configuration.SourcePath);
    }

    [Fact]
    public void LoadRejectsCurrentSidWithParentDirectoryReplacementAuthority()
    {
        string path = CreateDeploymentFile();
        SetCurrentSidAccess(path, FileSystemRights.ReadAndExecute);
        SetCurrentSidDirectoryAccess(Path.GetDirectoryName(path)!, FileSystemRights.FullControl);

        Assert.Throws<InvalidOperationException>(() => DeploymentConfiguration.Load(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private string CreateDeploymentFile()
    {
        Directory.CreateDirectory(_directory);
        string deploymentDirectory = Path.Combine(_directory, "deployment");
        Directory.CreateDirectory(deploymentDirectory);
        string path = Path.GetFullPath(Path.Combine(deploymentDirectory, "deployment.json"));
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Current process has no Windows SID.");
        File.WriteAllText(path, JsonSerializer.Serialize(new DeploymentConfiguration(Path.GetFullPath(_directory), sid, sid)));
        SetCurrentSidDirectoryAccess(deploymentDirectory, FileSystemRights.ReadAndExecute);
        return path;
    }

    private static void SetCurrentSidAccess(string path, FileSystemRights rights)
    {
        SecurityIdentifier currentSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current process has no Windows SID.");
        FileSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(currentSid, rights, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static void SetCurrentSidDirectoryAccess(string path, FileSystemRights rights)
    {
        SecurityIdentifier currentSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current process has no Windows SID.");
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(currentSid, rights, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
}