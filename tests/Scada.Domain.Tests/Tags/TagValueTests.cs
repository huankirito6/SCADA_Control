using Scada.Domain.Tags;

namespace Scada.Domain.Tests.Tags;

public sealed class TagValueTests
{
    [Fact]
    public void FactoriesPreserveEachSupportedKindAndPayload()
    {
        var boolean = TagValue.FromBool(true);
        var signed16 = TagValue.FromInt16(-12);
        var signed32 = TagValue.FromInt32(-1234);
        var signed64 = TagValue.FromInt64(-12345678901234L);
        var real32 = TagValue.FromFloat32(1.25F);
        var real64 = TagValue.FromFloat64(2.5D);
        var text = TagValue.FromString("value");
        var enumeration = TagValue.FromEnum("Running");

        Assert.Equal(TagValueKind.Boolean, boolean.Kind);
        Assert.True(boolean.AsBool());
        Assert.Equal(TagValueKind.Signed16, signed16.Kind);
        Assert.Equal((short)-12, signed16.AsInt16());
        Assert.Equal(TagValueKind.Signed32, signed32.Kind);
        Assert.Equal(-1234, signed32.AsInt32());
        Assert.Equal(TagValueKind.Signed64, signed64.Kind);
        Assert.Equal(-12345678901234L, signed64.AsInt64());
        Assert.Equal(TagValueKind.Real32, real32.Kind);
        Assert.Equal(1.25F, real32.AsFloat32());
        Assert.Equal(TagValueKind.Real64, real64.Kind);
        Assert.Equal(2.5D, real64.AsFloat64());
        Assert.Equal(TagValueKind.Text, text.Kind);
        Assert.Equal("value", text.AsString());
        Assert.Equal(TagValueKind.Enumeration, enumeration.Kind);
        Assert.Equal("Running", enumeration.AsEnum());
    }

    [Fact]
    public void WrongAccessorRejectsInsteadOfCoercing() =>
        Assert.Throws<InvalidOperationException>(() => TagValue.FromInt32(1).AsString());

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Float32FactoryRejectsNonFiniteValues(float value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TagValue.FromFloat32(value));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Float64FactoryRejectsNonFiniteValues(double value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TagValue.FromFloat64(value));

    [Fact]
    public void StringAndEnumFactoriesRejectNullPayloads()
    {
        Assert.Throws<ArgumentNullException>(() => TagValue.FromString(null!));
        Assert.Throws<ArgumentNullException>(() => TagValue.FromEnum(null!));
    }

    [Fact]
    public void Int64WireValueUsesInvariantDecimalStringBeyondJavaScriptSafeInteger()
    {
        var value = TagValue.FromInt64(9_007_199_254_740_993L);

        var wireValue = value.ToWireValue();

        Assert.IsType<string>(wireValue);
        Assert.Equal("9007199254740993", wireValue);
    }
}
