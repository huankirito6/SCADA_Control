using Microsoft.Data.Sqlite;
using Scada.Infrastructure.Sqlite.Migrations;
using Xunit;

namespace Scada.IntegrationTests;

public sealed class MigrationCrashTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scada-task7-{Guid.NewGuid():N}");

    [Fact]
    public void MigrationFailureAtEveryStatementBoundaryLeavesNoPartialMigration()
    {
        Directory.CreateDirectory(_directory);
        string databasePath = Path.Combine(_directory, "config.db");
        Migration migration = new(
            "202608120001_create_assets",
            [
                "CREATE TABLE assets (id INTEGER PRIMARY KEY)",
                "INSERT INTO assets (id) VALUES (1)",
            ]);

        for (int statementBoundary = 1; statementBoundary <= migration.Statements.Count; statementBoundary++)
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
            SqliteDatabaseMigrator migrator = new(
                databasePath,
                DatabaseRole.Config,
                DatabaseOwner.Web,
                [migration],
                new ThrowAtStatementBoundary(statementBoundary));

            Assert.Throws<MigrationInterruptedException>(() => migrator.Migrate(DatabaseOwner.Web));

            using (SqliteConnection connection = new($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM __scada_migration_ledger"));
                Assert.Equal(0L, ScalarLong(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'assets'"));
            }

            new SqliteDatabaseMigrator(databasePath, DatabaseRole.Config, DatabaseOwner.Web, [migration])
                .Migrate(DatabaseOwner.Web);

            using SqliteConnection recoveredConnection = new($"Data Source={databasePath};Pooling=False");
            recoveredConnection.Open();
            Assert.Equal(1L, ScalarLong(recoveredConnection, "SELECT COUNT(*) FROM assets"));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private sealed class ThrowAtStatementBoundary(int boundary) : IMigrationFaultInjector
    {
        public void BeforeStatement(string migrationId, int statementIndex)
        {
            if (statementIndex + 1 == boundary)
            {
                throw new MigrationInterruptedException(migrationId, statementIndex);
            }
        }
    }
}
