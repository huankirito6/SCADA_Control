namespace Scada.Infrastructure.Sqlite.Migrations.Historian;

public static class HistorianMigrations
{
    public static readonly IReadOnlyList<Migration> Catalog = [new("20260812000000_create_historian_catalog", ["CREATE TABLE historian_partitions (partition_id TEXT PRIMARY KEY NOT NULL, start_utc TEXT NOT NULL)"])];
    public static readonly IReadOnlyList<Migration> Partition = [new("20260812000000_create_historian_samples", ["CREATE TABLE historian_samples (ingest_id TEXT PRIMARY KEY NOT NULL, occurred_utc TEXT NOT NULL, value REAL NOT NULL)"])];
}
