using Microsoft.Data.Sqlite;
using Scada.Deployment;
using Scada.Infrastructure.Sqlite.Migrations;
using Xunit;

namespace Scada.IntegrationTests;

public sealed class MigrationCrashTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"scada-task7-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(MigrationFaultPoint.BeforeStatement)]
    [InlineData(MigrationFaultPoint.AfterStatement)]
    [InlineData(MigrationFaultPoint.BeforeLedgerInsert)]
    [InlineData(MigrationFaultPoint.AfterLedgerInsert)]
    [InlineData(MigrationFaultPoint.BeforeCommit)]
    [InlineData(MigrationFaultPoint.AfterCommit)]
    public void MigrationFaultAtEveryDurabilityBoundaryReopensAndRetries(MigrationFaultPoint point)
    {
        Directory.CreateDirectory(_directory);
        string databasePath = Path.Combine(_directory, $"{point}.db");
        Migration migration = new("20260812000000_recover", ["CREATE TABLE recovery_test (id INTEGER PRIMARY KEY)", "INSERT INTO recovery_test (id) VALUES (1)"]);
        IServiceIdentity web = ServiceIdentityPolicy.ForTest(ServiceIdentity.Web);

        SqliteDatabaseMigrator interrupted = new(databasePath, DatabaseRole.Config, [migration], web, new ThrowAt(point));
        Assert.Throws<MigrationInterruptedException>(interrupted.Migrate);

        using (SqliteConnection connection = Open(databasePath))
        {
            Assert.Equal("wal", ScalarString(connection, "PRAGMA journal_mode"));
            Assert.Equal(2L, ScalarLong(connection, "PRAGMA synchronous"));
        }

        new SqliteDatabaseMigrator(databasePath, DatabaseRole.Config, [migration], web).Migrate();
        using SqliteConnection recovered = Open(databasePath);
        Assert.Equal(1L, ScalarLong(recovered, "SELECT COUNT(*) FROM __scada_migration_ledger"));
        Assert.Equal(1L, ScalarLong(recovered, "SELECT COUNT(*) FROM recovery_test"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ScalarString(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Assert.IsType<string>(command.ExecuteScalar());
    }

    private sealed class ThrowAt(MigrationFaultPoint point) : IMigrationFaultInjector
    {
        public void At(MigrationFaultPoint actual, string migrationId, int statementIndex = -1)
        {
            if (actual == point) throw new MigrationInterruptedException(migrationId, actual, statementIndex);
        }
    }
}
