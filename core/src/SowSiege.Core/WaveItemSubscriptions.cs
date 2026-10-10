using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    // Subscriptions are synchronous and never use the presentation event stream as a work queue.
    internal sealed class WaveItemSubscriptions
    {
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly WavePrimitiveModules modules;
        private readonly Dictionary<string, Dictionary<string, (WaveItemDefinition Item, WavePrimitiveProgram Program)>> registry = new(StringComparer.Ordinal);
        private int registeredItems;
        private WaveRuntimeDefinition Definition => catalog.WaveRuntime!;
        private WaveRuntimeState State => world.WaveRuntime!;
        internal WaveItemSubscriptions(ContentCatalog catalog, WorldState world, WavePrimitiveModules modules)
        { this.catalog = catalog; this.world = world; this.modules = modules; Register(); }

        private void Register()
        {
            registry.Clear();
            foreach (var item in Definition.Items.Values)
            {
                var program = modules[item.Id];
                foreach (var unit in program.Params)
                {
                    var on = program.Value(unit.Key, "on");
                    var key = unit.Key + ":" + on;
                    if (!registry.TryGetValue(key, out var subscribers)) { subscribers = new(StringComparer.Ordinal); registry.Add(key, subscribers); }
                    subscribers.Add(item.Id, (item, program));
                }
            }
            registeredItems = Definition.Items.Count;
        }
        private IEnumerable<(WaveItemDefinition Item, WavePrimitiveProgram Program)> Owned(string unit, string on)
        {
            if (registeredItems != Definition.Items.Count && Definition.Programs is null) { Register(); }
            if (!registry.TryGetValue(unit + ":" + on, out var subscribers)) { yield break; }
            foreach (var id in State.Items)
            {
                if (subscribers.TryGetValue(id, out var subscription)) { yield return subscription; }
            }
        }
        internal int Modifier(string stat) => Owned("unit:stat-modifier", "").Where(x => x.Program.Is("unit:stat-modifier", "stat", stat)).Sum(x => x.Item.Amount);
        internal bool FrontOrbit(string source) => Owned("unit:geometry-modifier", "enemy-front").Any(x => x.Program.Is("unit:geometry-modifier", "change", "front-corner-orbit") && Linked(x.Item, source));
        internal bool WaitForReturnedGroup(string source) => Owned("unit:target-routing", "recruit").Any(x => Linked(x.Item, source) && x.Program.Is("unit:target-routing", "selection", "available-returned-group") && x.Program.Is("unit:target-routing", "fallback", "wait"));
        private static bool Linked(WaveItemDefinition item, string source) => item.EquipmentIds.Length == 0 || item.EquipmentIds.Contains(source);
        internal Position Aim(WaveGearDefinition gear, Position origin, Position facing)
        {
            foreach (var binding in Owned("unit:target-routing", "attack-ready"))
            {
                if (!Linked(binding.Item, gear.Id) || !binding.Program.Is("unit:target-routing", "selection", "raider-in-range")) { continue; }
                var raider = world.Enemies.Where(e => e.Health > 0 && (modules.Is(e.Definition, "unit:enemy-pressure", "target", "seed") || modules.Is(e.Definition, "unit:enemy-pressure", "target", "ripe")) && WaveRuntimeSystem.Within(e.Position, origin, gear.Range)).OrderBy(e => e.Position.DistanceSquared(origin)).ThenBy(e => e.Id).FirstOrDefault();
                if (raider is not null) { facing = new(raider.Position.X - origin.X, raider.Position.Y - origin.Y); }
            }
            return facing;
        }
        internal void SeedEaten(WaveWork crop) => Route("seed-eaten", crop.Source, crop.Id, crop.Position);
        internal void Hit(string source, EnemyState enemy) => Route("hit", source, enemy.Id, enemy.Position);
        private void Route(string on, string source, int subject, Position position)
        {
            foreach (var binding in Owned("unit:target-routing", on))
            {
                if (!Linked(binding.Item, source) || !binding.Program.Is("unit:target-routing", "selection", "side-route")) { continue; }
                var gear = Definition.Gear[source];
                State.Detours.Add(new() { Source = binding.Item.Id, Position = position, Radius = gear.WorkRadius, UntilTick = world.Tick + gear.WorkTicks });
                State.Emit(world.Tick, on == "seed-eaten" ? "seed-detour" : "hit-detour", binding.Item.Id, subject, position, position, gear.WorkRadius);
                break;
            }
        }
        internal void Harvest(WaveWork work)
        {
            foreach (var binding in Owned("unit:group-formation", "harvest-complete"))
            {
                if (!binding.Program.Is("unit:group-formation", "action", "reposition-existing-guard")) { continue; }
                var group = State.Groups.Where(g => g.Health > 0 && (g.Phase == "engaging" || g.Phase == "guarding")).OrderBy(g => g.Id).FirstOrDefault();
                if (group is not null) { group.Destination = work.Position; group.Phase = "guarding"; State.Emit(world.Tick, "harvest-guard", group.Source, group.Id, work.Position, work.Position); }
                break;
            }
        }
        internal void ReserveMissionFood(WaveGearDefinition gear, WaveGroup group)
        {
            if (Owned("unit:resource-routing", "mission-start").Any(x => Linked(x.Item, gear.Id) && x.Program.Is("unit:resource-routing", "resource", "food") && x.Program.Is("unit:resource-routing", "destination", "field-meal")) && world.Food > 0)
            { world.Food--; group.ReservedFood = 1; State.Emit(world.Tick, "food-reserved", gear.Id, group.Id, group.Position, group.Position, 1); }
        }
        internal void ReturnMissionFood(WaveGroup group) { world.Food += group.ReservedFood; group.ReservedFood = 0; }
        internal void ConsumeMissionFood(WaveGearDefinition gear, WaveGroup group)
        {
            if (group.ReservedFood <= 0) { return; }
            group.Health = Math.Min(gear.Capacity, group.Health + group.ReservedFood); group.ReservedFood = 0;
            State.Emit(world.Tick, "field-meal", group.Source, group.Id, group.Position, group.Position, 1);
        }
        internal void CarryWater(Action<WaveWork, string, int, int> irrigate)
        {
            if (!Owned("unit:resource-routing", "enter").Any(x => x.Program.Is("unit:resource-routing", "resource", "water") && x.Program.Is("unit:resource-routing", "source", "carried-stock"))) { return; }
            var gear = Definition.Gear.Values.FirstOrDefault(g => modules.Is(g.Id, "unit:stock-cycle", "resource", "water"));
            if (gear is null) { return; }
            if (State.CarriedWater == 0 && State.Water > 0)
            {
                var pool = State.Work.FirstOrDefault(w => w.Kind == "water" && w.Health > 0 && w.WetUntil > world.Tick && WaveRuntimeSystem.Within(w.Position, world.Lord, Definition.PickupRadius));
                if (pool is not null) { State.Water--; State.CarriedWater = 1; State.Emit(world.Tick, "water-carried", gear.Id, pool.Id, pool.Position, pool.Position, 1); }
            }
            if (State.CarriedWater == 0) { return; }
            var plot = State.Work.Where(w => w.Kind == "grain" && w.Health > 0 && !w.Complete && w.Dry && WaveRuntimeSystem.Within(w.Position, world.Lord, Definition.PickupRadius)).OrderBy(w => w.Id).FirstOrDefault();
            if (plot is null) { return; }
            State.CarriedWater--; irrigate(plot, gear.Id, -1, gear.RewardExperience);
        }
    }
}
