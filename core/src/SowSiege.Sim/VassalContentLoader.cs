using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class VassalContentLoader
{
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
        var rules = document.RootElement.Deserialize<VassalRunRules>(options) ?? throw new InvalidDataException("Missing vassal rules.");
        VassalRun.ValidateRules(rules);
        return rules with { Vassals = Array.AsReadOnly(rules.Vassals.ToArray()), Effects = Array.AsReadOnly(rules.Effects.ToArray()) };
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
