namespace Scada.Infrastructure.Sqlite.Migrations.AuditWeb;

public static class AuditWebMigrations
{
    public static readonly IReadOnlyList<Migration> All = [new("20260812000000_create_web_audit", ["CREATE TABLE web_audit (event_id TEXT PRIMARY KEY NOT NULL, occurred_utc TEXT NOT NULL, payload TEXT NOT NULL)"])];
}
