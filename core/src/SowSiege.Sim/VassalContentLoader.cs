using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class VassalContentLoader
{
    private sealed record SourceRules(string Id, int Version, IReadOnlyList<VassalDefinition> Vassals,
        IReadOnlyList<VassalEffectRule> Effects, VassalCapRules Caps);
    public static VassalRunRules Load(string root)
    {
        var data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        var bytes = File.ReadAllBytes(Path.Combine(data, "opt-in", "vassals", "vassal-run-v1.json"));
        if (bytes.Length > VassalRunCodec.MaximumBytes)
        {
            throw new InvalidDataException("Vassal definition exceeds limit.");
        }
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
        };
        using var document = JsonDocument.Parse(bytes);
        RejectDuplicateProperties(document.RootElement);
        RequireFields(document.RootElement, "id", "version", "vassals", "effects", "caps");
        RequireFields(document.RootElement.GetProperty("caps"), "baseSlots", "maximumSlots", "barracksLevelsPerSlot",
            "baseLevelCap", "levelCapPerForgeLevel", "maximumLevel", "maximumRank");
        foreach (var definition in document.RootElement.GetProperty("vassals").EnumerateArray())
        {
            RequireFields(definition, "id", "ability");
        }
        foreach (var effect in document.RootElement.GetProperty("effects").EnumerateArray())
        {
            RequireFields(effect, "ability", "base", "perLevel", "perRank", "cap");
        }
        var source = document.RootElement.Deserialize<SourceRules>(options) ?? throw new InvalidDataException("Missing vassal rules.");
        var rules = new VassalRunRules(source.Id, source.Version, source.Vassals, source.Effects, source.Caps, Convert.ToHexString(SHA256.HashData(bytes)));
        VassalRun.ValidateRules(rules);
        return rules with { Vassals = Array.AsReadOnly(rules.Vassals.ToArray()), Effects = Array.AsReadOnly(rules.Effects.ToArray()) };
    }

    private static void RequireFields(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object || names.Any(name => !element.TryGetProperty(name, out _)))
        {
            throw new InvalidDataException("Missing vassal JSON field.");
        }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Duplicate vassal JSON property.");
                }
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                RejectDuplicateProperties(child);
            }
        }
    }
}
