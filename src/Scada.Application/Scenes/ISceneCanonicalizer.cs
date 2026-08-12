using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scada.Contracts.Scenes;

namespace Scada.Application.Scenes;

public interface ISceneCanonicalizer { SceneCanonicalizationResult ValidateAndCanonicalize(string sceneJson); }
public sealed record SceneCanonicalizationResult(bool IsValid, byte[]? CanonicalBytes, string? Sha256, string? Error);

public sealed class SceneCanonicalizer : ISceneCanonicalizer
{
    private const int MaximumNumberLexemeLength = 64;
    private const int MaximumNumberExponentMagnitude = 64;
    private const int MaximumCanonicalNumberLength = 64;
    private static readonly SceneManifest Contract = SceneContract.Manifest;
    private static readonly HashSet<string> WidgetTypes = Contract.WidgetTypes.ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> Targets = Contract.BindingTargets.ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> Actions = Contract.ActionKinds.ToHashSet(StringComparer.Ordinal);
    private static readonly Regex SafeString = new(Contract.Values.SafeStringPattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public SceneCanonicalizationResult ValidateAndCanonicalize(string sceneJson)
    {
        try
        {
            using var document = JsonDocument.Parse(sceneJson, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            ValidateScene(document.RootElement);
            var bytes = Canonicalize(document.RootElement);
            return new(true, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)), null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or OverflowException)
        { return new(false, null, null, exception.Message); }
    }

    private static void ValidateScene(JsonElement scene)
    {
        RequireObject(scene, "scene"); RequireOnly(scene, "schemaVersion", "revision", "screenId", "canvas", "layers", "symbols", "elements");
        if (Require(scene, "schemaVersion").GetInt32() != Contract.SchemaVersion) throw new ArgumentException("Unsupported schema version.");
        if (Require(scene, "revision").GetInt32() < 0) throw new ArgumentException("Revision must be non-negative.");
        ValidateId(Require(scene, "screenId").GetString(), "screen ID");
        var canvas = Require(scene, "canvas"); RequireObject(canvas, "canvas"); RequireOnly(canvas, "width", "height", "viewBox"); PositiveNumber(Require(canvas, "width"), "canvas width"); PositiveNumber(Require(canvas, "height"), "canvas height");
        var viewBox = Require(canvas, "viewBox"); RequireObject(viewBox, "viewBox"); RequireOnly(viewBox, "x", "y", "width", "height"); FiniteNumber(Require(viewBox, "x"), "viewBox x"); FiniteNumber(Require(viewBox, "y"), "viewBox y"); PositiveNumber(Require(viewBox, "width"), "viewBox width"); PositiveNumber(Require(viewBox, "height"), "viewBox height");
        var layers = Require(scene, "layers"); RequireArray(layers, "layers"); var layerIds = layers.EnumerateArray().Select(x => x.GetString()!).ToHashSet(StringComparer.Ordinal); if (layerIds.Count != layers.GetArrayLength() || layerIds.Any(x => !IsId(x))) throw new ArgumentException("Layers must be unique stable IDs.");
        var elements = Require(scene, "elements"); RequireArray(elements, "elements"); if (elements.GetArrayLength() > Contract.Limits.Elements) throw new ArgumentException("Element limit exceeded.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var parents = new Dictionary<string, string>(StringComparer.Ordinal); var links = new List<(string From, string To)>(); var instances = new List<(string Id, string Symbol)>();
        foreach (var element in elements.EnumerateArray()) ValidateElement(element, ids, parents, links, instances, layerIds);
        foreach (var parent in parents) if (!ids.Contains(parent.Value)) throw new ArgumentException("Dangling parent.");
        foreach (var link in links) if (!ids.Contains(link.From) || !ids.Contains(link.To)) throw new ArgumentException("Dangling link.");
        foreach (var id in ids) { var current = id; for (var depth = 0; parents.TryGetValue(current, out current!); depth++) if (depth >= Contract.Limits.Nesting || current == id) throw new ArgumentException("Parent cycle or nesting limit."); }
        var symbols = Require(scene, "symbols"); RequireObject(symbols, "symbols"); var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var symbol in symbols.EnumerateObject()) { ValidateId(symbol.Name, "symbol ID"); RequireObject(symbol.Value, "symbol"); RequireOnly(symbol.Value, "rootElementId"); var root = Require(symbol.Value, "rootElementId").GetString(); ValidateId(root, "symbol root ID"); if (!ids.Contains(root!) || !roots.TryAdd(symbol.Name, root!)) throw new ArgumentException("Dangling or duplicate symbol."); }
        foreach (var instance in instances)
        {
            if (!roots.TryGetValue(instance.Symbol, out var root)) throw new ArgumentException("Dangling symbol instance.");
            for (var current = instance.Id; ;)
            {
                if (current == root) throw new ArgumentException("Recursive symbol instance.");
                if (!parents.TryGetValue(current, out current!)) break;
            }
        }
    }

    private static void ValidateElement(JsonElement e, HashSet<string> ids, Dictionary<string, string> parents, List<(string From, string To)> links, List<(string Id, string Symbol)> instances, HashSet<string> layers)
    {
        RequireObject(e, "element"); var id = Require(e, "id").GetString(); ValidateId(id, "element ID"); if (!ids.Add(id!)) throw new ArgumentException("Duplicate element ID."); var kind = Require(e, "kind").GetString();
        var allowed = kind switch { "group" => new[] { "id", "kind", "parentId", "layer", "geometry" }, "widget" => new[] { "id", "kind", "parentId", "layer", "widgetType", "geometry", "bindings", "actions", "props" }, "instance" => new[] { "id", "kind", "parentId", "layer", "symbolId", "tagScope", "geometry" }, _ => throw new ArgumentException("Unknown element kind.") };
        RequireOnly(e, allowed); if (e.TryGetProperty("parentId", out var parent)) { ValidateId(parent.GetString(), "parent ID"); parents.Add(id!, parent.GetString()!); } if (e.TryGetProperty("layer", out var layer) && !layers.Contains(layer.GetString()!)) throw new ArgumentException("Unknown layer.");
        if (kind == "widget") { var widget = Require(e, "widgetType").GetString(); if (!WidgetTypes.Contains(widget!)) throw new ArgumentException("Unknown widget."); ValidateBindings(e); ValidateActions(e); ValidateSafeMapProperty(e, "props"); }
        if (kind == "instance") { var symbol = Require(e, "symbolId").GetString(); ValidateId(symbol, "symbol ID"); ValidateSafeMapProperty(e, "tagScope", required: true); instances.Add((id!, symbol!)); }
        ValidateGeometry(Require(e, "geometry"), links);
    }

    private static void ValidateGeometry(JsonElement g, List<(string From, string To)> links)
    {
        RequireObject(g, "geometry"); var kind = Require(g, "kind").GetString();
        if (kind == "box") { RequireOnly(g, "kind", "x", "y", "w", "h", "rotation"); foreach (var key in new[] { "x", "y", "w", "h", "rotation" }) FiniteNumber(Require(g, key), key); PositiveNumber(Require(g, "w"), "width"); PositiveNumber(Require(g, "h"), "height"); return; }
        if (kind == "points") { RequireOnly(g, "kind", "vertices"); var points = Require(g, "vertices"); RequireArray(points, "points"); if (points.GetArrayLength() < 2 || points.GetArrayLength() > Contract.Limits.Vertices) throw new ArgumentException("Vertex limit exceeded."); foreach (var p in points.EnumerateArray()) { RequireObject(p, "point"); RequireOnly(p, "x", "y"); FiniteNumber(Require(p, "x"), "x"); FiniteNumber(Require(p, "y"), "y"); } return; }
        if (kind == "path") { RequireOnly(g, "kind", "segments"); var segments = Require(g, "segments"); RequireArray(segments, "segments"); if (segments.GetArrayLength() > Contract.Limits.Segments) throw new ArgumentException("Path limit exceeded."); foreach (var s in segments.EnumerateArray()) { RequireObject(s, "segment"); RequireOnly(s, "kind", "x", "y"); if (Require(s, "kind").GetString() is not ("move" or "line")) throw new ArgumentException("Unknown path segment."); FiniteNumber(Require(s, "x"), "segment x"); FiniteNumber(Require(s, "y"), "segment y"); } return; }
        if (kind == "link") { RequireOnly(g, "kind", "fromRef", "toRef", "routing"); if (Require(g, "routing").GetString() is not ("orthogonal" or "straight")) throw new ArgumentException("Unknown link routing."); var from = Require(g, "fromRef").GetString(); var to = Require(g, "toRef").GetString(); ValidateId(from, "link source"); ValidateId(to, "link target"); links.Add((from!, to!)); return; } throw new ArgumentException("Unknown geometry.");
    }

    private static void ValidateBindings(JsonElement e) { if (!e.TryGetProperty("bindings", out var bindings)) return; RequireArray(bindings, "bindings"); foreach (var b in bindings.EnumerateArray()) { RequireObject(b, "binding"); var tier = Require(b, "tier").GetString(); if (tier == "direct") RequireOnly(b, "tier", "tag", "target"); else if (tier == "map") { RequireOnly(b, "tier", "tag", "target", "map"); ValidateSafeMapValue(Require(b, "map"), "binding map"); } else throw new ArgumentException("Unknown binding tier."); ValidateId(Require(b, "tag").GetString(), "tag"); if (!Targets.Contains(Require(b, "target").GetString()!)) throw new ArgumentException("Binding target is not allowed."); } }
    private static void ValidateActions(JsonElement e) { if (!e.TryGetProperty("actions", out var actions)) return; RequireArray(actions, "actions"); foreach (var a in actions.EnumerateArray()) { RequireObject(a, "action"); RequireOnly(a, "kind", "commandId", "parameters"); if (!Actions.Contains(Require(a, "kind").GetString()!)) throw new ArgumentException("Unknown action."); ValidateId(Require(a, "commandId").GetString(), "command ID"); ValidateSafeMapValue(Require(a, "parameters"), "parameters"); } }
    private static void ValidateSafeMapProperty(JsonElement e, string name, bool required = false) { if (!e.TryGetProperty(name, out var map)) { if (required) throw new ArgumentException($"Missing {name}."); return; } ValidateSafeMapValue(map, name); }
    private static void ValidateSafeMapValue(JsonElement map, string name) { RequireObject(map, name); foreach (var property in map.EnumerateObject()) { ValidateId(property.Name, $"{name} key"); if (property.Value.ValueKind == JsonValueKind.String && IsSafeString(property.Value.GetString())) continue; if (property.Value.ValueKind == JsonValueKind.Number) { FiniteNumber(property.Value, name); continue; } if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) continue; throw new ArgumentException($"Unsafe {name}."); } }
    private static bool IsSafeString(string? value) => !string.IsNullOrEmpty(value) && value.Length <= Contract.Limits.StringLength && SafeString.IsMatch(value);
    private static byte[] Canonicalize(JsonElement root) { using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, root); return stream.ToArray(); }
    private static void WriteCanonical(Utf8JsonWriter w, JsonElement e) { switch (e.ValueKind) { case JsonValueKind.Object: w.WriteStartObject(); foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)) { w.WritePropertyName(p.Name); WriteCanonical(w, p.Value); } w.WriteEndObject(); break; case JsonValueKind.Array: w.WriteStartArray(); foreach (var x in e.EnumerateArray()) WriteCanonical(w, x); w.WriteEndArray(); break; case JsonValueKind.String: w.WriteStringValue(e.GetString()); break; case JsonValueKind.Number: w.WriteRawValue(NormalizeNumber(e.GetRawText()), skipInputValidation: false); break; case JsonValueKind.True: w.WriteBooleanValue(true); break; case JsonValueKind.False: w.WriteBooleanValue(false); break; default: throw new ArgumentException("Null is not allowed."); } }

    internal static string NormalizeNumber(string raw)
    {
        ValidateNumberLexeme(raw);
        var index = 0; var negative = raw[index] == '-'; if (negative) index++;
        var exponentAt = raw.IndexOfAny(['e', 'E'], index); var mantissaEnd = exponentAt < 0 ? raw.Length : exponentAt;
        var dot = raw.IndexOf('.', index, mantissaEnd - index); var integerEnd = dot < 0 ? mantissaEnd : dot;
        var digits = raw[index..integerEnd] + (dot < 0 ? "" : raw[(dot + 1)..mantissaEnd]);
        var scale = dot < 0 ? 0 : -(mantissaEnd - dot - 1);
        if (exponentAt >= 0)
        {
            if (!int.TryParse(raw[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent)) throw new ArgumentException("Number exponent is out of range.");
            scale += exponent;
        }
        digits = digits.TrimStart('0');
        if (digits.Length == 0) return "0";
        digits = BigInteger.Parse(digits, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        while (digits.Length > 1 && digits[^1] == '0') { digits = digits[..^1]; scale++; }
        var outputLength = scale >= 0
            ? checked(digits.Length + scale)
            : Math.Max(checked(digits.Length - scale + 1), checked(-scale + 2));
        if (outputLength + (negative ? 1 : 0) > MaximumCanonicalNumberLength) throw new ArgumentException("Canonical number exceeds the allowed length.");
        var output = scale >= 0 ? digits + new string('0', scale) : WriteDecimal(digits, -scale);
        return negative ? "-" + output : output;
    }

    private static string WriteDecimal(string digits, int fractionLength) => digits.Length > fractionLength ? digits[..(digits.Length - fractionLength)] + "." + digits[(digits.Length - fractionLength)..] : "0." + new string('0', fractionLength - digits.Length) + digits;
    private static JsonElement Require(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value : throw new ArgumentException($"Missing {name}.");
    private static void RequireOnly(JsonElement e, params string[] names) { foreach (var p in e.EnumerateObject()) if (!names.Contains(p.Name, StringComparer.Ordinal)) throw new ArgumentException($"Unknown property {p.Name}."); }
    private static void RequireObject(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Object) throw new ArgumentException($"{name} must be an object."); }
    private static void RequireArray(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Array) throw new ArgumentException($"{name} must be an array."); }
    private static void FiniteNumber(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Number) throw new ArgumentException($"{name} must be finite."); ValidateNumberLexeme(e.GetRawText()); if (!double.IsFinite(e.GetDouble())) throw new ArgumentException($"{name} must be finite."); }
    private static void PositiveNumber(JsonElement e, string name) { FiniteNumber(e, name); if (e.GetDouble() <= 0) throw new ArgumentException($"{name} must be positive."); }
    private static void ValidateNumberLexeme(string raw)
    {
        if (raw.Length > MaximumNumberLexemeLength) throw new ArgumentException("Number lexeme exceeds the allowed length.");
        var exponentAt = raw.IndexOfAny(['e', 'E']);
        if (exponentAt < 0) return;
        if (!int.TryParse(raw[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent) || Math.Abs((long)exponent) > MaximumNumberExponentMagnitude) throw new ArgumentException("Number exponent exceeds the allowed magnitude.");
    }
    private static void ValidateId(string? value, string name) { if (!IsId(value)) throw new ArgumentException($"Invalid {name}."); }
    private static bool IsId(string? value) => !string.IsNullOrEmpty(value) && value.Length <= Contract.Limits.StringLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
