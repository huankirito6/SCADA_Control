using System.Text.Json;

namespace Scada.Contracts.Scenes;

public sealed record SceneManifest(
    int SchemaVersion,
    string[] WidgetTypes,
    string[] BindingTargets,
    string[] ActionKinds,
    SceneLimits Limits);

public sealed record SceneLimits(int Elements, int Nesting, int Vertices, int Segments, int StringLength);

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
