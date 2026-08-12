using System.Text.Json;

namespace Scada.Contracts.Scenes;

public sealed record SceneManifest(
    int SchemaVersion,
    string[] WidgetTypes,
    string[] BindingTargets,
    string[] ActionKinds,
    SceneLimits Limits,
    SceneValuePolicy Values);

public sealed record SceneLimits(int Elements, int Nesting, int Vertices, int Segments, int StringLength);

/// <summary>Manifest policy consumed by every generated validator. Values are JSON scalars only; strings use this allowlist so scene data cannot carry executable syntax, markup, or URLs.</summary>
public sealed record SceneValuePolicy(string SafeStringPattern);

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
