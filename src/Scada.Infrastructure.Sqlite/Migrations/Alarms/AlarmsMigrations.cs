namespace Scada.Infrastructure.Sqlite.Migrations.Alarms;

public static class AlarmsMigrations
{
    public static readonly IReadOnlyList<Migration> All = [new("20260812000000_create_alarm_events", ["CREATE TABLE alarm_events (alarm_id TEXT PRIMARY KEY NOT NULL, occurred_utc TEXT NOT NULL, state TEXT NOT NULL)"])];
}
