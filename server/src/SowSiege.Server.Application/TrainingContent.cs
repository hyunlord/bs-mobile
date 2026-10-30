using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed record TrainingLevel(int Level, int Capacity, int BatchLimit);
public sealed record TrainingUnit(string UnitId, string Name, int RequiredBarracksLevel, int SecondsPerUnit,
    Dictionary<string, int> CostPerUnit);
public sealed record TrainingRules(string Version, string Status, string BarracksKind, int MaximumBonusPermille,
    TrainingLevel[] Levels, TrainingUnit[] Units);
public sealed record TrainingContent(TrainingRules Rules, string Hash)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static TrainingContent Load(string root, EstateContent estate)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        return Parse(File.ReadAllBytes(Path.Combine(data, "opt-in", "slg-training-v1.json")), estate);
    }

    public static TrainingContent Parse(byte[] bytes, EstateContent estate)
    {
        var rules = JsonSerializer.Deserialize<TrainingRules>(bytes, Json) ?? throw new InvalidDataException("Missing training rules.");
        var building = estate.Config.Buildings.SingleOrDefault(b => b.Kind == rules.BarracksKind);
        bool valid = rules.Version == "slg-training-v1" && rules.Status == "prototype-hypothesis" && building is not null &&
            rules.MaximumBonusPermille >= 0 && rules.MaximumBonusPermille <= PlayerInput.Scale &&
            rules.Levels.Length == building.Levels.Length + 1 && rules.Units.Length > 0 &&
            rules.Levels.Select(x => x.Level).SequenceEqual(Enumerable.Range(0, rules.Levels.Length)) &&
            rules.Levels[0] == new TrainingLevel(0, 0, 0) &&
            rules.Levels.Skip(1).All(x => x.Capacity > 0 && x.BatchLimit > 0 && x.BatchLimit <= x.Capacity) &&
            rules.Units.Select(x => x.UnitId).Distinct(StringComparer.Ordinal).Count() == rules.Units.Length &&
            rules.Units.All(x => !string.IsNullOrWhiteSpace(x.UnitId) && !string.IsNullOrWhiteSpace(x.Name) &&
                x.RequiredBarracksLevel > 0 && x.RequiredBarracksLevel < rules.Levels.Length && x.SecondsPerUnit > 0 &&
                x.CostPerUnit.Count > 0 && x.CostPerUnit.All(c => c.Value > 0 && estate.Meta.Materials.Any(m => m.Id == c.Key)));
        if (!valid)
        {
            throw new InvalidDataException("Training rules violate the pinned estate catalog.");
        }
        foreach (var unit in rules.Units)
        {
            int largestBatch = rules.Levels.Max(x => x.BatchLimit);
            _ = checked((long)unit.SecondsPerUnit * largestBatch * 1000);
            foreach (int cost in unit.CostPerUnit.Values)
            {
                _ = checked(cost * largestBatch);
            }
        }
        return new(rules, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}
