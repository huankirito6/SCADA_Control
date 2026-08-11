namespace Scada.Domain.Tags;

public readonly record struct SampleStamp(
    long LogicalTsUs,
    long? SourceTsUs,
    long MonotonicTicks,
    Guid BootId,
    long StateRevision);