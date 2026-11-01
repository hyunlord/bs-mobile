using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Content;

public static class MetaCatalogReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static MetaCatalog Read(string root)
    {
        var data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        return Parse(File.ReadAllBytes(Path.Combine(data, "meta", "progression.json")));
    }

    public static MetaCatalog Parse(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        Shape(document.RootElement, typeof(MetaCatalog));
        var executable = JsonNode.Parse(document.RootElement.GetRawText())!.AsObject();
        foreach (var group in new[] { "materials", "chapters", "manorBuildings", "vassals", "challenges" })
        {
            foreach (var record in executable[group]!.AsArray().Select(x => x!.AsObject()))
            {
                foreach (var field in DesignFields) { record.Remove(field); }
                if (group == "manorBuildings")
                {
                    record["effect"] = record["manorEffect"]!.DeepClone();
                    record.Remove("manorEffect");
                }
            }
        }

        return executable.Deserialize<MetaCatalog>(Options) ?? throw new InvalidDataException("Missing meta catalog.");
    }

    private static readonly string[] DesignFields = { "kind", "tags", "designStatus", "concept", "effect" };
    private sealed record DesignEffect(string Trigger, string Benefit, string Cost);

    private static string? ContentKind(Type type) => type == typeof(MetaMaterial) ? "material"
        : type == typeof(MetaChapter) ? "chapter"
        : type == typeof(MetaManorBuilding) ? "manor-building"
        : type == typeof(MetaVassal) ? "meta-vassal"
        : type == typeof(MetaChallenge) ? "challenge" : null;

    private static void Shape(JsonElement value, Type type)
    {
        Require(value.ValueKind != JsonValueKind.Null, "Null meta member.");
        if (type == typeof(string)) { Require(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()), "Empty meta string."); return; }
        if (type.IsPrimitive || type.IsEnum)
        {
            return;
        }

        if (type.IsArray) { Require(value.ValueKind == JsonValueKind.Array, "Meta array required."); foreach (var x in value.EnumerateArray()) { Shape(x, type.GetElementType()!); } return; }
        Require(value.ValueKind == JsonValueKind.Object, "Meta object required.");
        var properties = value.EnumerateObject().ToArray();
        Require(properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length, "Duplicate meta property.");
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) { foreach (var x in properties) { Shape(x.Value, type.GenericTypeArguments[1]); } return; }
        var kind = ContentKind(type);
        string FieldName(System.Reflection.PropertyInfo p) => type == typeof(MetaManorBuilding) && p.Name == nameof(MetaManorBuilding.Effect)
            ? "manorEffect" : JsonNamingPolicy.CamelCase.ConvertName(p.Name);
        var allowed = type.GetProperties().Select(FieldName).ToHashSet(StringComparer.Ordinal);
        if (kind is not null)
        {
            allowed.UnionWith(DesignFields);
            foreach (var field in DesignFields) { Require(value.TryGetProperty(field, out _), "Missing meta design property: " + field); }
            Shape(value.GetProperty("kind"), typeof(string));
            Require(value.GetProperty("kind").GetString() == kind, "Unexpected meta kind.");
            Shape(value.GetProperty("designStatus"), typeof(string));
            Require(value.GetProperty("designStatus").GetString() is "current-implementation" or "designed-v1", "Unknown meta design status.");
            Shape(value.GetProperty("concept"), typeof(string));
            Shape(value.GetProperty("effect"), typeof(DesignEffect));
            var tags = value.GetProperty("tags");
            Shape(tags, typeof(string[]));
            Require(tags.GetArrayLength() > 0 && tags.EnumerateArray().Select(x => x.GetString()).Distinct(StringComparer.Ordinal).Count() == tags.GetArrayLength(), "Meta tags must be nonempty and unique.");
        }

        Require(properties.All(p => allowed.Contains(p.Name)), "Unknown meta property.");
        foreach (var p in type.GetProperties())
        {
            Require(value.TryGetProperty(FieldName(p), out var child), "Missing meta property: " + p.Name);
            Shape(child, p.PropertyType);
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
