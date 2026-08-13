using Scada.Cli.Database;
using Scada.Hosting.Database;
using Xunit;

namespace Scada.IntegrationTests;

public sealed class ServiceDeploymentPathTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scada-task7-service-path-{Guid.NewGuid():N}");

    [Fact]
    public void NormalServiceStartUsesConfiguredProcessDeploymentPath()
    {
        string deploymentPath = Path.GetFullPath(Path.Combine(_directory, "deployment.json"));

        string selected = ServiceHostRunner.ReadProcessDeploymentPath(["Scada.Web.exe", "--deployment", deploymentPath, "--service-name", "OperationsWeb"]);

        Assert.Equal(deploymentPath, selected);
    }

    [Fact]
    public void MigrationOnceArgumentsRequireBoundedDeploymentArgument()
    {
        string deploymentPath = Path.GetFullPath(Path.Combine(_directory, "deployment.json"));

        ServiceMigrationOnceArguments parsed = ServiceHostRunner.ParseMigrationOnceArguments(["migration-once", "operation", "pipe", "--deployment", deploymentPath]);

        Assert.Equal("operation", parsed.OperationId);
        Assert.Equal("pipe", parsed.PipeName);
        Assert.Equal(deploymentPath, parsed.DeploymentPath);
        Assert.Throws<ArgumentException>(() => ServiceHostRunner.ParseMigrationOnceArguments(["migration-once", "operation", "pipe"]));
        Assert.Throws<ArgumentException>(() => ServiceHostRunner.ParseMigrationOnceArguments(["migration-once", "operation", "pipe", "--deployment", deploymentPath, "extra"]));
    }

    [Fact]
    public void OneShotServiceStartIncludesConfiguredDeploymentPathForCustomServiceName()
    {
        string deploymentPath = Path.GetFullPath(Path.Combine(_directory, "deployment.json"));

        string[] arguments = WindowsServiceMigrationRequester.CreateStartArguments("OperationsWeb", "operation", "pipe", deploymentPath);

        Assert.Equal(["start", "OperationsWeb", "migration-once", "operation", "pipe", "--deployment", deploymentPath], arguments);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
