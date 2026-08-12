using Microsoft.Data.Sqlite;
using Scada.Infrastructure.Sqlite.Migrations;
using Xunit;

namespace Scada.IntegrationTests;

public sealed class DatabaseOwnershipTests
{
    [Theory]
    [InlineData(DatabaseRole.Config, DatabaseOwner.Web)]
    [InlineData(DatabaseRole.AuditWeb, DatabaseOwner.Web)]
    [InlineData(DatabaseRole.HistorianCatalog, DatabaseOwner.Runtime)]
    [InlineData(DatabaseRole.HistorianPartition, DatabaseOwner.Runtime)]
    [InlineData(DatabaseRole.AuditRuntime, DatabaseOwner.Runtime)]
    [InlineData(DatabaseRole.Alarms, DatabaseOwner.Runtime)]
    public void DatabaseRoleHasExactlyOneWriterOwner(DatabaseRole role, DatabaseOwner expectedOwner)
    {
        Assert.Equal(expectedOwner, DatabaseOwnership.OwnerOf(role));
    }

    [Fact]
    public void CliCanOnlyOrchestrateOfflineServices()
    {
        Assert.Throws<DatabaseOwnershipException>(() =>
            DatabaseOwnership.AuthorizeMigration(DatabaseOwner.Cli, DatabaseRole.Config, servicesAreOffline: false));

        DatabaseOwnership.AuthorizeMigration(DatabaseOwner.Cli, DatabaseRole.Config, servicesAreOffline: true);
    }

    [Fact]
    public void WrongWriterIsRejected()
    {
        Assert.Throws<DatabaseOwnershipException>(() =>
            DatabaseOwnership.AuthorizeMigration(DatabaseOwner.Web, DatabaseRole.Alarms, servicesAreOffline: true));
    }

    [Fact]
    public void NetworkDatabasePathsAreRejected()
    {
        Assert.Throws<DatabasePathException>(() => DatabasePathPolicy.EnsureLocal(@"\\server\share\config.db"));
    }

    [Fact]
    public void ExistingNewerSchemaIsRefused()
    {
        using TemporaryDatabase database = new();
        Migration knownMigration = new("202608120001_known", ["CREATE TABLE known_table (id INTEGER PRIMARY KEY)"]);
        new SqliteDatabaseMigrator(database.Path, DatabaseRole.Config, DatabaseOwner.Web, [knownMigration])
            .Migrate(DatabaseOwner.Web);

        using (SqliteConnection connection = database.Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO __scada_migration_ledger (migration_id, checksum, applied_utc) VALUES ('999999999999_newer', 'abc', '2026-08-12T00:00:00.0000000+00:00')";
            command.ExecuteNonQuery();
        }

        Assert.Throws<SchemaCompatibilityException>(() =>
            new SqliteDatabaseMigrator(database.Path, DatabaseRole.Config, DatabaseOwner.Web, [knownMigration])
                .Migrate(DatabaseOwner.Web));
    }

    [Fact]
    public void ExistingMigrationLockPreventsSecondWriter()
    {
        using TemporaryDatabase database = new();
        using FileStream lockHandle = new(database.Path + ".migration.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        Assert.Throws<DatabaseOwnershipException>(() =>
            new SqliteDatabaseMigrator(database.Path, DatabaseRole.Config, DatabaseOwner.Web, [])
                .Migrate(DatabaseOwner.Web));
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scada-task7-{Guid.NewGuid():N}");

        public TemporaryDatabase()
        {
            Directory.CreateDirectory(_directory);
        }

        public string Path => System.IO.Path.Combine(_directory, "database.db");

        public SqliteConnection Open()
        {
            SqliteConnection connection = new($"Data Source={Path};Pooling=False");
            connection.Open();
            return connection;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
