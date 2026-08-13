using Microsoft.Data.Sqlite;
using Scada.Cli.Database;
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
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void WebCompositionResolvesActualCurrentSidAndMigratesOnlyWebDatabases()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        WebServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, CurrentProcessPolicy(ServiceIdentity.Web));

        AssertLedger(root.DatabasePath("config.db"), ConfigMigrations.All.Single().Id);
        AssertLedger(root.DatabasePath("audit-web.db"), AuditWebMigrations.All.Single().Id);
        Assert.False(File.Exists(root.DatabasePath("alarms.db")));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void RuntimeCompositionMigratesCatalogEveryExistingPartitionAndNewPartition()
    {
        using TemporaryDatabaseRoot root = CreateRoot();
        string partitions = Path.Combine(root.Path, "historian-partitions");
        Directory.CreateDirectory(partitions);
        File.Create(Path.Combine(partitions, "2026-08.db")).Dispose();
        File.Create(Path.Combine(partitions, "2026-09.db")).Dispose();

        RuntimeServiceDatabaseStartup.MigrateOwnedDatabases(root.Path, CurrentProcessPolicy(ServiceIdentity.Runtime));
        RuntimeServiceDatabaseStartup.OpenPartition(root.Path, "2026-10", CurrentProcessPolicy(ServiceIdentity.Runtime));

        AssertLedger(root.DatabasePath("historian-catalog.db"), HistorianMigrations.Catalog.Single().Id);
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
        Assert.False(File.Exists(root.DatabasePath("config.db")));
    }

    [Fact]
    public void OfflineCliRefusesRunningScmServiceBeforeRequestingOwnerMigration()
    {
        RecordingRequester requester = new();
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Running, "RuntimeScada", ServiceControlState.Stopped), requester);

        Assert.Throws<InvalidOperationException>(() => coordinator.RequestOwnerMigration());
        Assert.Empty(requester.RequestedServices);
    }

    [Fact]
    public void OfflineCliRequestsBothOwnersOnlyAfterScmReportsStoppedAndNeverOpensDatabase()
    {
        RecordingRequester requester = new();
        OfflineMigrationCoordinator coordinator = new(new FixtureScm("WebScada", ServiceControlState.Stopped, "RuntimeScada", ServiceControlState.Stopped), requester);

        coordinator.RequestOwnerMigration();

        Assert.Equal(["WebScada", "RuntimeScada"], requester.RequestedServices);
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

    private sealed class FixtureScm(params object[] states) : IServiceControlManager
    {
        private readonly Dictionary<string, ServiceControlState> _states = states.Chunk(2).ToDictionary(pair => (string)pair[0], pair => (ServiceControlState)pair[1], StringComparer.Ordinal);
        public ServiceControlState GetState(string serviceName) => _states[serviceName];
    }
    private sealed class RecordingRequester : IServiceMigrationRequester { public List<string> RequestedServices { get; } = []; public void RequestMigration(string serviceName) => RequestedServices.Add(serviceName); }
    private sealed class TemporaryDatabaseRoot(string path) : IDisposable { public string Path { get; } = path; public string DatabasePath(string name) => System.IO.Path.Combine(Path, name); public void Dispose() { } }
}
