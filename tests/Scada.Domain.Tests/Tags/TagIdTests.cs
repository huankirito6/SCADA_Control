using Scada.Domain.Tags;

namespace Scada.Domain.Tests.Tags;

public sealed class TagIdTests
{
    [Fact]
    public void DefaultIsExplicitlyInvalid()
    {
        var value = default(TagId);

        Assert.False(value.IsValid);
        var valueException = Assert.Throws<InvalidOperationException>(() => _ = value.Value);
        Assert.Contains("invalid", valueException.Message, StringComparison.OrdinalIgnoreCase);

        var requireException = Assert.Throws<InvalidOperationException>(() => value.RequireValid());
        Assert.Contains("invalid", requireException.Message, StringComparison.OrdinalIgnoreCase);

        Assert.NotEqual(value, new TagId(1));
    }

    [Fact]
    public void PositiveSigned64ValueIsPreserved()
    {
        var value = new TagId(long.MaxValue);

        Assert.True(value.IsValid);
        Assert.Equal(long.MaxValue, value.Value);
        Assert.Equal(long.MaxValue, value.RequireValid());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void NonPositiveValuesAreRejected(long value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TagId(value));
}
