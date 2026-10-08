using SowSiege.Core;

namespace SowSiege.Sim;

public static partial class ContentLoader
{
    private static bool CanonicalToolExists(string directory, string id) => Directory.EnumerateFiles(Path.Combine(directory, "tools"), "*.json", SearchOption.AllDirectories)
        .Select(Read<ToolContent>).Any(tool => tool.Id == id);

    private static void ValidateFirstPlayable(RuntimeProfile profile, Tuning tuning,
        IReadOnlyDictionary<string, WeaponDefinition> weapons, IReadOnlyDictionary<string, ToolDefinition> tools,
        IReadOnlyDictionary<string, EnemyDefinition> enemies, RuntimeCatalog? runtime)
    {
        if (profile.FirstPlayable is not { } definition) { return; }
        Require(definition.ContractVersion == 1 && runtime is not null && profile.WeaponCombat is { ContractVersion: 2 }, "First playable requires runtime and canonical growth contracts.");
        Require(tuning.DurationTicks == 27000 && tuning.TickRate == 30, "First playable must last fifteen minutes at 30Hz.");
        Require(profile.Selection.Weapons.Length == 10 && profile.Selection.Tools.Length == 8 && profile.Selection.Enemies.Length == 13 && runtime!.Charters.Count == 8 && runtime.Items.Count == 30 && runtime.Evolutions.Count == 8, "First playable content count mismatch.");
        Require(profile.Selection.Heroes.Length == 1 && profile.Selection.Estates.Length == 1, "First playable has one hero and estate.");
        Require(profile.Selection.Tools.Select(id => tools[id]).Count(tool => tool.Growth.Target == "land") == 3 && profile.Selection.Tools.Select(id => tools[id]).Count(tool => tool.Growth.Target == "building") == 3 && profile.Selection.Tools.Select(id => tools[id]).Count(tool => tool.Growth.Target == "people") == 2, "First playable tools require three land, three building and two people.");
        Require(definition.Weapons.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(profile.Selection.Weapons) && definition.Enemies.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(profile.Selection.Enemies), "First playable definitions must exactly cover selected content.");
        var forms = new[] { "sector90", "sector180", "projectile", "volley", "orbit", "field", "piercing", "chain", "boomerang", "nova" };
        Require(definition.Weapons.Values.Select(weapon => weapon.Form).ToHashSet(StringComparer.Ordinal).SetEquals(forms), "Ten distinct first playable weapon forms required.");
        foreach (var weapon in definition.Weapons.Values)
        {
            Require(new[] { weapon.Radius, weapon.LifetimeTicks, weapon.HitIntervalTicks }.All(value => value is > 0 and <= 1000000) && new[] { weapon.Speed, weapon.ChainRange }.All(value => value is >= 0 and <= 1000000), "Weapon geometry, lifetime and speed exceed content bounds.");
            Require(weapon.SpreadPermille is >= 0 and <= 1000 && weapon.BurstIntervalTicks >= 0 && weapon.BurstIntervalTicks <= weapon.LifetimeTicks, "Weapon spread or burst interval outside bounds.");
            Require(weapon.Form is "sector90" or "sector180" or "field" or "chain" || weapon.Speed > 0, "Moving weapon requires speed.");
            Require(weapon.Form == "chain" ? weapon.ChainRange > 0 : weapon.ChainRange == 0, "Chain range must match form.");
        }
        Require(definition.Enemies.Values.Count(enemy => enemy.Rank == "normal") == 10 && definition.Enemies.Values.Count(enemy => enemy.Rank == "elite") == 2 && definition.Enemies.Values.Count(enemy => enemy.Rank == "boss") == 1, "Enemy rank count mismatch.");
        foreach (var enemy in definition.Enemies.Values)
        {
            Require(enemy.FirstSpawnTick >= 0 && enemy.FirstSpawnTick < tuning.DurationTicks && enemy.RepeatTicks is >= 0 and <= 1000000 && enemy.Weight is >= 0 and <= 1000000, "Enemy schedule outside run.");
            Require(enemy.Rank == "normal" ? enemy.Weight > 0 && enemy.RepeatTicks == 0 : enemy.Weight == 0, "Enemy schedule weight mismatch.");
            Require(enemy.Rank != "boss" || enemy.RepeatTicks == 0 && enemy.FirstSpawnTick >= tuning.DurationTicks - tuning.World.Seasons[^1].DurationTicks, "Boss must appear once in winter.");
        }
        Require(definition.MapEvents.Length == 3 && definition.MapEvents.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count() == 3 && definition.MapEvents.Select(entry => entry.Kind).ToHashSet(StringComparer.Ordinal).SetEquals(["merchant", "shrine", "cart"]), "Three distinct map events required.");
        foreach (var entry in definition.MapEvents)
        {
            Require(NamespaceId().IsMatch(entry.Id) && entry.FirstSpawnTick >= 0 && entry.FirstSpawnTick < tuning.DurationTicks && new[] { entry.LifetimeTicks, entry.SpawnRadius, entry.InteractRadius }.All(value => value is > 0 and <= 1000000) && new[] { entry.RepeatTicks, entry.RewardCount, entry.HealAmount, entry.FoodCost, entry.Experience, entry.Health }.All(value => value is >= 0 and <= 1000000) && entry.ItemIds.All(runtime!.Items.ContainsKey), "Invalid map event schedule or reward.");
            Require(entry.Kind == "cart" ? entry.Health > 0 : entry.Health == 0, "Only cart has attackable health.");
            Require(entry.Kind != "merchant" || entry.FoodCost > 0, "Merchant requires food payment.");
            Require(entry.Kind != "shrine" || entry.HealAmount > 0 || entry.Experience > 0, "Shrine requires recovery or experience.");
        }
        Require(definition.EvolutionRequirements.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(runtime!.Evolutions.Keys), "Evolution requirements must cover selected recipes.");
        Require(runtime.Evolutions.Values.Count(evolution => evolution.Kind == "weapon-tool") >= 4, "Four weapon and tool evolutions required.");
        foreach (var evolution in runtime.Evolutions.Values)
        {
            var requirements = definition.EvolutionRequirements[evolution.Id];
            Require(requirements.Length == evolution.InputIds.Length && requirements.Select(requirement => requirement.EquipmentId).ToHashSet(StringComparer.Ordinal).SetEquals(evolution.InputIds) && requirements.All(requirement => requirement.MinimumLevel is > 0 and <= 12), "Evolution level requirements must match inputs.");
        }
        if (definition.EvolutionGrowthRequirements is { } growth)
        {
            Require(growth.All(pair => runtime.Evolutions.TryGetValue(pair.Key, out var recipe) && recipe.Kind == "tool-growth" && pair.Value.Target == "ripe" && pair.Value.Minimum is > 0 and <= 1000000), "Invalid evolution growth requirement.");
        }
        Require(definition.BuildingWorkRequired is > 0 and <= 1000000 && definition.BuildingWorkPerActivation is > 0 and <= 1000000 && definition.BuildingWorkPerActivation < definition.BuildingWorkRequired && definition.MaxActiveAttacks is > 0 and <= 1024, "Invalid first playable work budgets.");
        foreach (var charter in runtime.Charters.Values)
        {
            Require(charter.Effects.Any(effect => effect.Operation == "stat-add" && effect.Subject.StartsWith("attack-", StringComparison.Ordinal)) && charter.Effects.Any(effect => effect.Operation == "stat-add" && !effect.Subject.StartsWith("attack-", StringComparison.Ordinal)), "Each first playable charter must affect weapon and estate stats.");
        }
    }
}
