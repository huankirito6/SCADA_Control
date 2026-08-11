using Scada.Application.Time;
using Scada.Domain.Tags;

namespace Scada.Runtime.Time;

public sealed class MonotonicLogicalClock : ILogicalClock
{
    public const long ForwardReanchorThresholdUs = 1_000_000;
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly IClockStateStore _store;
    private readonly Guid _bootId;
    private long _anchorLogicalUs;
    private long _anchorTicks;
    private long _highWaterUs;
    private long _revision;
    private ClockHealth _health;

    public MonotonicLogicalClock(TimeProvider timeProvider, IClockStateStore stateStore, Guid? bootId = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(stateStore);
        _time = timeProvider;
        _store = stateStore;
        _bootId = bootId ?? Guid.NewGuid();
        try
        {
            var wall = ToUnixMicroseconds(_time.GetUtcNow());
            var ticks = _time.GetTimestamp();
            var state = _store.Load();
            if (state is { IsValid: false }) throw new InvalidDataException("Invalid clock state.");
            _highWaterUs = state?.HighWaterLogicalTsUs ?? checked(wall - 1);
            _revision = state?.StateRevision ?? 0;
            _anchorLogicalUs = wall;
            _anchorTicks = ticks;
            _health = _time.TimestampFrequency > 0 ? ClockHealth.Healthy : ClockHealth.ClockDegraded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or OverflowException or ArgumentOutOfRangeException)
        {
            _health = ClockHealth.ClockDegraded;
        }
    }

    public ClockHealth Health { get { lock (_gate) return _health; } }

    public event Action<ClockDeviation>? Deviation;

    public SampleStamp Next(long? sourceTsUs = null)
    {
        lock (_gate)
        {
            if (_health != ClockHealth.Healthy || _time.TimestampFrequency <= 0) return Fail();
            try
            {
                var ticks = _time.GetTimestamp();
                var wall = ToUnixMicroseconds(_time.GetUtcNow());
                var elapsed = ConvertTicksToMicroseconds(checked(ticks - _anchorTicks), _time.TimestampFrequency);
                var predicted = checked(_anchorLogicalUs + elapsed);
                ClockDeviation? eventData = null;
                if (checked(wall - predicted) >= ForwardReanchorThresholdUs)
                {
                    var nextRevision = checked(_revision + 1);
                    eventData = new(ClockDeviationKind.ClockReanchored, _bootId, _anchorLogicalUs, wall, wall, ticks, nextRevision);
                    predicted = wall;
                    _anchorLogicalUs = wall;
                    _anchorTicks = ticks;
                    _revision = nextRevision;
                }
                var logical = predicted > _highWaterUs ? predicted : checked(_highWaterUs + 1);
                var nextState = new ClockState(logical, _revision);
                _store.Save(nextState);
                if (eventData is not null)
                {
                    try { Deviation?.Invoke(eventData); }
                    catch { return Fail(); }
                }
                _highWaterUs = logical;
                return new SampleStamp(logical, sourceTsUs, ticks, _bootId, _revision);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException or ArgumentOutOfRangeException)
            {
                return Fail();
            }
        }
    }

    private SampleStamp Fail()
    {
        _health = ClockHealth.ClockDegraded;
        throw new ClockUnavailableException();
    }

    public static long ConvertTicksToMicroseconds(long elapsedTicks, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        var value = ((Int128)elapsedTicks * 1_000_000) / frequency;
        return checked((long)value);
    }

    private static long ToUnixMicroseconds(DateTimeOffset value) => checked((value.Ticks - DateTimeOffset.UnixEpoch.Ticks) / 10);
}