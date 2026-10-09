using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class MetaContentLoader
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
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "meta", "progression.json")));
        Shape(document.RootElement, typeof(MetaCatalog));
        return document.RootElement.Deserialize<MetaCatalog>(Options) ?? throw new InvalidDataException("Missing meta catalog.");
    }

    public static MetaCatalog Load(string root)
    {
        var data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        var catalog = Read(data);
        var project = Directory.GetParent(Path.GetFullPath(data))!.FullName;
        using var art = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "unity", "Assets", "Art", "first-playable-manifest.json")));
        var roles = art.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var vassals = Directory.GetFiles(Path.Combine(data, "vassals"), "*.json").Select(file =>
        {
            using var value = JsonDocument.Parse(File.ReadAllText(file));
            return value.RootElement.GetProperty("id").GetString()!;
        }).ToHashSet(StringComparer.Ordinal);
        Validate(catalog, ContentLoader.Load(data, profileName: "first-playable"), roles, vassals);
        return catalog;
    }

    public static void Validate(MetaCatalog c, ContentCatalog run, IReadOnlySet<string> artRoles, IReadOnlySet<string> vassalIds)
    {
        Require(c.ContractVersion == 1 && c.Materials.Length == 4 && c.Chapters.Length == 10 && c.ManorBuildings.Length == 4 && c.Vassals.Length == 6 && c.Challenges.Length == 20, "Meta contract counts.");
        var allIds = c.Materials.Select(x => x.Id).Concat(c.Chapters.Select(x => x.Id)).Concat(c.ManorBuildings.Select(x => x.Id)).Concat(c.Vassals.Select(x => x.Id)).Concat(c.Challenges.Select(x => x.Id)).ToArray();
        Require(allIds.Distinct(StringComparer.Ordinal).Count() == allIds.Length && allIds.All(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$")), "Meta IDs must be unique namespace IDs.");
        var materials = c.Materials.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        void Money(Dictionary<string, int> values) => Require(values.Count > 0 && values.All(x => materials.Contains(x.Key) && x.Value >= 0 && x.Value <= 1000000), "Invalid material reference or amount.");
        void Art(string role) => Require(artRoles.Contains(role), "Missing art role: " + role);
        foreach (var m in c.Materials) { Require(m.WalletCap is > 0 and <= 1000000, "Wallet cap."); Art(m.ArtRole); }
        Require(c.Chapters.Select(x => x.Index).Order().SequenceEqual(Enumerable.Range(1, 10)), "Chapter indices must cover 1..10.");
        foreach (var ch in c.Chapters)
        {
            Require(new[] { ch.WidthPermille, ch.HeightPermille, ch.ThreatPermille, ch.EnemyHealthPermille, ch.EnemyDamagePermille }.All(x => x is >= 100 and <= 5000), "Chapter multipliers.");
            Require(ch.SiteCount is > 0 and <= 100 && ch.SiteSpacing is > 0 and <= 2000 && ch.FarmCapacity is > 0 and <= 1000, "Chapter world limits.");
            Require(ch.EnemyIds.Length > 1 && ch.EnemyIds.Distinct().Count() == ch.EnemyIds.Length && ch.EnemyIds.All(run.Enemies.ContainsKey) && ch.EnemyIds.Contains(ch.BossId) && run.FirstPlayable!.Enemies[ch.BossId].Rank == "boss", "Chapter enemy/boss references.");
            Require(ch.Terrain.Select(x => x.Kind).ToHashSet().SetEquals(new[] { "river", "forest", "hill" }), "Chapter terrain kinds.");
            foreach (var t in ch.Terrain)
            {
                Require(t.XPermille >= 0 && t.YPermille >= 0 && t.WidthPermille > 0 && t.HeightPermille > 0 && (long)t.XPermille + t.WidthPermille <= 1000 && (long)t.YPermille + t.HeightPermille <= 1000, "Terrain bounds."); Art(t.ArtRole);
            }
            Money(ch.RewardCaps); Require(ch.RewardCaps.Keys.ToHashSet().SetEquals(materials) && ch.RewardCaps.Values.All(x => x > 0), "Chapter caps must cover every material.");
        }
        Require(c.ManorBuildings.Select(x => x.Effect).Distinct().Count() == 4, "Four distinct manor effects required.");
        foreach (var b in c.ManorBuildings) { Require(Enum.IsDefined(b.Effect) && b.MaxLevel is > 0 and <= 20, "Manor limits."); Money(b.BaseCost); Money(b.CostPerLevel); Require(b.BaseCost.Values.Any(x => x > 0), "Free manor growth."); Art(b.ArtRole); }
        Require(c.Vassals.Count(x => x.InitiallyUnlocked) == 1 && c.Vassals.Select(x => x.Ability).Distinct().Count() == 6, "One initial vassal and six abilities required.");
        foreach (var v in c.Vassals)
        {
            Require(vassalIds.Contains(v.Id) && Enum.IsDefined(v.Ability) && v.MaxLevel == 20 && v.MaxRank is > 0 and <= 10 && v.RankCosts.Length == v.MaxRank && v.FragmentCap is > 0 and <= 1000000 && v.RankCosts.All(x => x > 0 && x <= v.FragmentCap), "Vassal reference/levels/fragments.");
            Require(new[] { v.BasePermille, v.PerLevelPermille, v.PerRankPermille }.All(x => x is >= 0 and <= 500) && v.BasePermille + 19L * v.PerLevelPermille + v.MaxRank * v.PerRankPermille <= 2000, "Vassal multiplier bound."); Money(v.LevelCostBase); Money(v.LevelCostStep); Require(v.LevelCostBase.Values.Any(x => x > 0), "Free vassal growth."); Art(v.ArtRole);
        }
        var content = run.Weapons.Keys.Concat(run.Tools.Keys).Concat(run.Runtime!.Items.Keys).ToHashSet(StringComparer.Ordinal);
        Require(c.InitialContentIds.Length > 0 && c.InitialContentIds.Length < content.Count && c.InitialContentIds.Distinct().Count() == c.InitialContentIds.Length && c.InitialContentIds.All(content.Contains), "Initial content pool.");
        Require(c.InitialContentIds.Contains(run.Tuning.World.Progression.StartingWeapon) && run.Heroes.Values.All(x => c.InitialContentIds.Contains(x.StartingTool)), "Starting equipment must be unlocked.");
        var researchMax = c.ManorBuildings.Single(x => x.Effect == ManorEffect.Research).MaxLevel;
        foreach (var ch in c.Challenges)
        {
            Require(Enum.IsDefined(ch.Metric) && ch.Target is > 0 and <= 1000000000 && ch.ResearchLevel >= 0 && ch.ResearchLevel <= researchMax, "Challenge target/research unreachable.");
            Require(ch.UnlockContentIds.Length + ch.UnlockVassalIds.Length > 0 && ch.UnlockContentIds.All(content.Contains) && ch.UnlockVassalIds.All(id => c.Vassals.Any(x => x.Id == id && !x.InitiallyUnlocked)), "Challenge unlock references."); Money(ch.Rewards);
        }
        var unlocks = c.InitialContentIds.Concat(c.Challenges.SelectMany(x => x.UnlockContentIds)).ToArray();
        Require(unlocks.Distinct().Count() == unlocks.Length && unlocks.ToHashSet().SetEquals(content), "Content unlock coverage/duplicates.");
        var people = c.Challenges.SelectMany(x => x.UnlockVassalIds).ToArray();
        Require(people.Length == 5 && people.Distinct().Count() == 5, "Every later vassal must unlock exactly once.");
        var e = c.Economy;
        Require(new[] { e.RepeatPermille, e.DeathPermille, e.AbandonPermille }.All(x => x is >= 0 and <= 1000) && e.MinimumRewardTicks > 0 && e.MinimumRewardTicks <= run.Tuning.DurationTicks, "Settlement fractions/clock.");
        Require(e.IdleStepSeconds > 0 && e.BaseIdleCapSeconds >= 7200 && e.MaximumIdleCapSeconds <= 14400 && e.BaseIdleCapSeconds <= e.MaximumIdleCapSeconds && e.IdleStepSeconds <= e.BaseIdleCapSeconds && e.IdleCapSecondsPerLevel >= 0 && e.IdleCapSecondsPerLevel <= e.MaximumIdleCapSeconds && e.ClockToleranceSeconds is >= 0 and <= 300, "Idle time limits."); Money(e.IdlePerStep);
        Require(e.BaseVassalLevelCap is > 0 and <= 20 && e.LevelCapPerForgeLevel is > 0 and <= 20 && e.BaseVassalSlots == 1 && e.MaximumVassalSlots is >= 1 and <= 6 && e.BarracksLevelsPerSlot > 0 && e.FragmentsPerClear is >= 0 and <= 1000 && e.FragmentsPerDeath is >= 0 and <= 1000, "Economy vassal limits.");
        Require(e.RewardRules.Length == materials.Count && e.RewardRules.Select(x => x.MaterialId).ToHashSet().SetEquals(materials), "Reward rule materials.");
        foreach (var r in e.RewardRules)
        {
            Require(new[] { r.PerFarm, r.PerBuilding, r.PerPerson, r.PerHarvest, r.PerLevel, r.PerBoss }.All(x => x is >= 0 and <= 1000), "Reward coefficients.");
        }
    }

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
        foreach (var p in type.GetProperties())
        {
            Require(value.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(p.Name), out var child), "Missing meta property: " + p.Name);
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
