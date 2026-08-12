using System.Globalization;
using System.Security.Cryptography;
using Scada.Application.Scenes;

namespace Scada.Domain.Tests.Scenes;

public sealed class SceneCorpusTests
{
    private static readonly string CorpusDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../fixtures/scenes/schema-v1"));

    [Fact]
    public void CSharpValidatorAcceptsAndRejectsTheSharedCorpus()
    {
        var canonicalizer = new SceneCanonicalizer();

        foreach (var fixturePath in Directory.GetFiles(CorpusDirectory, "*.json").OrderBy(static path => path, StringComparer.Ordinal))
        {
            using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fixturePath));
            var expected = fixture.RootElement.GetProperty("expectedValid").GetBoolean();
            var result = canonicalizer.ValidateAndCanonicalize(fixture.RootElement.GetProperty("scene").GetRawText());

            Assert.Equal(expected, result.IsValid);
        }
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

    private static string LoadScene(string fixtureName)
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(CorpusDirectory, fixtureName)));
        return fixture.RootElement.GetProperty("scene").GetRawText();
    }
}
