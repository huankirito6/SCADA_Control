using Scada.Domain.Tags;

namespace Scada.Application.Time;

public interface ILogicalClock
{
#pragma warning disable CA1716 // Required by the published Task 5 contract.
    SampleStamp Next(long? sourceTsUs = null);
#pragma warning restore CA1716

    ClockHealth Health { get; }
}

public enum ClockHealth
{
    Healthy,
    ClockDegraded,
}