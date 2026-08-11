namespace Scada.Runtime.Time;

public sealed record ClockState(long HighWaterLogicalTsUs, long StateRevision)
{
    public bool IsValid => HighWaterLogicalTsUs >= 0 && StateRevision >= 0;
}

public interface IClockStateStore
{
    ClockState? Load();

    void Save(ClockState state);
}

public enum ClockDeviationKind
{
    ClockReanchored,
    ClockDeviationDetected,
}

public sealed record ClockDeviation(
    ClockDeviationKind Kind,
    Guid BootId,
    long PriorAnchorLogicalTsUs,
    long NewAnchorLogicalTsUs,
    long ObservedWallLogicalTsUs,
    long MonotonicTicks,
    long StateRevision);

public sealed class ClockUnavailableException : InvalidOperationException
{
    public ClockUnavailableException() : base("The logical clock is degraded and cannot issue samples.") { }
}