using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scada.Application.Scenes;

public interface ISceneCanonicalizer { SceneCanonicalizationResult ValidateAndCanonicalize(string sceneJson); }
public sealed record SceneCanonicalizationResult(bool IsValid, byte[]? CanonicalBytes, string? Sha256, string? Error);

public sealed class SceneCanonicalizer : ISceneCanonicalizer
{
    private static readonly HashSet<string> WidgetTypes = ["connector", "image", "motor", "pipe", "text"];
    private static readonly HashSet<string> Targets = ["attr:fill", "attr:opacity", "attr:stroke", "text:value", "visibility"];
    private const int MaxElements = 300, MaxDepth = 8, MaxVertices = 4, MaxSegments = 4, MaxString = 48;

    public SceneCanonicalizationResult ValidateAndCanonicalize(string sceneJson)
    {
        try
        {
            using var document = JsonDocument.Parse(sceneJson, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            ValidateScene(document.RootElement);
            var bytes = Canonicalize(document.RootElement);
            return new(true, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            return new(false, null, null, exception.Message);
        }
    }

    private static void ValidateScene(JsonElement scene)
    {
        RequireObject(scene, "scene");
        RequireOnly(scene, "schemaVersion", "revision", "screenId", "canvas", "layers", "symbols", "elements");
        Require(scene, "schemaVersion").GetInt32().Equals(1).OrThrow("Unsupported schema version.");
        Require(scene, "revision").GetInt32().AtLeast(0, "Revision must be non-negative.");
        ValidateId(Require(scene, "screenId").GetString(), "screen ID");
        var canvas = Require(scene, "canvas"); RequireObject(canvas, "canvas"); RequireOnly(canvas, "width", "height", "viewBox");
        PositiveNumber(Require(canvas, "width"), "canvas width"); PositiveNumber(Require(canvas, "height"), "canvas height");
        var viewBox = Require(canvas, "viewBox"); RequireObject(viewBox, "viewBox"); RequireOnly(viewBox, "x", "y", "width", "height");
        foreach (var key in new[] { "x", "y", "width", "height" }) FiniteNumber(Require(viewBox, key), $"viewBox {key}");
        var layers = Require(scene, "layers"); RequireArray(layers, "layers"); var layerIds = layers.EnumerateArray().Select(static x => x.GetString()!).ToHashSet(StringComparer.Ordinal);
        if (layerIds.Count != layers.GetArrayLength() || layerIds.Any(x => !IsId(x))) throw new ArgumentException("Layers must be unique stable IDs.");
        var elements = Require(scene, "elements"); RequireArray(elements, "elements"); if (elements.GetArrayLength() > MaxElements) throw new ArgumentException("Element limit exceeded.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var parents = new Dictionary<string, string>(StringComparer.Ordinal); var links = new List<(string, string)>(); var instances = new List<(string, string)>();
        foreach (var element in elements.EnumerateArray()) ValidateElement(element, ids, parents, links, instances, layerIds);
        foreach (var parent in parents) if (!ids.Contains(parent.Value)) throw new ArgumentException("Dangling parent.");
        foreach (var link in links) if (!ids.Contains(link.Item1) || !ids.Contains(link.Item2)) throw new ArgumentException("Dangling link.");
        foreach (var id in ids) { var current = id; for (var depth = 0; parents.TryGetValue(current, out current!); depth++) { if (depth >= MaxDepth || current == id) throw new ArgumentException("Parent cycle or nesting limit."); } }
        var symbols = Require(scene, "symbols"); RequireArray(symbols, "symbols"); var symbolRoots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols.EnumerateArray()) { RequireObject(symbol, "symbol"); RequireOnly(symbol, "id", "rootElementId"); var id = Require(symbol, "id").GetString()!; ValidateId(id, "symbol ID"); if (!ids.Contains(Require(symbol, "rootElementId").GetString()!)) throw new ArgumentException("Dangling symbol root."); symbolRoots.Add(id, Require(symbol, "rootElementId").GetString()!); }
        foreach (var instance in instances) { if (!symbolRoots.TryGetValue(instance.Item2, out var root) || root == instance.Item1) throw new ArgumentException("Dangling or recursive symbol instance."); }
    }

    private static void ValidateElement(JsonElement e, HashSet<string> ids, Dictionary<string, string> parents, List<(string, string)> links, List<(string, string)> instances, HashSet<string> layers)
    {
        RequireObject(e, "element"); RequireOnly(e, "id", "kind", "parentId", "layer", "widgetType", "symbolId", "tagScope", "geometry", "bindings", "actions", "props");
        var id = Require(e, "id").GetString()!; ValidateId(id, "element ID"); if (!ids.Add(id)) throw new ArgumentException("Duplicate element ID.");
        var kind = Require(e, "kind").GetString(); if (kind is not ("group" or "widget" or "instance")) throw new ArgumentException("Unknown element kind.");
        if (e.TryGetProperty("parentId", out var parent)) { ValidateId(parent.GetString(), "parent ID"); parents.Add(id, parent.GetString()!); }
        if (e.TryGetProperty("layer", out var layer) && !layers.Contains(layer.GetString()!)) throw new ArgumentException("Unknown layer.");
        if (kind == "widget" && (!e.TryGetProperty("widgetType", out var widget) || !WidgetTypes.Contains(widget.GetString()!))) throw new ArgumentException("Unknown widget.");
        if (kind == "instance") { ValidateId(Require(e, "symbolId").GetString(), "symbol ID"); RequireObject(Require(e, "tagScope"), "tag scope"); instances.Add((id, Require(e, "symbolId").GetString()!)); }
        ValidateGeometry(Require(e, "geometry"), links); ValidateBindings(e); ValidateActions(e); ValidateProps(e);
    }

    private static void ValidateGeometry(JsonElement geometry, List<(string, string)> links)
    {
        RequireObject(geometry, "geometry"); var kind = Require(geometry, "kind").GetString();
        if (kind == "box") { RequireOnly(geometry, "kind", "x", "y", "w", "h", "rotation"); foreach (var key in new[] { "x", "y", "w", "h", "rotation" }) FiniteNumber(Require(geometry, key), key); PositiveNumber(Require(geometry, "w"), "width"); PositiveNumber(Require(geometry, "h"), "height"); return; }
        if (kind == "points") { RequireOnly(geometry, "kind", "vertices"); BoundedPoints(Require(geometry, "vertices"), MaxVertices); return; }
        if (kind == "path") { RequireOnly(geometry, "kind", "segments"); var segments = Require(geometry, "segments"); RequireArray(segments, "segments"); if (segments.GetArrayLength() > MaxSegments) throw new ArgumentException("Path limit exceeded."); foreach (var s in segments.EnumerateArray()) { RequireObject(s, "segment"); RequireOnly(s, "kind", "x", "y"); if (Require(s, "kind").GetString() is not ("move" or "line")) throw new ArgumentException("Unknown path segment."); FiniteNumber(Require(s, "x"), "segment x"); FiniteNumber(Require(s, "y"), "segment y"); } return; }
        if (kind == "link") { RequireOnly(geometry, "kind", "fromRef", "toRef", "routing"); var routing = Require(geometry, "routing").GetString(); if (routing is not ("orthogonal" or "straight")) throw new ArgumentException("Unknown link routing."); links.Add((Require(geometry, "fromRef").GetString()!, Require(geometry, "toRef").GetString()!)); return; }
        throw new ArgumentException("Unknown geometry.");
    }

    private static void ValidateBindings(JsonElement e) { if (!e.TryGetProperty("bindings", out var bindings)) return; RequireArray(bindings, "bindings"); foreach (var b in bindings.EnumerateArray()) { RequireObject(b, "binding"); RequireOnly(b, "tier", "tag", "target", "map"); if (Require(b, "tier").GetString() is not ("direct" or "map")) throw new ArgumentException("Unknown binding tier."); ValidateId(Require(b, "tag").GetString(), "tag"); if (!Targets.Contains(Require(b, "target").GetString()!)) throw new ArgumentException("Binding target is not allowed."); } }
    private static void ValidateActions(JsonElement e) { if (!e.TryGetProperty("actions", out var actions)) return; RequireArray(actions, "actions"); foreach (var a in actions.EnumerateArray()) { RequireObject(a, "action"); RequireOnly(a, "kind", "commandId", "parameters"); if (Require(a, "kind").GetString() != "command") throw new ArgumentException("Unknown action."); ValidateId(Require(a, "commandId").GetString(), "command ID"); if (a.TryGetProperty("parameters", out var p)) RequireObject(p, "parameters"); } }
    private static void ValidateProps(JsonElement e) { if (!e.TryGetProperty("props", out var props)) return; RequireObject(props, "props"); foreach (var p in props.EnumerateObject()) { if (p.Name.Length > MaxString || p.Value.ValueKind != JsonValueKind.String || p.Value.GetString()!.Length > MaxString || p.Value.GetString()!.Contains(':')) throw new ArgumentException("Unsafe or oversized property."); } }
    private static void BoundedPoints(JsonElement points, int max) { RequireArray(points, "points"); if (points.GetArrayLength() < 2 || points.GetArrayLength() > max) throw new ArgumentException("Vertex limit exceeded."); foreach (var p in points.EnumerateArray()) { RequireObject(p, "point"); RequireOnly(p, "x", "y"); FiniteNumber(Require(p, "x"), "x"); FiniteNumber(Require(p, "y"), "y"); } }
    private static byte[] Canonicalize(JsonElement root) { using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, root); return stream.ToArray(); }
    private static void WriteCanonical(Utf8JsonWriter w, JsonElement e) { switch (e.ValueKind) { case JsonValueKind.Object: w.WriteStartObject(); foreach (var p in e.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal)) { w.WritePropertyName(p.Name); WriteCanonical(w, p.Value); } w.WriteEndObject(); break; case JsonValueKind.Array: w.WriteStartArray(); foreach (var x in e.EnumerateArray()) WriteCanonical(w, x); w.WriteEndArray(); break; case JsonValueKind.String: w.WriteStringValue(e.GetString()); break; case JsonValueKind.Number: w.WriteRawValue(e.GetRawText(), true); break; case JsonValueKind.True: w.WriteBooleanValue(true); break; case JsonValueKind.False: w.WriteBooleanValue(false); break; default: throw new ArgumentException("Null is not allowed."); } }
    private static JsonElement Require(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value : throw new ArgumentException($"Missing {name}.");
    private static void RequireOnly(JsonElement e, params string[] names) { foreach (var p in e.EnumerateObject()) if (!names.Contains(p.Name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown property {p.Name}."); }
    private static void RequireObject(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{name} must be an object."); }
    private static void RequireArray(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Array) throw new ArgumentException($"{name} must be an array."); }
    private static void FiniteNumber(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Number || !double.IsFinite(e.GetDouble())) throw new ArgumentException($"{name} must be finite."); }
    private static void PositiveNumber(JsonElement e, string name) { FiniteNumber(e, name); if (e.GetDouble() <= 0) throw new ArgumentException($"{name} must be positive."); }
    private static void ValidateId(string? value, string name) { if (!IsId(value)) throw new ArgumentException($"Invalid {name}."); }
    private static bool IsId(string? value) => !string.IsNullOrEmpty(value) && value.Length <= MaxString && value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

file static class SceneContractExtensions { public static void OrThrow(this bool value, string message) { if (!value) throw new ArgumentException(message); } public static void AtLeast(this int value, int minimum, string message) { if (value < minimum) throw new ArgumentException(message); } }