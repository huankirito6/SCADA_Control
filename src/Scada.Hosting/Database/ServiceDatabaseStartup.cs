using Scada.Infrastructure.Sqlite.Migrations;
using Scada.Infrastructure.Sqlite.Migrations.Alarms;
using Scada.Infrastructure.Sqlite.Migrations.AuditRuntime;
using Scada.Infrastructure.Sqlite.Migrations.AuditWeb;
using Scada.Infrastructure.Sqlite.Migrations.Config;
using Scada.Infrastructure.Sqlite.Migrations.Historian;
using Scada.Deployment;

namespace Scada.Hosting.Database;

public static class WebServiceDatabaseStartup
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void MigrateOwnedDatabases(string root, ServiceIdentityPolicy identityPolicy)
    {
        OsServiceIdentity identity = ResolveOwner(identityPolicy, ServiceIdentity.Web);
        string ownerRoot = Path.Combine(root, "web");
        Migrate(ownerRoot, "config.db", DatabaseRole.Config, ConfigMigrations.All, identity);
        Migrate(ownerRoot, "audit-web.db", DatabaseRole.AuditWeb, AuditWebMigrations.All, identity);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static OsServiceIdentity ResolveOwner(ServiceIdentityPolicy identityPolicy, ServiceIdentity expected)
    {
        OsServiceIdentity identity = identityPolicy.ResolveCurrentProcess();
        if (identity.Identity != expected) throw new Scada.Infrastructure.Sqlite.Migrations.DatabaseOwnershipException($"Current Windows service SID is not configured for {expected} database ownership.");
        return identity;
    }

    private static void Migrate(string root, string fileName, DatabaseRole role, IReadOnlyList<Migration> migrations, OsServiceIdentity identity)
        => new SqliteDatabaseMigrator(Path.Combine(root, fileName), role, migrations, identity).Migrate();
}

public static class RuntimeServiceDatabaseStartup
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void MigrateOwnedDatabases(string root, ServiceIdentityPolicy identityPolicy)
    {
        OsServiceIdentity identity = ResolveOwner(identityPolicy);
        string ownerRoot = Path.Combine(root, "runtime");
        new SqliteDatabaseMigrator(Path.Combine(ownerRoot, "historian-catalog.db"), DatabaseRole.HistorianCatalog, HistorianMigrations.Catalog, identity).Migrate();
        string partitionDirectory = Path.Combine(ownerRoot, "historian-partitions");
        Directory.CreateDirectory(partitionDirectory);
        foreach (string partition in HistorianPartitionPaths.Existing(ownerRoot)) MigratePartition(partition, identity);
        new SqliteDatabaseMigrator(Path.Combine(ownerRoot, "audit-runtime.db"), DatabaseRole.AuditRuntime, AuditRuntimeMigrations.All, identity).Migrate();
        new SqliteDatabaseMigrator(Path.Combine(ownerRoot, "alarms.db"), DatabaseRole.Alarms, AlarmsMigrations.All, identity).Migrate();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static string OpenPartition(string root, string partitionId, ServiceIdentityPolicy identityPolicy)
    {
        OsServiceIdentity identity = ResolveOwner(identityPolicy);
        string path = HistorianPartitionPaths.For(Path.Combine(root, "runtime"), partitionId);
        MigratePartition(path, identity);
        return path;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static OsServiceIdentity ResolveOwner(ServiceIdentityPolicy identityPolicy)
    {
        OsServiceIdentity identity = identityPolicy.ResolveCurrentProcess();
        if (identity.Identity != ServiceIdentity.Runtime) throw new Scada.Infrastructure.Sqlite.Migrations.DatabaseOwnershipException("Current Windows service SID is not configured for Runtime database ownership.");
        return identity;
    }

    private static void MigratePartition(string path, OsServiceIdentity identity)
        => new SqliteDatabaseMigrator(path, DatabaseRole.HistorianPartition, HistorianMigrations.Partition, identity).Migrate();
}

internal static class HistorianPartitionPaths
{
    private const string DirectoryName = "historian-partitions";
    internal static IEnumerable<string> Existing(string root)
    {
        string directory = Path.Combine(root, DirectoryName);
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.db", SearchOption.TopDirectoryOnly) : [];
    }

    internal static string For(string root, string partitionId)
    {
        if (string.IsNullOrWhiteSpace(partitionId) || partitionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || partitionId.Contains(Path.DirectorySeparatorChar) || partitionId.Contains(Path.AltDirectorySeparatorChar))
            throw new DatabasePathException("Historian partition ID must be a simple file-name component.");
        return Path.Combine(root, DirectoryName, partitionId + ".db");
    }
}
