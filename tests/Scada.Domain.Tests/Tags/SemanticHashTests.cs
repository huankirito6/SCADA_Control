using Scada.Domain.Tags;

namespace Scada.Domain.Tests.Tags;

public sealed class SemanticHashTests
{
    [Theory]
    [InlineData(null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "digest")]
    [InlineData("", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "digest")]
    [InlineData(" ", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "digest")]
    [InlineData("not-a-hash", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "digest")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "digest")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", null, "digest")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "", "digest")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", " ", "digest")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", null)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", " ")]
    public void DirectConstructionRejectsInvalidHashesOrDigest(string? source, string? meaning, string? digest) =>
        Assert.Throws<ArgumentException>(() => new TagSemanticIdentity(source!, meaning!, digest!));

    [Fact]
    public void DirectConstructionAcceptsCanonicalHashesAndDigest()
    {
        var identity = new TagSemanticIdentity(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "physical");

        Assert.Equal(64, identity.SourceBindingHash.Length);
        Assert.Equal("physical", identity.PhysicalTargetDigest);
    }

    [Fact]
    public void SourceBindingHashChangesForEveryBindingField()
    {
        var baseline = TagSemanticIdentity.Create("opc.tcp://line-1", "ns=2", "Pump.Speed", "Float64", 0.1D, "rpm", "physical-digest");

        Assert.NotEqual(baseline.SourceBindingHash, TagSemanticIdentity.Create("opc.tcp://line-2", "ns=2", "Pump.Speed", "Float64", 0.1D, "rpm", "physical-digest").SourceBindingHash);
        Assert.NotEqual(baseline.SourceBindingHash, TagSemanticIdentity.Create("opc.tcp://line-1", "ns=3", "Pump.Speed", "Float64", 0.1D, "rpm", "physical-digest").SourceBindingHash);
        Assert.NotEqual(baseline.SourceBindingHash, TagSemanticIdentity.Create("opc.tcp://line-1", "ns=2", "Pump.State", "Float64", 0.1D, "rpm", "physical-digest").SourceBindingHash);
    }

    [Fact]
    public void ValueMeaningHashChangesForEveryMeaningField()
    {
        var baseline = TagSemanticIdentity.Create("opc.tcp://line-1", "ns=2", "Pump.Speed", "Float64", 0.1D, "rpm", "physical-digest");

        Assert.NotEqual(baseline.ValueMeaningHash, TagSemanticIdentity.Create("opc.tcp://line-1", "ns=2", "Pump.Speed", "Int32", 0.1D, "rpm", "physical-digest").ValueMeaningHash);
        Assert.NotEqual(baseline.ValueMeaningHash, TagSemanticIdentity.Create("opc.tcp://line-1", "ns=2", "Pump.Speed", "Float64", 0.2D, "rpm", "physical-digest").ValueMeaningHash);
        Assert.NotEqual(baseline.ValueMeaningHash, TagSemanticIdentity.Create("opc.tcp://line-1", "ns=2", "Pump.Speed", "Float64", 0.1D, "Hz", "physical-digest").ValueMeaningHash);
    }

    [Fact]
    public void LengthPrefixingSeparatesSameConcatenationCollisionVector()
    {
        var first = TagSemanticIdentity.Create("ab", "c", "d", "Int32", 1D, "bar", "digest");
        var second = TagSemanticIdentity.Create("a", "bc", "d", "Int32", 1D, "bar", "digest");

        Assert.NotEqual(first.SourceBindingHash, second.SourceBindingHash);
    }

    [Fact]
    public void HashesAreLowercaseSha256HexAndPhysicalDigestIsOpaque()
    {
        var identity = TagSemanticIdentity.Create("endpoint", "unit", "address", "Int64", 1.25D, "kg", "opaque:physical-digest");

        Assert.Matches("^[0-9a-f]{64}$", identity.SourceBindingHash);
        Assert.Matches("^[0-9a-f]{64}$", identity.ValueMeaningHash);
        Assert.Equal("opaque:physical-digest", identity.PhysicalTargetDigest);
    }

    [Fact]
    public void FactoryRejectsInvalidSemanticInputs()
    {
        Assert.Throws<ArgumentException>(() => TagSemanticIdentity.Create(" ", "unit", "address", "Int32", 1D, "bar", "digest"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TagSemanticIdentity.Create("endpoint", "unit", "address", "Int32", double.NaN, "bar", "digest"));
        Assert.Throws<ArgumentException>(() => TagSemanticIdentity.Create("endpoint", "unit", "address", "Int32", 1D, "bar", " "));
    }
}
