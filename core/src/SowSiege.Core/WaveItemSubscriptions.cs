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
        private readonly Dictionary<(string Unit, string On), List<Subscription>> registry = new();
        private int registeredItems;
        private readonly WaveEnemyQueries queries;
        private readonly bool ownsQueries;
        private readonly List<EnemyState> candidates = new();
        private WaveRuntimeDefinition Definition => catalog.WaveRuntime!;
        private WaveRuntimeState State => world.WaveRuntime!;
        internal WaveItemSubscriptions(ContentCatalog catalog, WorldState world, WavePrimitiveModules modules, WaveEnemyQueries? queries = null)
        { this.catalog = catalog; this.world = world; this.modules = modules; ownsQueries = queries is null; this.queries = queries ?? new(world, catalog.Tuning.World.Farms.Spacing); Register(); }

        private void Register()
        {
            registry.Clear();
            foreach (var item in Definition.Items.Values)
            {
                var program = modules[item.Id];
                foreach (var unit in program.Params)
                {
                    var on = program.Value(unit.Key, "on");
                    var key = (unit.Key, on);
                    if (!registry.TryGetValue(key, out var subscribers)) { subscribers = new(); registry.Add(key, subscribers); }
                    subscribers.Add(new(item, program, unit.Key));
                }
            }
            foreach (var subscribers in registry.Values) { subscribers.Sort((a, b) => StringComparer.Ordinal.Compare(a.Item.Id, b.Item.Id)); }
            registeredItems = Definition.Items.Count;
        }
        private OwnedSubscriptions Owned(string unit, string on)
        {
            if (registeredItems != Definition.Items.Count && Definition.Programs is null) { Register(); }
            registry.TryGetValue((unit, on), out var subscribers);
            return new(State.Items, subscribers);
        }
        internal int Modifier(string stat)
        {
            var amount = 0;
            foreach (var binding in Owned("unit:stat-modifier", ""))
            { if (binding.Stat == stat) { amount = checked(amount + binding.Item.Amount); } }
            return amount;
        }
        internal bool FrontOrbit(string source)
        {
            foreach (var binding in Owned("unit:geometry-modifier", "enemy-front"))
            { if (binding.Change == "front-corner-orbit" && Linked(binding.Item, source)) { return true; } }
            return false;
        }
        internal bool WaitForReturnedGroup(string source)
        {
            foreach (var binding in Owned("unit:target-routing", "recruit"))
            { if (Linked(binding.Item, source) && binding.Selection == "available-returned-group" && binding.Fallback == "wait") { return true; } }
            return false;
        }
        private static bool Linked(WaveItemDefinition item, string source) => item.EquipmentIds.Length == 0 || item.EquipmentIds.Contains(source);
        internal Position Aim(WaveGearDefinition gear, Position origin, Position facing)
        {
            if (ownsQueries) { queries.Rebuild(); }
            foreach (var binding in Owned("unit:target-routing", "attack-ready"))
            {
                if (!Linked(binding.Item, gear.Id) || binding.Selection != "raider-in-range") { continue; }
                queries.ByDistance(candidates, origin, gear.Range, origin);
                EnemyState? raider = null;
                foreach (var candidate in candidates)
                {
                    var target = modules.Enemy(candidate.Definition).Target;
                    if (target == "seed" || target == "ripe") { raider = candidate; break; }
                }
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
                if (!Linked(binding.Item, source) || binding.Selection != "side-route") { continue; }
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
                if (binding.Action != "reposition-existing-guard") { continue; }
                var group = State.Groups.Where(g => g.Health > 0 && (g.Phase == "engaging" || g.Phase == "guarding")).OrderBy(g => g.Id).FirstOrDefault();
                if (group is not null) { group.Destination = work.Position; group.Phase = "guarding"; State.Emit(world.Tick, "harvest-guard", group.Source, group.Id, work.Position, work.Position); }
                break;
            }
        }
        internal void ReserveMissionFood(WaveGearDefinition gear, WaveGroup group)
        {
            if (world.Food <= 0) { return; }
            foreach (var binding in Owned("unit:resource-routing", "mission-start"))
            {
                if (!Linked(binding.Item, gear.Id) || binding.Resource != "food" || binding.Destination != "field-meal") { continue; }
                world.Food--; group.ReservedFood = 1; State.Emit(world.Tick, "food-reserved", gear.Id, group.Id, group.Position, group.Position, 1); break;
            }
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
            var enabled = false;
            foreach (var binding in Owned("unit:resource-routing", "enter"))
            { if (binding.Resource == "water" && binding.Source == "carried-stock") { enabled = true; break; } }
            if (!enabled) { return; }
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
        private sealed class Subscription
        {
            internal readonly WaveItemDefinition Item;
            internal readonly string Stat, Change, Selection, Fallback, Action, Resource, Destination, Source;
            internal Subscription(WaveItemDefinition item, WavePrimitiveProgram program, string unit)
            {
                Item = item;
                Stat = program.Value(unit, "stat"); Change = program.Value(unit, "change");
                Selection = program.Value(unit, "selection"); Fallback = program.Value(unit, "fallback");
                Action = program.Value(unit, "action"); Resource = program.Value(unit, "resource");
                Destination = program.Value(unit, "destination"); Source = program.Value(unit, "source");
            }
        }
        private readonly struct OwnedSubscriptions
        {
            private readonly SortedSet<string> items;
            private readonly List<Subscription>? subscribers;
            internal OwnedSubscriptions(SortedSet<string> items, List<Subscription>? subscribers)
            { this.items = items; this.subscribers = subscribers; }
            public Enumerator GetEnumerator() => new(items, subscribers);
            internal struct Enumerator
            {
                private readonly SortedSet<string> items;
                private int index;
                private readonly List<Subscription>? subscribers;
                public Subscription Current { get; private set; }
                internal Enumerator(SortedSet<string> items, List<Subscription>? subscribers)
                { this.items = items; this.subscribers = subscribers; index = 0; Current = null!; }
                public bool MoveNext()
                {
                    if (subscribers is null) { return false; }
                    while (index < subscribers.Count)
                    {
                        var binding = subscribers[index++];
                        if (items.Contains(binding.Item.Id)) { Current = binding; return true; }
                    }
                    return false;
                }
            }
        }
    }
}
