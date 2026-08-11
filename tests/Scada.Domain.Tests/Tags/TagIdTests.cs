using Scada.Domain.Tags;

namespace Scada.Domain.Tests.Tags;

public sealed class TagIdTests
{
    [Fact]
    public void PositiveSigned64ValueIsPreserved() => Assert.Equal(long.MaxValue, new TagId(long.MaxValue).Value);

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NonPositiveValuesAreRejected(long value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TagId(value));
}
