using Scada.Application.Time;
using Scada.Runtime.Time;

namespace Scada.Runtime.Tests.Time;

public sealed class MonotonicLogicalClockTests
{
    [Fact]
    public void InvalidFrequencyFailsClosedWithoutSample()
    {
        var clock = new MonotonicLogicalClock(new FakeTimeProvider(1, 0, 0), new InMemoryClockStateStore());
        Assert.Equal(ClockHealth.ClockDegraded, clock.Health);
        Assert.Throws<ClockUnavailableException>(() => clock.Next());
    }

    [Theory]
    [InlineData(3L, 2_000_000L, 1L)]
    [InlineData(-3L, 2_000_000L, -1L)]
    [InlineData(long.MaxValue, long.MaxValue, 1_000_000L)]
    public void TickConversionUsesInt128AndTruncatesTowardZero(long ticks, long frequency, long expected) =>
        Assert.Equal(expected, MonotonicLogicalClock.ConvertTicksToMicroseconds(ticks, frequency));

    [Fact]
    public void SaveFailureFailsClosedWithoutSample()
    {
        var clock = new MonotonicLogicalClock(new FakeTimeProvider(1, 0), new ThrowingClockStateStore());
        Assert.Throws<ClockUnavailableException>(() => clock.Next());
        Assert.Equal(ClockHealth.ClockDegraded, clock.Health);
    }

    [Fact]
    public void ThrowingDeviationSubscriberDoesNotAffectDurableReanchoredSample()
    {
        var store = new InMemoryClockStateStore();
        var time = new FakeTimeProvider(1_000_000, 0);
        var clock = new MonotonicLogicalClock(time, store);
        ClockDeviation? received = null;
        clock.Deviation += _ => throw new InvalidOperationException();
        clock.Deviation += deviation => received = deviation;
        _ = clock.Next();
        time.Set(2_000_001, 1);

        var sample = clock.Next();

        Assert.Equal(ClockHealth.Healthy, clock.Health);
        Assert.Equal(new ClockState(sample.LogicalTsUs, sample.StateRevision), store.State);
        Assert.NotNull(received);
        Assert.Equal(sample.StateRevision, received.StateRevision);
    }

    [Fact]
    public void ConcurrentIssuanceIsStrictlyUnique()
    {
        var clock = Create(new FakeTimeProvider(1_000_000, 0));
        var values = new System.Collections.Concurrent.ConcurrentBag<long>();
        Parallel.For(0, 100, _ => values.Add(clock.Next().LogicalTsUs));
        Assert.Equal(100, values.Distinct().Count());
    }

    [Fact]
    public void FileStoreCreatesReplacesRestartsAndCleansOwnedTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "clock.json");
            var store = new FileClockStateStore(path);
            store.Save(new ClockState(10, 1));
            Assert.Equal(new ClockState(10, 1), store.Load());
            store.Save(new ClockState(11, 2));
            Assert.Equal(new ClockState(11, 2), new FileClockStateStore(path).Load());
            Assert.Empty(Directory.GetFiles(directory, ".clock.json.*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not-json")]
    public void CorruptOrNullCheckpointStartsDegraded(string content)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, content);
            var clock = new MonotonicLogicalClock(new FakeTimeProvider(1, 0), new FileClockStateStore(path));
            Assert.Equal(ClockHealth.ClockDegraded, clock.Health);
            Assert.Throws<ClockUnavailableException>(() => clock.Next());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NtpBackwardStepDoesNotMoveLogicalTimeBackward()
    {
        var time = new FakeTimeProvider(utcUs: 2_000_000, timestamp: 0);
        var clock = Create(time);
        var first = clock.Next();
        time.Set(utcUs: 1_000_000, timestamp: 10);

        var second = clock.Next();

        Assert.True(second.LogicalTsUs > first.LogicalTsUs, "A backward NTP step must not move logical time backward.");
    }

    [Fact]
    public void NtpForwardStepReanchorsLogicalTimeForward()
    {
        var time = new FakeTimeProvider(utcUs: 2_000_000, timestamp: 0);
        var clock = Create(time);
        ClockDeviation? deviation = null;
        clock.Deviation += observed => deviation = observed;
        _ = clock.Next();
        time.Set(utcUs: 4_000_000, timestamp: 10);

        var second = clock.Next();

        Assert.Equal(4_000_000, second.LogicalTsUs);
        Assert.NotNull(deviation);
        Assert.Equal(second.StateRevision, deviation.StateRevision);
        Assert.Equal(ClockDeviationKind.ClockReanchored, deviation.Kind);
    }

    [Fact]
    public void EverySuccessfulSampleAdvancesAndPersistsExactlyOneRevision()
    {
        var store = new InMemoryClockStateStore();
        var clock = new MonotonicLogicalClock(new FakeTimeProvider(2_000_000, 0), store, Guid.Empty);

        var first = clock.Next();
        Assert.Equal(new ClockState(first.LogicalTsUs, first.StateRevision), store.State);

        var second = clock.Next();
        Assert.Equal(first.StateRevision + 1, second.StateRevision);
        Assert.Equal(new ClockState(second.LogicalTsUs, second.StateRevision), store.State);
    }

    [Fact]
    public void SameMicrosecondIssuanceProducesStrictlyIncreasingLogicalTime()
    {
        var time = new FakeTimeProvider(utcUs: 2_000_000, timestamp: 0);
        var clock = Create(time);
        var first = clock.Next();

        var second = clock.Next();

        Assert.True(second.LogicalTsUs > first.LogicalTsUs, "Samples issued in one microsecond must remain ordered.");
    }

    [Fact]
    public void RestartBelowPersistedHighWaterContinuesAboveCheckpoint()
    {
        var state = new InMemoryClockStateStore(new ClockState(9_000_000, 4));
        var time = new FakeTimeProvider(utcUs: 2_000_000, timestamp: 0);

        var sample = new MonotonicLogicalClock(time, state, Guid.Empty).Next();

        Assert.True(sample.LogicalTsUs > 9_000_000, "Restart must issue above the persisted high-water mark.");
    }

    [Fact]
    public void OutOfOrderSourceTimestampsAreRetainedAsMetadataNotOrdering()
    {
        var time = new FakeTimeProvider(utcUs: 2_000_000, timestamp: 0);
        var clock = Create(time);
        var first = clock.Next(sourceTsUs: 9_000);
        time.Set(utcUs: 1_000_000, timestamp: 1);

        var second = clock.Next(sourceTsUs: 1_000);

        Assert.True(second.LogicalTsUs > first.LogicalTsUs, "Out-of-order source timestamps must not control logical ordering.");
        Assert.Equal(1_000, second.SourceTsUs);
    }

    private static MonotonicLogicalClock Create(FakeTimeProvider time) =>
        new(time, new InMemoryClockStateStore(), Guid.Empty);

    private sealed class InMemoryClockStateStore(ClockState? initial = null) : IClockStateStore
    {
        public ClockState? State { get; private set; } = initial;
        public ClockState? Load() => State;
        public void Save(ClockState state) => State = state;
    }

    private sealed class ThrowingClockStateStore : IClockStateStore
    {
        public ClockState? Load() => null;
        public void Save(ClockState state) => throw new IOException();
    }

    private sealed class FakeTimeProvider(long utcUs, long timestamp, long frequency = 1_000_000) : TimeProvider
    {
        private long _utcUs = utcUs;
        private long _timestamp = timestamp;
        public override long TimestampFrequency => frequency;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(checked(_utcUs * 10));
        public override long GetTimestamp() => _timestamp;
        public void Set(long utcUs, long timestamp) { _utcUs = utcUs; _timestamp = timestamp; }
    }
}