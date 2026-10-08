using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
namespace SowSiege.Core
{
    internal static class FirstPlayableView
    {
        internal static bool BossDefeated(ContentCatalog catalog, WorldState world) => catalog.FirstPlayable is { } definition && definition.Enemies.Any(p => p.Value.Rank == "boss") && definition.Enemies.Where(p => p.Value.Rank == "boss").All(p => world.FirstPlayable!.DefeatedBosses.Contains(p.Key));
        internal static FirstPlayableFrame? Capture(ContentCatalog catalog, WorldState world, string heroId)
        {
            if (catalog.FirstPlayable is not { } definition || world.FirstPlayable is not { } state) { return null; }
            var attacks = state.Attacks.Select(a => new ActiveAttackView(a.Id, a.Source, a.Form, InteractiveState.Point(a.Position), InteractiveState.Point(a.Previous), definition.Weapons[a.Source].Radius, a.Age, definition.Weapons[a.Source].LifetimeTicks)).ToArray();
            var events = state.MapEvents.Select(e => { var d = definition.MapEvents.Single(x => x.Id == e.Definition); return new MapEventView(e.Id, e.Definition, d.Kind, InteractiveState.Point(e.Position), e.Health, d.Health, d.FoodCost, e.ExpiresTick); }).ToArray();
            var buildings = world.Buildings.Select(b => { var work = state.BuildingWork.GetValueOrDefault(b.Id); var status = work > 0 ? "constructing" : b.Built ? b.Health == 0 ? "ruin" : b.Health < catalog.Tuning.World.Buildings.Health ? "damaged" : "complete" : "site"; return new BuildingProgressView(b.Id, status, work, definition.BuildingWorkRequired); }).ToArray();
            var evolutions = catalog.Runtime!.Evolutions.Values.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => new EvolutionClueView(e.Id, world.Runtime!.Evolutions.Contains(e.Id), RuntimeSystem.EvolutionEligible(catalog, world, e), Array.AsReadOnly(definition.EvolutionRequirements.GetValueOrDefault(e.Id) ?? Array.Empty<EvolutionRequirement>()), definition.EvolutionGrowthRequirements?.GetValueOrDefault(e.Id))).ToArray();
            var cards = world.PendingCards.Select(id => { var rarity = state.OfferedRarities[id]; var level = world.Equipment.FirstOrDefault(e => e.Id == id)?.Level ?? world.Runtime!.Charters.GetValueOrDefault(id); var next = level + rarity.UpgradeAmount; if (catalog.Weapons.ContainsKey(id)) { next = Math.Min(catalog.WeaponCombat!.Weapons[id].Levels.Length, next); } return new OfferedCardDetail(id, rarity.Name, rarity.UpgradeAmount, level, next, Array.AsReadOnly(evolutions.Where(e => e.Requirements.Any(r => r.EquipmentId == id)).Select(e => e.Id).ToArray())); }).ToArray();
            var bosses = definition.Enemies.Where(p => p.Value.Rank != "normal").OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => { var id = state.BossEntities.GetValueOrDefault(p.Key); var entity = world.Enemies.FirstOrDefault(e => e.Id == id); return new BossView(p.Key, id, p.Value.Rank, state.DefeatedBosses.Contains(p.Key) ? "defeated" : entity is not null ? "active" : "pending", entity?.Health ?? 0, catalog.Enemies[p.Key].Health); }).ToArray();
            return new(Array.AsReadOnly(attacks), Array.AsReadOnly(events), Array.AsReadOnly(buildings), Array.AsReadOnly(cards), Array.AsReadOnly(evolutions), Array.AsReadOnly(world.Runtime!.Charters.Keys.ToArray()), new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(world.Runtime.Items, StringComparer.Ordinal)), state.Kills, world.Harvests, heroId,
                Array.AsReadOnly(world.People.Select(p => new PersonActivityView(p.Id, state.PersonActivities.GetValueOrDefault(p.Id, "idle"))).ToArray()), Array.AsReadOnly(bosses), BossDefeated(catalog, world));
        }
    }
}
