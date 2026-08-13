using System.Globalization;
using System.Security.Cryptography;
using Scada.Application.Scenes;
using Scada.Contracts.Scenes;

namespace Scada.Domain.Tests.Scenes;

public sealed class SceneCorpusTests
{
    private static readonly string CorpusDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../fixtures/scenes/schema-v1"));
    private static readonly string[] GeometryKinds = ["box", "points", "path", "link"];


    [Fact]
    public void CSharpValidatorAcceptsAndRejectsTheSharedCorpus()
    {
        var canonicalizer = new SceneCanonicalizer();

        foreach (var fixturePath in Directory.GetFiles(CorpusDirectory, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
        {
            using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixturePath));
            var expected = fixture.RootElement.GetProperty("expectedValid").GetBoolean();
            var sceneJson = fixture.RootElement.GetProperty("sceneJson").GetString();
            Assert.False(string.IsNullOrWhiteSpace(sceneJson), $"{Path.GetFileName(fixturePath)} must declare explicit raw sceneJson.");
            var result = canonicalizer.ValidateAndCanonicalize(sceneJson!);

            Assert.Equal(expected, result.IsValid);
        }
    }

    [Fact]
    public void SchemaManifestAndValidatorPolicyStayConsistent()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(LoadContractResource("Scada.Contracts.Scenes.scene.schema.json"));
        var manifest = SceneContract.Manifest;
        var schemaText = schema.RootElement.GetRawText();

        Assert.Equal(manifest.Limits.Elements, schema.RootElement.GetProperty("properties").GetProperty("elements").GetProperty("maxItems").GetInt32());
        var definitions = schema.RootElement.GetProperty("$defs");
        Assert.Equal(manifest.Limits.Vertices, definitions.GetProperty("points").GetProperty("properties").GetProperty("vertices").GetProperty("maxItems").GetInt32());
        Assert.Equal(manifest.Limits.Segments, definitions.GetProperty("path").GetProperty("properties").GetProperty("segments").GetProperty("maxItems").GetInt32());
        Assert.Contains($"{{1,{manifest.Limits.StringLength}}}", definitions.GetProperty("id").GetProperty("pattern").GetString(), StringComparison.Ordinal);
        foreach (var value in manifest.WidgetTypes.Concat(manifest.BindingTargets).Concat(manifest.ActionKinds).Concat(manifest.RoutingKinds)) Assert.Contains($"\"{value}\"", schemaText, StringComparison.Ordinal);
        foreach (var shape in manifest.ElementOwnership.Values) foreach (var property in shape) Assert.NotEmpty(property);
        foreach (var shape in manifest.Bindings.Values.Concat(manifest.Actions.Values)) { foreach (var property in shape.Required.Concat(shape.Allowed)) Assert.NotEmpty(property); }
    }

    [Fact]
    public void ManifestMakesEveryGeometryShapeAndBoundSchemaAuthoritative()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(LoadContractResource("Scada.Contracts.Scenes.scene.schema.json"));
        using var manifest = System.Text.Json.JsonDocument.Parse(LoadContractResource("Scada.Contracts.Scenes.widget-manifest.json"));
        var definitions = schema.RootElement.GetProperty("$defs");
        var geometry = manifest.RootElement.GetProperty("geometry");

        AssertShape(geometry.GetProperty("box"), definitions.GetProperty("box"));
        AssertShape(geometry.GetProperty("points"), definitions.GetProperty("points"));
        AssertShape(geometry.GetProperty("path"), definitions.GetProperty("path"));
        AssertShape(geometry.GetProperty("link"), definitions.GetProperty("link"));

        var point = geometry.GetProperty("point");
        Assert.Equal(point.GetProperty("required").EnumerateArray().Select(static x => x.GetString()), definitions.GetProperty("point").GetProperty("required").EnumerateArray().Select(static x => x.GetString()));
        Assert.Equal(point.GetProperty("allowed").EnumerateArray().Select(static x => x.GetString()).OrderBy(static x => x).ToArray(), definitions.GetProperty("point").GetProperty("properties").EnumerateObject().Select(static x => x.Name).OrderBy(static x => x).ToArray());

        var vertices = geometry.GetProperty("points").GetProperty("array");
        var schemaVertices = definitions.GetProperty("points").GetProperty("properties").GetProperty("vertices");
        Assert.Equal(vertices.GetProperty("minItems").GetInt32(), schemaVertices.GetProperty("minItems").GetInt32());
        Assert.Equal(vertices.GetProperty("maxItems").GetInt32(), schemaVertices.GetProperty("maxItems").GetInt32());

        var segments = geometry.GetProperty("path").GetProperty("array");
        var schemaSegments = definitions.GetProperty("path").GetProperty("properties").GetProperty("segments");
        Assert.Equal(segments.GetProperty("maxItems").GetInt32(), schemaSegments.GetProperty("maxItems").GetInt32());
        var segment = segments.GetProperty("item");
        Assert.Equal(segment.GetProperty("required").EnumerateArray().Select(static x => x.GetString()), schemaSegments.GetProperty("items").GetProperty("required").EnumerateArray().Select(static x => x.GetString()));
        Assert.Equal(segment.GetProperty("allowed").EnumerateArray().Select(static x => x.GetString()).OrderBy(static x => x).ToArray(), schemaSegments.GetProperty("items").GetProperty("properties").EnumerateObject().Select(static x => x.Name).OrderBy(static x => x).ToArray());
        Assert.Equal(segment.GetProperty("kinds").EnumerateArray().Select(static x => x.GetString()), schemaSegments.GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(static x => x.GetString()));
    }

    [Fact]
    public void ManifestMakesRootEnvelopeAndGeometryDiscriminatorsAuthoritative()
    {
        using var schema = System.Text.Json.JsonDocument.Parse(LoadContractResource("Scada.Contracts.Scenes.scene.schema.json"));
        using var manifest = System.Text.Json.JsonDocument.Parse(LoadContractResource("Scada.Contracts.Scenes.widget-manifest.json"));

        Assert.True(manifest.RootElement.TryGetProperty("scene", out var scene), "The manifest must own the root scene envelope policy.");
        Assert.True(manifest.RootElement.TryGetProperty("canvas", out var canvas), "The manifest must own the canvas and viewBox policy.");
        Assert.True(manifest.RootElement.TryGetProperty("symbol", out var symbol), "The manifest must own the symbol value shape.");
        Assert.Equal(scene.GetProperty("required").EnumerateArray().Select(static x => x.GetString()), schema.RootElement.GetProperty("required").EnumerateArray().Select(static x => x.GetString()));
        AssertShape(scene, schema.RootElement);
        Assert.Equal(scene.GetProperty("minimum").GetProperty("revision").GetInt32(), schema.RootElement.GetProperty("properties").GetProperty("revision").GetProperty("minimum").GetInt32());
        AssertShape(canvas, schema.RootElement.GetProperty("$defs").GetProperty("canvas"));
        AssertShape(canvas.GetProperty("viewBox"), schema.RootElement.GetProperty("$defs").GetProperty("canvas").GetProperty("properties").GetProperty("viewBox"));
        AssertShape(symbol, schema.RootElement.GetProperty("$defs").GetProperty("symbol"));

        var definitions = schema.RootElement.GetProperty("$defs");
        var geometry = manifest.RootElement.GetProperty("geometry");
        foreach (var kind in GeometryKinds)
        {
            var policy = geometry.GetProperty(kind);
            var schemaShape = definitions.GetProperty(kind);
            Assert.Equal(kind, policy.GetProperty("kind").GetString());
            Assert.Equal(kind, schemaShape.GetProperty("properties").GetProperty("kind").GetProperty("const").GetString());
        }

        AssertPositiveProperties(geometry.GetProperty("box"), definitions.GetProperty("box"));
        AssertPositiveProperties(canvas, definitions.GetProperty("canvas"));
        AssertPositiveProperties(canvas.GetProperty("viewBox"), definitions.GetProperty("canvas").GetProperty("properties").GetProperty("viewBox"));
    }

    private static void AssertShape(System.Text.Json.JsonElement manifestShape, System.Text.Json.JsonElement schemaShape)
    {
        Assert.Equal(manifestShape.GetProperty("required").EnumerateArray().Select(static x => x.GetString()), schemaShape.GetProperty("required").EnumerateArray().Select(static x => x.GetString()));
        Assert.Equal(manifestShape.GetProperty("allowed").EnumerateArray().Select(static x => x.GetString()).OrderBy(static x => x).ToArray(), schemaShape.GetProperty("properties").EnumerateObject().Select(static x => x.Name).OrderBy(static x => x).ToArray());
    }

    private static void AssertPositiveProperties(System.Text.Json.JsonElement manifestShape, System.Text.Json.JsonElement schemaShape)
    {
        var schemaPositive = schemaShape.GetProperty("properties").EnumerateObject()
            .Where(static property => property.Value.TryGetProperty("exclusiveMinimum", out var minimum) && minimum.GetDouble() == 0)
            .Select(static property => property.Name)
            .OrderBy(static property => property);
        Assert.Equal(manifestShape.GetProperty("positive").EnumerateArray().Select(static property => property.GetString()).OrderBy(static property => property), schemaPositive);
    }

    [Fact]
    public void ServerCanonicalBytesAndHashAreCultureStableAndPreserveNumericMeaning()
    {
        var scene = LoadScene("valid-complex.json");
        var canonicalizer = new SceneCanonicalizer();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var english = canonicalizer.ValidateAndCanonicalize(scene);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = canonicalizer.ValidateAndCanonicalize(scene);

            Assert.True(english.IsValid, english.Error);
            Assert.True(turkish.IsValid, turkish.Error);
            Assert.Equal(english.CanonicalBytes, turkish.CanonicalBytes);
            Assert.Equal(english.Sha256, turkish.Sha256);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(english.CanonicalBytes!)), english.Sha256);
            Assert.Contains("12.5", System.Text.Encoding.UTF8.GetString(english.CanonicalBytes!));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void ServerCanonicalizesEquivalentFiniteNumberLexemesToTheSameBytesAndHash()
    {
        var canonicalizer = new SceneCanonicalizer();
        var one = canonicalizer.ValidateAndCanonicalize(LoadScene("valid-complex.json"));
        var onePointZero = canonicalizer.ValidateAndCanonicalize(LoadScene("valid-complex.json").Replace("12.5", "1.25e1", StringComparison.Ordinal));

        Assert.True(one.IsValid, one.Error);
        Assert.True(onePointZero.IsValid, onePointZero.Error);
        Assert.Equal(one.CanonicalBytes, onePointZero.CanonicalBytes);
        Assert.Equal(one.Sha256, onePointZero.Sha256);
    }

    [Fact]
    public void ServerCanonicalizesEquivalentDecimalLexemesAndRejectsIntegersOutsideTheClientExactRange()
    {
        var canonicalizer = new SceneCanonicalizer();
        var source = LoadScene("valid-complex.json");
        var one = canonicalizer.ValidateAndCanonicalize(source.Replace("12.5", "1", StringComparison.Ordinal));
        var onePointZero = canonicalizer.ValidateAndCanonicalize(source.Replace("12.5", "1.0", StringComparison.Ordinal));
        var oneExponentZero = canonicalizer.ValidateAndCanonicalize(source.Replace("12.5", "1e0", StringComparison.Ordinal));
        var lowerInteger = canonicalizer.ValidateAndCanonicalize(source.Replace("12.5", "9007199254740992", StringComparison.Ordinal));
        var higherInteger = canonicalizer.ValidateAndCanonicalize(source.Replace("12.5", "9007199254740993", StringComparison.Ordinal));

        Assert.True(one.IsValid, one.Error);
        Assert.True(onePointZero.IsValid, onePointZero.Error);
        Assert.True(oneExponentZero.IsValid, oneExponentZero.Error);
        Assert.False(lowerInteger.IsValid);
        Assert.False(higherInteger.IsValid);
        Assert.Equal(one.CanonicalBytes, onePointZero.CanonicalBytes);
        Assert.Equal(one.CanonicalBytes, oneExponentZero.CanonicalBytes);
        Assert.Null(lowerInteger.CanonicalBytes);
        Assert.Null(higherInteger.CanonicalBytes);
    }

    private static string LoadScene(string fixtureName)
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory, fixtureName)));
        return fixture.RootElement.GetProperty("sceneJson").GetString()!;
    }

    private static string LoadContractResource(string name)
    {
        using var stream = typeof(SceneContract).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded contract resource {name} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
