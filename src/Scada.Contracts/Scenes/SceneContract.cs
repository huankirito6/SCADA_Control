using System.Text.Json;

namespace Scada.Contracts.Scenes;

public sealed record SceneManifest(
    int SchemaVersion,
    string[] WidgetTypes,
    string[] BindingTargets,
    string[] ActionKinds,
    string[] RoutingKinds,
    SceneLimits Limits,
    SceneValuePolicy Values,
    Dictionary<string, string[]> ElementOwnership,
    Dictionary<string, SceneShape> Bindings,
    Dictionary<string, SceneShape> Actions);

public sealed record SceneLimits(int Elements, int Nesting, int Vertices, int Segments, int StringLength, int NumberLexemeLength, int NumberExponentMagnitude, int CanonicalNumberLength, bool RequiresExactJavaScriptInteger);

/// <summary>ASCII display tokens deliberately exclude markup delimiters, URL separators, quotes, escapes, and executable syntax.</summary>
public sealed record SceneValuePolicy(string SafeStringPattern, string SafeStringRationale);
public sealed record SceneShape(string[] Required, string[] Allowed);

public static class SceneContract
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<SceneManifest> ManifestValue = new(LoadManifest);
    public static SceneManifest Manifest => ManifestValue.Value;

    private static SceneManifest LoadManifest()
    {
        var resource = typeof(SceneContract).Assembly.GetManifestResourceStream("Scada.Contracts.Scenes.widget-manifest.json")
            ?? throw new InvalidOperationException("Embedded widget manifest is missing.");
        return JsonSerializer.Deserialize<SceneManifest>(resource, SerializerOptions)
            ?? throw new InvalidOperationException("Embedded widget manifest is invalid.");
    }
}
