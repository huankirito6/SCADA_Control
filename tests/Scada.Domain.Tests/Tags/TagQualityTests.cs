using Scada.Domain.Tags;

namespace Scada.Domain.Tests.Tags;

public sealed class TagQualityTests
{
    public static IEnumerable<object[]> SeverityPairs()
    {
        foreach (var left in new[] { QualitySeverity.Good, QualitySeverity.Uncertain, QualitySeverity.Bad })
        {
            foreach (var right in new[] { QualitySeverity.Good, QualitySeverity.Uncertain, QualitySeverity.Bad })
            {
                yield return new object[] { left, right, (QualitySeverity)Math.Max((byte)left, (byte)right) };
            }
        }
    }

    [Theory]
    [MemberData(nameof(SeverityPairs))]
    public void WorstSeverityReturnsDefinedOrdinalMaximum(QualitySeverity left, QualitySeverity right, QualitySeverity expected) =>
        Assert.Equal(expected, TagQuality.WorstSeverity(left, right));

    [Fact]
    public void CombineReturnsWorstSeverityAndUnionsReasonsWithoutEncodingSeverity()
    {
        var uncertain = new TagQuality(QualitySeverity.Uncertain, QualityReason.LastKnown, 42U);
        var bad = new TagQuality(QualitySeverity.Bad, QualityReason.CommFail, 7U);

        var aggregate = TagQuality.Combine(uncertain, bad);

        Assert.Equal(QualitySeverity.Bad, aggregate.Severity);
        Assert.Equal(QualityReason.LastKnown | QualityReason.CommFail, aggregate.Reasons);
        Assert.Null(aggregate.NativeStatus);
    }

    [Fact]
    public void ConstructorPreservesNativeStatusWithoutTreatingItAsAReason() =>
        Assert.Equal(0xDEADBEEFU, new TagQuality(QualitySeverity.Good, QualityReason.None, 0xDEADBEEFU).NativeStatus);

    [Fact]
    public void ConstructorRejectsUndefinedSeverity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TagQuality((QualitySeverity)3, QualityReason.None, null));

    [Fact]
    public void ConstructorRejectsUnknownReasonBits() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TagQuality(QualitySeverity.Good, (QualityReason)512, null));

    [Fact]
    public void WorstSeverityRejectsUndefinedSeverity() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TagQuality.WorstSeverity(QualitySeverity.Good, (QualitySeverity)3));
}
