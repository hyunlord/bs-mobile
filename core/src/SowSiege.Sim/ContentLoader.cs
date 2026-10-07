using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class ContentLoader

{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ContentCatalog Load(string directory, bool includeTest = false)
    {
        var tuning = Read<Tuning>(Path.Combine(directory, "tuning.json"));
        var roots = includeTest ? new[] { directory, Path.Combine(directory, "test") } : new[] { directory };
        var tools = LoadRecords<ToolDefinition>(roots, "tools", tool => tool.Id);
        var heroes = LoadRecords<HeroDefinition>(roots, "heroes", hero => hero.Id);
        var estates = LoadRecords<EstateDefinition>(roots, "estates", estate => estate.Id);
        foreach (var hero in heroes.Values)
        {
            if (!tools.ContainsKey(hero.StartingTool)) { throw new InvalidDataException($"Unknown starting tool: {hero.StartingTool}"); }
        }
        foreach (var tool in tools.Values)
        {
            if (tool.Activation.Damage <= 0 || tool.Growth.Yield <= 0) { throw new InvalidDataException($"Tool requires activation and growth: {tool.Id}"); }
            foreach (var reference in tool.AntiSynergy)
            {
                if (!tools.ContainsKey(reference)) { throw new InvalidDataException($"Unknown anti-synergy: {reference}"); }
            }
        }
        if (tuning.TickRate <= 0 || tuning.DurationTicks <= 0 || tuning.DamageRollMax <= 0 ||
            !heroes.ContainsKey(tuning.DefaultHero) || !estates.ContainsKey(tuning.DefaultEstate))
        {
            throw new InvalidDataException("Invalid simulation tuning or default references.");
        }
        return new(tuning, tools, heroes, estates);
    }

    private static Dictionary<string, T> LoadRecords<T>(IEnumerable<string> roots, string kind, Func<T, string> id)
    {
        return roots.Select(root => Path.Combine(root, kind)).Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.json")).Order(StringComparer.Ordinal)
            .Select(Read<T>).ToDictionary(id, StringComparer.Ordinal);
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Null content: {path}");
}
