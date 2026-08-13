using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Scada.Infrastructure.Sqlite.Migrations;

public enum DatabaseRole { Config, AuditWeb, HistorianCatalog, HistorianPartition, AuditRuntime, Alarms }
public enum ServiceIdentity { Web, Runtime, Cli }
public enum MigrationFaultPoint { BeforeStatement, AfterStatement, BeforeLedgerInsert, AfterLedgerInsert, BeforeCommit, AfterCommit }

public interface IServiceIdentity { ServiceIdentity Identity { get; } }
public interface IServiceStateProbe { bool AreAllOwnerServicesStopped(); }

// Production hosts construct this from the current Windows access token and an ACL policy;
// its constructor is internal so a process cannot make a public role/boolean authorization claim.
public sealed class OsServiceIdentity : IServiceIdentity
{
    internal OsServiceIdentity(ServiceIdentity identity, string sid, bool enforceAccessControl) { Identity = identity; Sid = sid; EnforceAccessControl = enforceAccessControl; }
    public ServiceIdentity Identity { get; }
    public string Sid { get; }
    internal bool EnforceAccessControl { get; }
}

public sealed class ServiceIdentityPolicy
{
    private readonly IReadOnlyDictionary<string, ServiceIdentity> _sidToIdentity;
    private readonly bool _enforceAccessControl;
    public ServiceIdentityPolicy(IReadOnlyDictionary<string, ServiceIdentity> sidToIdentity) : this(sidToIdentity, enforceAccessControl: true) { }
    private ServiceIdentityPolicy(IReadOnlyDictionary<string, ServiceIdentity> sidToIdentity, bool enforceAccessControl) { _sidToIdentity = sidToIdentity; _enforceAccessControl = enforceAccessControl; }
    public OsServiceIdentity ResolveCurrentProcess()
    {
        if (!OperatingSystem.IsWindows()) throw new DatabaseOwnershipException("Database owner identity verification is supported only on Windows release hosts.");
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? throw new DatabaseOwnershipException("Current process has no Windows SID.");
        if (!_sidToIdentity.TryGetValue(sid, out ServiceIdentity identity)) throw new DatabaseOwnershipException($"Windows SID '{sid}' is not a configured database service identity.");
        return new OsServiceIdentity(identity, sid, _enforceAccessControl);
    }
    [SupportedOSPlatform("windows")]
    internal static ServiceIdentityPolicy ForTestCurrentProcess(ServiceIdentity identity)
    {
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? throw new DatabaseOwnershipException("Current process has no Windows SID.");
        return new ServiceIdentityPolicy(new Dictionary<string, ServiceIdentity>(StringComparer.OrdinalIgnoreCase) { [sid] = identity }, enforceAccessControl: false);
    }
    internal static OsServiceIdentity ForTest(ServiceIdentity identity) => new(identity, $"test-{identity}", enforceAccessControl: false);
}

public static class DatabaseOwnership
{
    public static ServiceIdentity OwnerOf(DatabaseRole role) => role is DatabaseRole.Config or DatabaseRole.AuditWeb ? ServiceIdentity.Web : ServiceIdentity.Runtime;
    internal static OsServiceIdentity DemandOwner(DatabaseRole role, IServiceIdentity identity)
    {
        if (identity is not OsServiceIdentity verified || verified.Identity != OwnerOf(role))
            throw new DatabaseOwnershipException($"The verified Windows service identity is not the writer owner of {role}.");
        return verified;
    }
}

internal static class WindowsDatabaseAccessPolicy
{
    internal static void Apply(string databasePath, OsServiceIdentity identity)
    {
        // Release hosts use a configured service SID. The parent ACL protects the DB,
        // WAL/SHM files, and migration lock before SQLite opens any of them.
        if (!OperatingSystem.IsWindows() || !identity.EnforceAccessControl) return;
        string directory = Path.GetDirectoryName(databasePath)!;
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "icacls",
            Arguments = $"\"{directory}\" /inheritance:r /grant:r \"{identity.Sid}:(OI)(CI)F\" /grant:r \"*S-1-5-18:(OI)(CI)F\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new DatabaseOwnershipException("Unable to start icacls for database ACL enforcement.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new DatabaseOwnershipException("icacls failed to apply the database owner ACL.");
    }
}

public sealed class DatabaseServiceCoordinator
{
    private readonly IServiceIdentity _identity; private readonly IServiceStateProbe _services;
    public DatabaseServiceCoordinator(IServiceIdentity identity, IServiceStateProbe services) { _identity = identity; _services = services; }
    public void RequestOwnerMigration(DatabaseRole role)
    {
        if (_identity is not OsServiceIdentity { Identity: ServiceIdentity.Cli }) throw new DatabaseOwnershipException("Only the verified CLI service may coordinate owner migration.");
        if (!_services.AreAllOwnerServicesStopped()) throw new DatabaseOwnershipException("CLI refuses migration coordination until owner services are verified stopped.");
        // Deliberately no SQLite connection: a service manager request is the only CLI capability.
    }
}

public sealed class DatabaseOwnershipException(string message, Exception? inner = null) : InvalidOperationException(message, inner);
public sealed class DatabasePathException(string message) : ArgumentException(message);
public sealed class SchemaCompatibilityException(string message) : InvalidOperationException(message);

public static class DatabasePathPolicy
{
    public static string ResolveLocal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (databasePath.StartsWith("\\\\", StringComparison.Ordinal) || databasePath.StartsWith("//", StringComparison.Ordinal))
            throw new DatabasePathException($"Network database paths are not supported: '{databasePath}'.");
        string canonical = Path.GetFullPath(databasePath);
        if (OperatingSystem.IsWindows() && Regex.IsMatch(canonical, "^[A-Za-z]:\\\\"))
        {
            try
            {
                DriveInfo drive = new(Path.GetPathRoot(canonical)!);
                if (drive.DriveType is not DriveType.Fixed) throw new DatabasePathException($"Database paths must be on a fixed local volume: '{databasePath}'.");
            }
            catch (IOException)
            {
                throw new DatabasePathException($"Database path is not on an available fixed local volume: '{databasePath}'.");
            }
        }
        string? directory = Path.GetDirectoryName(canonical);
        if (string.IsNullOrEmpty(directory)) throw new DatabasePathException("Database path must include a local directory.");
        ValidateNoReparsePoint(directory);
        return canonical;
    }
    private static void ValidateNoReparsePoint(string directory)
    {
        DirectoryInfo? current = new(directory);
        while (current is not null)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new DatabasePathException($"Database and lock paths may not traverse reparse points: '{current.FullName}'.");
            current = current.Parent;
        }
    }
}

public sealed record Migration(string Id, IReadOnlyList<string> Statements)
{
    public string Checksum { get; } = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Statements))));
}
public interface IMigrationFaultInjector { void At(MigrationFaultPoint point, string migrationId, int statementIndex = -1); }
public sealed class MigrationInterruptedException(string migrationId, MigrationFaultPoint point, int statementIndex = -1) : Exception($"Migration '{migrationId}' was interrupted at {point} ({statementIndex}).");

public static class DatabaseMigrationSets
{
    public static IReadOnlyList<Migration> For(DatabaseRole role) => role switch
    {
        DatabaseRole.Config => Config.ConfigMigrations.All,
        DatabaseRole.AuditWeb => AuditWeb.AuditWebMigrations.All,
        DatabaseRole.HistorianCatalog => Historian.HistorianMigrations.Catalog,
        DatabaseRole.HistorianPartition => Historian.HistorianMigrations.Partition,
        DatabaseRole.AuditRuntime => AuditRuntime.AuditRuntimeMigrations.All,
        DatabaseRole.Alarms => Alarms.AlarmsMigrations.All,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}

public sealed class SqliteDatabaseMigrator
{
    private readonly string _databasePath; private readonly DatabaseRole _role; private readonly IReadOnlyList<Migration> _migrations; private readonly IServiceIdentity _identity; private readonly IMigrationFaultInjector? _fault;
    public SqliteDatabaseMigrator(string databasePath, DatabaseRole role, IReadOnlyList<Migration> migrations, IServiceIdentity identity, IMigrationFaultInjector? fault = null)
    {
        _databasePath = DatabasePathPolicy.ResolveLocal(databasePath); _role = role; _identity = identity; _fault = fault;
        ValidateContract(migrations); _migrations = migrations.ToArray();
    }
    public void Migrate()
    {
        OsServiceIdentity ownerIdentity = DatabaseOwnership.DemandOwner(_role, _identity);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        WindowsDatabaseAccessPolicy.Apply(_databasePath, ownerIdentity);
        string lockPath = DatabasePathPolicy.ResolveLocal(_databasePath + ".migration.lock");
        using FileStream migrationLock = AcquireExclusiveLock(lockPath);
        using SqliteConnection connection = new($"Data Source={_databasePath};Mode=ReadWriteCreate;Pooling=False"); connection.Open();
        ConfigureDurability(connection); EnsureLedger(connection); ValidateLedger(connection);
        foreach (Migration migration in _migrations) ApplyIfNeeded(connection, migration);
    }
    private static void ValidateContract(IReadOnlyList<Migration> migrations)
    {
        string? prior = null; var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Migration migration in migrations)
        {
            if (!Regex.IsMatch(migration.Id, "^[0-9]{14}_[a-z0-9_]+$") || migration.Statements.Count == 0 || !ids.Add(migration.Id) || (prior is not null && string.CompareOrdinal(prior, migration.Id) >= 0))
                throw new SchemaCompatibilityException("Migration contract requires unique, strictly increasing timestamp IDs and non-empty statements.");
            prior = migration.Id;
        }
    }
    private static FileStream AcquireExclusiveLock(string lockPath) { try { return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException e) { throw new DatabaseOwnershipException($"Another process owns migration lock for '{lockPath}'.", e); } }
    private static void ConfigureDurability(SqliteConnection connection) { Execute(connection, "PRAGMA journal_mode=WAL"); Execute(connection, "PRAGMA synchronous=FULL"); }
    private static void EnsureLedger(SqliteConnection connection) => Execute(connection, "CREATE TABLE IF NOT EXISTS __scada_migration_ledger (migration_id TEXT PRIMARY KEY NOT NULL, checksum TEXT NOT NULL, applied_utc TEXT NOT NULL)");
    private void ValidateLedger(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT migration_id, checksum FROM __scada_migration_ledger ORDER BY migration_id";
        using SqliteDataReader rows = command.ExecuteReader(); var byId = _migrations.ToDictionary(x => x.Id, StringComparer.Ordinal);
        while (rows.Read()) { string id = rows.GetString(0); if (!byId.TryGetValue(id, out Migration? known)) throw new SchemaCompatibilityException($"Database contains unknown, removed, or newer migration '{id}'."); if (!string.Equals(rows.GetString(1), known.Checksum, StringComparison.Ordinal)) throw new SchemaCompatibilityException($"Migration '{id}' checksum does not match the database ledger."); }
    }
    private void ApplyIfNeeded(SqliteConnection connection, Migration migration)
    {
        using SqliteCommand exists = connection.CreateCommand(); exists.CommandText = "SELECT COUNT(*) FROM __scada_migration_ledger WHERE migration_id=$id"; exists.Parameters.AddWithValue("$id", migration.Id); if (Convert.ToInt64(exists.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != 0) return;
        using SqliteTransaction tx = connection.BeginTransaction();
        for (int i = 0; i < migration.Statements.Count; i++) { _fault?.At(MigrationFaultPoint.BeforeStatement, migration.Id, i); using SqliteCommand statement = connection.CreateCommand(); statement.Transaction = tx; statement.CommandText = migration.Statements[i]; statement.ExecuteNonQuery(); _fault?.At(MigrationFaultPoint.AfterStatement, migration.Id, i); }
        _fault?.At(MigrationFaultPoint.BeforeLedgerInsert, migration.Id); using SqliteCommand ledger = connection.CreateCommand(); ledger.Transaction = tx; ledger.CommandText = "INSERT INTO __scada_migration_ledger (migration_id, checksum, applied_utc) VALUES ($id,$checksum,$utc)"; ledger.Parameters.AddWithValue("$id", migration.Id); ledger.Parameters.AddWithValue("$checksum", migration.Checksum); ledger.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O")); ledger.ExecuteNonQuery(); _fault?.At(MigrationFaultPoint.AfterLedgerInsert, migration.Id); _fault?.At(MigrationFaultPoint.BeforeCommit, migration.Id); tx.Commit(); _fault?.At(MigrationFaultPoint.AfterCommit, migration.Id);
    }
    private static void Execute(SqliteConnection c, string sql) { using SqliteCommand cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
}
