using Microsoft.Data.Sqlite;
using System.Security.AccessControl;
using System.Security.Principal;
using Scada.Cli.Database;
using Scada.Deployment;
using Scada.Hosting.Database;
using Scada.Infrastructure.Sqlite.Migrations;
using Scada.Infrastructure.Sqlite.Migrations.AuditWeb;
using Scada.Infrastructure.Sqlite.Migrations.Config;
using Scada.Infrastructure.Sqlite.Migrations.Historian;
using Xunit;

namespace Scada.IntegrationTests;

public sealed class DatabaseOwnershipTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scada-task7-{Guid.NewGuid():N}");

    [Fact]
    public void StartupFailureDiagnosticFormatsOnlyBoundedExceptionTypeAndSingleLineMessage()
    {
        string diagnostic = ServiceStartupFailureDiagnostic.Format(new InvalidOperationException($"bad{Environment.NewLine}value", new ArgumentException("secret")));

        Assert.Equal("InvalidOperationException: bad value", diagnostic);
        Assert.DoesNotContain("secret", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.NewLine, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WebCompositionResolvesActualCurrentSidAndMigratesOnlyWebDatabases()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        WebServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, CurrentProcessPolicy(ServiceIdentity.Web));

        AssertLedger(root.DatabasePath("web", "config.db"), ConfigMigrations.All.Single().Id);
        AssertLedger(root.DatabasePath("web", "audit-web.db"), AuditWebMigrations.All.Single().Id);
        Assert.False(File.Exists(root.DatabasePath("runtime", "alarms.db")));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void RuntimeCompositionMigratesCatalogEveryExistingPartitionAndNewPartition()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        string partitions = Path.Combine(root.Path, "runtime", "historian-partitions");
        Directory.CreateDirectory(partitions);
        File.Create(Path.Combine(partitions, "2026-08.db")).Dispose();
        File.Create(Path.Combine(partitions, "2026-09.db")).Dispose();

        RuntimeServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, CurrentProcessPolicy(ServiceIdentity.Runtime));
        RuntimeServiceDatabaseStartup.OpenPartition(root.Path, "2026-10", CurrentProcessPolicy(ServiceIdentity.Runtime));

        AssertLedger(root.DatabasePath("runtime", "historian-catalog.db"), HistorianMigrations.Catalog.Single().Id);
        AssertLedger(Path.Combine(partitions, "2026-08.db"), HistorianMigrations.Partition.Single().Id);
        AssertLedger(Path.Combine(partitions, "2026-09.db"), HistorianMigrations.Partition.Single().Id);
        AssertLedger(Path.Combine(partitions, "2026-10.db"), HistorianMigrations.Partition.Single().Id);
    }

    [Fact]
    public void CompositionCannotAcceptForgedCallerSuppliedIdentity()
    {
        Assert.DoesNotContain(typeof(WebServiceDatabaseStartup).GetMethods(), method => method.GetParameters().Any(p => p.ParameterType == typeof(IServiceIdentity)));
        Assert.DoesNotContain(typeof(RuntimeServiceDatabaseStartup).GetMethods(), method => method.GetParameters().Any(p => p.ParameterType == typeof(IServiceIdentity)));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WebCompositionRejectsCurrentSidConfiguredAsRuntime()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        Assert.Throws<DatabaseOwnershipException>(() => WebServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, CurrentProcessPolicy(ServiceIdentity.Runtime)));
        Assert.False(File.Exists(root.DatabasePath("web", "config.db")));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void MigrationRejectsOwnerDirectoryAlsoWritableByOtherConfiguredOwner()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        string ownerDirectory = Path.Combine(root.Path, "web");
        Directory.CreateDirectory(ownerDirectory);
        SecurityIdentifier currentSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current process has no Windows SID.");
        SecurityIdentifier otherOwnerSid = new("S-1-5-21-1-2-3-4");
        SetOwnerDirectoryAccess(ownerDirectory, currentSid, otherOwnerSid);
        ServiceIdentityPolicy policy = new(new Dictionary<string, ServiceIdentity>
        {
            [currentSid.Value] = ServiceIdentity.Web,
            [otherOwnerSid.Value] = ServiceIdentity.Runtime
        });

        Assert.Throws<DatabaseOwnershipException>(() => WebServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, policy));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void MigrationAllowsOwnerDirectoryWritableOnlyByOwnerSystemAndAdministrators()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        string ownerDirectory = Path.Combine(root.Path, "web");
        Directory.CreateDirectory(ownerDirectory);
        SecurityIdentifier currentSid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current process has no Windows SID.");
        SetOwnerDirectoryAccess(ownerDirectory, currentSid);
        ServiceIdentityPolicy policy = new(new Dictionary<string, ServiceIdentity>
        {
            [currentSid.Value] = ServiceIdentity.Web,
            ["S-1-5-21-1-2-3-4"] = ServiceIdentity.Runtime
        });

        WebServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, policy);

        AssertLedger(root.DatabasePath("web", "config.db"), ConfigMigrations.All.Single().Id);
    }

    [Fact]
    public void OfflineCliRefusesRunningScmServiceBeforeRequestingOwnerMigration()
    {
        AcknowledgingRequester requester = new();
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Running, "RuntimeScada", ServiceControlState.Stopped), requester);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestOwnerMigration());
        Assert.Empty(requester.RequestedServices);
    }

    [Fact]
    public void OfflineCliRequestsBothOwnersOnlyAfterScmReportsStoppedAndNeverOpensDatabase()
    {
        AcknowledgingRequester requester = new();
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped), requester);

        coordinator.RequestOwnerMigration();

        Assert.Equal(["WebScada", "RuntimeScada"], requester.RequestedServices);
    }

    [Fact]
    public void OfflineCliRequiresAcknowledgedMigrationAndStoppedServicesBeforeReturning()
    {
        AcknowledgingRequester requester = new();
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped), requester, TimeSpan.FromMilliseconds(50));

        coordinator.RequestOwnerMigration();

        Assert.Equal(["WebScada", "RuntimeScada"], requester.RequestedServices);
        Assert.Equal(["WebScada", "RuntimeScada"], requester.AcknowledgedServices);
    }

    [Fact]
    public void OfflineCliPreparesEachAcknowledgementBeforeStartingItsOwnerService()
    {
        OrderedAcknowledgingRequester requester = new();
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped), requester);

        coordinator.RequestOwnerMigration();

        Assert.Equal(
            ["prepare:WebScada", "request:WebScada", "wait:WebScada", "prepare:RuntimeScada", "request:RuntimeScada", "wait:RuntimeScada"],
            requester.Calls);
    }

    [Fact]
    public void OfflineCliWaitsForScmStoppedAfterEachSuccessfulAcknowledgement()
    {
        AcknowledgingRequester requester = new();
        FixtureScm scm = new("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped);
        OfflineMigrationCoordinator coordinator = new(scm, requester);

        coordinator.RequestOwnerMigration();

        Assert.Equal(["WebScada", "RuntimeScada"], scm.WaitedForStopped);
    }

    [Fact]
    public void OfflineCliFailsWhenScmDoesNotStopAfterSuccessfulAcknowledgement()
    {
        AcknowledgingRequester requester = new();
        FixtureScm scm = new("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped) { StopWaitResult = false };
        OfflineMigrationCoordinator coordinator = new(scm, requester, TimeSpan.FromMilliseconds(10));

        Assert.Throws<TimeoutException>(coordinator.RequestOwnerMigration);
        Assert.Equal(["WebScada"], scm.WaitedForStopped);
    }

    [Fact]
    public void OfflineCliFailsWhenOwnerDoesNotAcknowledgeBeforeBoundedTimeout()
    {
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped), new NeverAcknowledgingRequester(), TimeSpan.FromMilliseconds(10));

        Assert.Throws<TimeoutException>(coordinator.RequestOwnerMigration);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AcknowledgementPipeRejectsMissingOwnerSidBeforeServiceStart()
    {
        using WindowsServiceMigrationRequester requester = new(Deployment("", "S-1-5-21-1-2-3-4"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => requester.PrepareMigration("WebScada"));

        Assert.DoesNotContain("S-", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void AcknowledgementPipeRejectsInvalidOwnerSidBeforeServiceStart()
    {
        using WindowsServiceMigrationRequester requester = new(Deployment("not-a-sid", "S-1-5-21-1-2-3-4"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => requester.PrepareMigration("WebScada"));

        Assert.DoesNotContain("not-a-sid", exception.Message, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData(DatabaseRole.Config)]
    [InlineData(DatabaseRole.AuditWeb)]
    [InlineData(DatabaseRole.HistorianCatalog)]
    [InlineData(DatabaseRole.HistorianPartition)]
    [InlineData(DatabaseRole.AuditRuntime)]
    [InlineData(DatabaseRole.Alarms)]
    public void EveryDatabaseRoleHasNonEmptyForwardOnlyMigration(DatabaseRole role)
    {
        Migration migration = Assert.Single(DatabaseMigrationSets.For(role));
        Assert.NotEmpty(migration.Statements);
        Assert.Matches("^[0-9]{14}_[a-z0-9_]+$", migration.Id);
    }

    [Theory]
    [InlineData(@"\\server\share\config.db")]
    [InlineData("//server/share/config.db")]
    [InlineData(@"Z:\mapped\config.db")]
    public void NetworkAndMappedDatabasePathsAreRejected(string path) => Assert.Throws<DatabasePathException>(() => DatabasePathPolicy.ResolveLocal(path));

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
    private TemporaryDatabaseRoot CreateRoot() { Directory.CreateDirectory(_directory); return new TemporaryDatabaseRoot(_directory); }
    private static void AssertLedger(string path, string migrationId) { using SqliteConnection c = Open(path); using SqliteCommand command = c.CreateCommand(); command.CommandText = "SELECT migration_id FROM __scada_migration_ledger"; Assert.Equal(migrationId, Assert.IsType<string>(command.ExecuteScalar())); }
    private static SqliteConnection Open(string path) { SqliteConnection connection = new($"Data Source={path};Pooling=False"); connection.Open(); return connection; }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static ServiceIdentityPolicy CurrentProcessPolicy(ServiceIdentity identity)
    {
        return ServiceIdentityPolicy.ForTestCurrentProcess(identity);
    }
    private static DeploymentConfiguration Deployment(string webSid, string runtimeSid) => new(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scada-task7-deployment")), webSid, runtimeSid);
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void SetOwnerDirectoryAccess(string path, SecurityIdentifier ownerSid, SecurityIdentifier? otherOwnerSid = null)
    {
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(ownerSid, FileSystemRights.FullControl, AccessControlType.Allow));
        if (otherOwnerSid is not null) security.AddAccessRule(new FileSystemAccessRule(otherOwnerSid, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }

    private sealed class FixtureScm(params object[] states) : IServiceControlManager
    {
        private readonly Dictionary<string, ServiceControlState> _states = states.Chunk(2).ToDictionary(pair => (string)pair[0], pair => (ServiceControlState)pair[1], StringComparer.Ordinal);
        public List<string> WaitedForStopped { get; } = [];
        public bool StopWaitResult { get; init; } = true;
        public ServiceControlState GetState(string serviceName) => _states[serviceName];
        public bool WaitForStopped(string serviceName, TimeSpan timeout)
        {
            WaitedForStopped.Add(serviceName);
            return StopWaitResult && GetState(serviceName) == ServiceControlState.Stopped;
        }
    }
    private sealed class RecordingRequester : IServiceMigrationRequester { public List<string> RequestedServices { get; } = []; public void RequestMigration(string serviceName) => RequestedServices.Add(serviceName); }
    private sealed class AcknowledgingRequester : IAcknowledgingServiceMigrationRequester
    {
        public List<string> RequestedServices { get; } = [];
        public List<string> AcknowledgedServices { get; } = [];
        public void PrepareMigration(string serviceName) { }
        public void RequestMigration(string serviceName) => RequestedServices.Add(serviceName);
        public bool WaitForMigration(string serviceName, TimeSpan timeout) { AcknowledgedServices.Add(serviceName); return true; }
    }
    private sealed class NeverAcknowledgingRequester : IAcknowledgingServiceMigrationRequester
    {
        public void PrepareMigration(string serviceName) { }
        public void RequestMigration(string serviceName) { }
        public bool WaitForMigration(string serviceName, TimeSpan timeout) => false;
    }
    private sealed class OrderedAcknowledgingRequester : IAcknowledgingServiceMigrationRequester
    {
        public List<string> Calls { get; } = [];
        public void PrepareMigration(string serviceName) => Calls.Add($"prepare:{serviceName}");
        public void RequestMigration(string serviceName) => Calls.Add($"request:{serviceName}");
        public bool WaitForMigration(string serviceName, TimeSpan timeout) { Calls.Add($"wait:{serviceName}"); return true; }
    }

    private sealed class TemporaryDatabaseRoot(string path) : IDisposable { public string Path { get; } = path; public string DatabasePath(params string[] segments) => System.IO.Path.Combine([Path, .. segments]); public void Dispose() { } }
}
