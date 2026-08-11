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
        var uncertain = new TagQuality(QualitySeverity.Uncertain, QualityReason.LastKnown, null);
        var bad = new TagQuality(QualitySeverity.Bad, QualityReason.CommFail, null);

        var aggregate = TagQuality.Combine(uncertain, bad);

        Assert.Equal(QualitySeverity.Bad, aggregate.Severity);
        Assert.Equal(QualityReason.LastKnown | QualityReason.CommFail, aggregate.Reasons);
        Assert.Null(aggregate.NativeStatus);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(42U, null, 42U)]
    [InlineData(null, 7U, 7U)]
    [InlineData(42U, 42U, 42U)]
    public void CombinePreservesOnlyUnambiguousNativeStatus(uint? leftStatus, uint? rightStatus, uint? expected)
    {
        var left = new TagQuality(QualitySeverity.Good, QualityReason.LastKnown, leftStatus);
        var right = new TagQuality(QualitySeverity.Uncertain, QualityReason.CommFail, rightStatus);

        Assert.Equal(expected, TagQuality.Combine(left, right).NativeStatus);
    }

    [Fact]
    public void CombineRejectsConflictingNativeStatuses()
    {
        var left = new TagQuality(QualitySeverity.Good, QualityReason.LastKnown, 42U);
        var right = new TagQuality(QualitySeverity.Uncertain, QualityReason.CommFail, 7U);

        var exception = Assert.Throws<InvalidOperationException>(() => TagQuality.Combine(left, right));
        Assert.Contains("conflicting native quality statuses", exception.Message, StringComparison.Ordinal);
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
