namespace Scada.Infrastructure.Sqlite.Migrations.AuditRuntime;

public static class AuditRuntimeMigrations
{
    public static readonly IReadOnlyList<Migration> All = [new("20260812000000_create_runtime_audit", ["CREATE TABLE runtime_audit (event_id TEXT PRIMARY KEY NOT NULL, occurred_utc TEXT NOT NULL, payload TEXT NOT NULL)"])];
}
