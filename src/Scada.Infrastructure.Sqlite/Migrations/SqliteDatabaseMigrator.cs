using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Scada.Infrastructure.Sqlite.Migrations;

public enum DatabaseOwner
{
    Web,
    Runtime,
    Cli,
}

public enum DatabaseRole
{
    Config,
    AuditWeb,
    HistorianCatalog,
    HistorianPartition,
    AuditRuntime,
    Alarms,
}

public static class DatabaseOwnership
{
    public static DatabaseOwner OwnerOf(DatabaseRole role) => role switch
    {
        DatabaseRole.Config or DatabaseRole.AuditWeb => DatabaseOwner.Web,
        DatabaseRole.HistorianCatalog or DatabaseRole.HistorianPartition or DatabaseRole.AuditRuntime or DatabaseRole.Alarms => DatabaseOwner.Runtime,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static void AuthorizeMigration(DatabaseOwner actor, DatabaseRole role, bool servicesAreOffline = false)
    {
        if (actor == DatabaseOwner.Cli && servicesAreOffline)
        {
            return;
        }

        if (actor != OwnerOf(role))
        {
            throw new DatabaseOwnershipException($"{actor} is not authorized to migrate {role}; its writer owner is {OwnerOf(role)}.");
        }
    }
}

public sealed class DatabaseOwnershipException : InvalidOperationException
{
    public DatabaseOwnershipException(string message)
        : base(message)
    {
    }

    public DatabaseOwnershipException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public static class DatabasePathPolicy
{
    public static void EnsureLocal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (databasePath.StartsWith(@"\\", StringComparison.Ordinal) || databasePath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new DatabasePathException($"Network database paths are not supported: '{databasePath}'.");
        }
    }
}

public sealed class DatabasePathException(string message) : ArgumentException(message);

public sealed record Migration(string Id, IReadOnlyList<string> Statements)
{
    public string Checksum { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Statements))));
}

public interface IMigrationFaultInjector
{
    void BeforeStatement(string migrationId, int statementIndex);
}

public sealed class MigrationInterruptedException(string migrationId, int statementIndex)
    : Exception($"Migration '{migrationId}' was interrupted before statement {statementIndex}.");

public sealed class SqliteDatabaseMigrator
{
    private readonly string _databasePath;
    private readonly DatabaseRole _role;
    private readonly DatabaseOwner _writerOwner;
    private readonly IReadOnlyList<Migration> _migrations;
    private readonly IMigrationFaultInjector? _faultInjector;

    public SqliteDatabaseMigrator(string databasePath, DatabaseRole role, DatabaseOwner writerOwner, IReadOnlyList<Migration> migrations, IMigrationFaultInjector? faultInjector = null)
    {
        DatabasePathPolicy.EnsureLocal(databasePath);
        if (DatabaseOwnership.OwnerOf(role) != writerOwner)
        {
            throw new DatabaseOwnershipException($"{writerOwner} cannot be configured as writer for {role}.");
        }

        _databasePath = Path.GetFullPath(databasePath);
        _role = role;
        _writerOwner = writerOwner;
        _migrations = migrations.OrderBy(migration => migration.Id, StringComparer.Ordinal).ToArray();
        _faultInjector = faultInjector;
    }

    public void Migrate(DatabaseOwner actor, bool servicesAreOffline = false)
    {
        DatabaseOwnership.AuthorizeMigration(actor, _role, servicesAreOffline);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using FileStream ownershipLock = AcquireExclusiveLock();
        using SqliteConnection connection = new($"Data Source={_databasePath};Mode=ReadWriteCreate;Pooling=False");
        connection.Open();
        EnsureLedger(connection);
        RefuseNewerSchema(connection);

        foreach (Migration migration in _migrations)
        {
            ApplyIfNeeded(connection, migration);
        }
    }

    private FileStream AcquireExclusiveLock()
    {
        try
        {
            return new FileStream(_databasePath + ".migration.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new DatabaseOwnershipException($"Another process owns migration lock for '{_databasePath}'.", exception);
        }
    }

    private static void EnsureLedger(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS __scada_migration_ledger (migration_id TEXT PRIMARY KEY NOT NULL, checksum TEXT NOT NULL, applied_utc TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }

    private void RefuseNewerSchema(SqliteConnection connection)
    {
        string? newestKnownMigration = _migrations.Select(migration => migration.Id).DefaultIfEmpty().Max(StringComparer.Ordinal);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT migration_id FROM __scada_migration_ledger ORDER BY migration_id DESC LIMIT 1";
        string? newestAppliedMigration = command.ExecuteScalar() as string;
        if (newestAppliedMigration is not null &&
            (newestKnownMigration is null || string.CompareOrdinal(newestAppliedMigration, newestKnownMigration) > 0))
        {
            throw new SchemaCompatibilityException($"Database schema '{newestAppliedMigration}' is newer than this service supports.");
        }
    }

    private void ApplyIfNeeded(SqliteConnection connection, Migration migration)
    {
        using SqliteCommand existingCommand = connection.CreateCommand();
        existingCommand.CommandText = "SELECT checksum FROM __scada_migration_ledger WHERE migration_id = $id";
        existingCommand.Parameters.AddWithValue("$id", migration.Id);
        string? recordedChecksum = existingCommand.ExecuteScalar() as string;
        if (recordedChecksum is not null)
        {
            if (!string.Equals(recordedChecksum, migration.Checksum, StringComparison.Ordinal))
            {
                throw new SchemaCompatibilityException($"Migration '{migration.Id}' checksum does not match the database ledger.");
            }

            return;
        }

        using SqliteTransaction transaction = connection.BeginTransaction();
        for (int index = 0; index < migration.Statements.Count; index++)
        {
            _faultInjector?.BeforeStatement(migration.Id, index);
            using SqliteCommand statement = connection.CreateCommand();
            statement.Transaction = transaction;
            statement.CommandText = migration.Statements[index];
            statement.ExecuteNonQuery();
        }

        using SqliteCommand ledger = connection.CreateCommand();
        ledger.Transaction = transaction;
        ledger.CommandText = "INSERT INTO __scada_migration_ledger (migration_id, checksum, applied_utc) VALUES ($id, $checksum, $appliedUtc)";
        ledger.Parameters.AddWithValue("$id", migration.Id);
        ledger.Parameters.AddWithValue("$checksum", migration.Checksum);
        ledger.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.ToString("O"));
        ledger.ExecuteNonQuery();
        transaction.Commit();
    }
}

public sealed class SchemaCompatibilityException(string message) : InvalidOperationException(message);
