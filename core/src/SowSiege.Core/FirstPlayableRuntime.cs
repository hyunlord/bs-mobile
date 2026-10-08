using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    internal sealed partial class RuntimeSystem
    {
        internal static bool EvolutionEligible(ContentCatalog catalog, WorldState world, EvolutionDefinition evolution)
        {
            if (!evolution.InputIds.All(id => world.Equipment.Any(e => e.Id == id))) { return false; }
            if (catalog.FirstPlayable is not { } definition) { return true; }
            if (definition.EvolutionRequirements.TryGetValue(evolution.Id, out var requirements) && requirements.Any(r => !world.Equipment.Any(e => e.Id == r.EquipmentId && e.Level >= r.MinimumLevel))) { return false; }
            if (definition.EvolutionGrowthRequirements?.TryGetValue(evolution.Id, out var growth) == true)
            {
                var count = growth.Target switch { "ripe" => world.Farms.Count(f => f.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1), "land" => world.Farms.Count, "building" => world.Buildings.Count(b => b.Built && b.Health > 0), "people" => world.People.Sum(p => p.Members), _ => 0 };
                if (count < growth.Minimum) { return false; }
            }
            return true;
        }
        private void TickMapEvents()
        {
            if (world.FirstPlayable is not { } fp || catalog.FirstPlayable is not { } definition) { return; }
            foreach (var entry in definition.MapEvents.OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                var next = fp.NextEventSpawn.GetValueOrDefault(entry.Id, entry.FirstSpawnTick);
                if (world.Tick < next) { continue; }
                if (!fp.MapEvents.Any(e => e.Definition == entry.Id))
                {
                    var position = ClampPosition(new(world.Lord.X + SignedOffset(entry.SpawnRadius), world.Lord.Y + SignedOffset(entry.SpawnRadius)));
                    fp.MapEvents.Add(new() { Id = world.AllocateId(), Definition = entry.Id, Position = position, Health = entry.Health, ExpiresTick = checked(world.Tick + entry.LifetimeTicks) });
                    fp.Count("event:" + entry.Kind + ":spawn"); interactive?.Experience(world.Tick, PresentationKind.EventSpawned, entry.Id, position, 0);
                }
                fp.NextEventSpawn[entry.Id] = entry.RepeatTicks > 0 ? checked(world.Tick + entry.RepeatTicks) : int.MaxValue;
            }
            foreach (var entity in fp.MapEvents.ToArray())
            {
                var entry = definition.MapEvents.Single(e => e.Id == entity.Definition);
                if (world.Tick >= entity.ExpiresTick) { fp.MapEvents.Remove(entity); continue; }
                var claimed = entry.Kind == "cart" ? entity.Health <= 0 : Within(entity.Position, world.Lord, entry.InteractRadius) && world.Food >= entry.FoodCost;
                if (!claimed) { continue; }
                if (entry.Kind != "cart") { world.Food -= entry.FoodCost; }
                var healed = Math.Min(entry.HealAmount, catalog.Tuning.World.Map.LordHealth - world.LordHealth);
                world.LordHealth += healed; world.Experience += entry.Experience; world.TaxExperience += entry.Experience;
                if (entry.Experience > 0) { Experience("building", entry.Experience); interactive?.Experience(world.Tick, PresentationKind.TaxExperience, entry.Id, entity.Position, entry.Experience); }
                var pool = this.definition.Items.Values.Where(item => ItemEligible(item) && (entry.ItemIds.Length == 0 || entry.ItemIds.Contains(item.Id, StringComparer.Ordinal))).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
                for (var reward = 0; reward < entry.RewardCount && pool.Length > 0; reward++)
                {
                    var item = pool[random.Next(pool.Length)]; state.Items[item.Id] = checked(state.Items.GetValueOrDefault(item.Id) + 1);
                    state.Loot.Add(new(world.Tick, entry.Kind == "merchant" ? "market" : entry.Kind == "cart" ? "cart" : "chest", entry.Id, entity.Id, item.Id, 1, state.Items[item.Id], reward == 0 ? entry.FoodCost : 0));
                }
                fp.Count("event:" + entry.Kind + ":claim"); interactive?.Experience(world.Tick, entry.Kind == "cart" ? PresentationKind.CartBroken : PresentationKind.EventClaimed, entry.Id, entity.Position, healed);
                fp.MapEvents.Remove(entity);
            }
        }
    }
}
