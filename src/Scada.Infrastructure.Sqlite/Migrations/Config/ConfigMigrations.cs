namespace Scada.Infrastructure.Sqlite.Migrations.Config;

public static class ConfigMigrations
{
    public static readonly IReadOnlyList<Migration> All = [new("20260812000000_create_config_versions", ["CREATE TABLE config_versions (version_id TEXT PRIMARY KEY NOT NULL, canonical_json TEXT NOT NULL, schema_version INTEGER NOT NULL)"])];
}
