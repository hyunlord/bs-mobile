using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{

    internal sealed partial class RuntimeSystem
    {
        private readonly DiagnosticObserver? diagnostics;
        private readonly InteractiveState? interactive;
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly TrackedRandom random;
        private readonly SpatialHash spatial;
        private readonly RuntimeCatalog definition;
        private readonly RuntimeState state;
        public Action<FarmState>? HarvestFarm { get; set; }
        public Func<string, Position, bool>? PlantFarm { get; set; }
        private readonly HashSet<string> activeTriggers = new(StringComparer.Ordinal);

        public RuntimeSystem(ContentCatalog catalog, WorldState world, TrackedRandom random, SpatialHash spatial, DiagnosticObserver? diagnostics = null, InteractiveState? interactive = null)
        {
            this.diagnostics = diagnostics;
            this.interactive = interactive;
            this.catalog = catalog; this.world = world; this.random = random; this.spatial = spatial;
            definition = catalog.Runtime ?? throw new ArgumentException("Runtime content required.");
            state = world.Runtime = new();
        }

        public RuntimeEntityState Entity(int id)
        {
            if (!state.Entities.TryGetValue(id, out var value)) { value = new(); state.Entities.Add(id, value); }
            return value;
        }
        public void Experience(string category, long amount) => state.Experience.Add(new(world.Tick, category, amount));
        public int OfferWeight(string category)
        {
            state.Experience.RemoveAll(entry => world.Tick - entry.Tick > definition.Tuning.RecentExperienceWindowTicks);
            var source = category == "people" ? "land" : category;
            return Clamp(1 + state.Experience.Where(entry => entry.Category == source).Sum(entry => entry.Amount) / definition.Tuning.ExperienceWeightDivisor, 1);
        }
        public void Growth(string source, string target, long amount)
        {
            if (amount > 0)
            {
                world.FirstPlayable?.Count("equipment:" + source + ":growth");
            }

            if (amount <= 0) { return; }
            if (!state.Growth.TryGetValue(source, out var targets)) { targets = new(StringComparer.Ordinal); state.Growth.Add(source, targets); }
            targets[target] = targets.GetValueOrDefault(target) + amount;
        }
        public void Tick()
        {
            UnlockEvolutions();
            TickMapEvents();
            var tuning = definition.Tuning;
            if (world.Tick % tuning.LootPeriodTicks == 0 && state.GroundLoot.Count < tuning.MaxGroundLoot && definition.Items.Count > 0)
            {
                var sources = tuning.LootSources;
                var roll = random.Next(sources.Sum(source => source.Weight));
                var chosen = sources[0];
                foreach (var source in sources) { roll -= source.Weight; if (roll < 0) { chosen = source; break; } }
                var items = definition.Items.Values.Where(ItemEligible).OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
                if (items.Length > 0)
                {
                    var position = new Position(world.Lord.X + SignedOffset(tuning.LootSpawnRadius), world.Lord.Y + SignedOffset(tuning.LootSpawnRadius));
                    state.GroundLoot.Add(new(world.AllocateId(), chosen.Id, items[random.Next(items.Length)].Id, ClampPosition(position)));
                }
            }
            foreach (var loot in state.GroundLoot.ToArray())
            {
                if (!Within(loot.Position, world.Lord, tuning.LootPickupRadius) || !ItemEligible(definition.Items[loot.Item])) { continue; }
                var source = tuning.LootSources.Single(source => source.Id == loot.Source);
                if (world.Food < source.FoodCost) { continue; }
                world.Food -= source.FoodCost;
                state.Items[loot.Item] = checked(state.Items.GetValueOrDefault(loot.Item) + tuning.LootQuantity);
                state.Loot.Add(new(world.Tick, source.Kind, source.Id, loot.Id, loot.Item, tuning.LootQuantity, state.Items[loot.Item], source.FoodCost));
                state.GroundLoot.Remove(loot);
            }
            var liveIds = world.Farms.Select(farm => farm.Id).Concat(world.Buildings.Select(building => building.Id)).Concat(world.People.Select(person => person.Id)).ToHashSet();
            foreach (var id in state.Entities.Keys.Where(id => !liveIds.Contains(id)).ToArray()) { state.Entities.Remove(id); }
        }
        private int SignedOffset(int radius) => random.Next(radius + 1) * ((random.Next(int.MaxValue) & 1) == 0 ? -1 : 1);
        private bool ItemEligible(ItemDefinition item) => item.RequiredTags.All(tag => world.Equipment.Any(equipment => Tags(equipment.Id).Contains(tag, StringComparer.Ordinal)));
        public void UnlockEvolutions()
        {
            foreach (var evolution in definition.Evolutions.Values.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                if (!EvolutionEligible(catalog, world, evolution) || !state.Evolutions.Add(evolution.Id)) { continue; }
                state.EvolutionEvents.Add(new(world.Tick, evolution.Id, evolution.BaseId));
                if (world.FirstPlayable is { } fp) { fp.Count("evolution:" + evolution.Id); interactive?.Experience(world.Tick, PresentationKind.Evolution, evolution.Id, world.Lord, 0); }
            }
        }
        private IEnumerable<OwnedEffect> OwnedEffects()
        {
            var effects = new List<OwnedEffect>();
            foreach (var equipment in world.Equipment)
            {
                if (definition.Equipment.TryGetValue(equipment.Id, out var runtime)) { effects.AddRange(runtime.Effects.Select(effect => new OwnedEffect(equipment.Id, catalog.Tools.ContainsKey(equipment.Id) ? "tool" : "weapon", 1, effect))); }
            }
            foreach (var pair in state.Charters) { effects.AddRange(definition.Charters[pair.Key].Effects.Select(effect => new OwnedEffect(pair.Key, "charter", pair.Value, effect))); }
            foreach (var pair in state.Items) { if (world.FirstPlayable is not null && !ItemEligible(definition.Items[pair.Key])) { continue; } effects.AddRange(definition.Items[pair.Key].Effects.Select(effect => new OwnedEffect(pair.Key, "item", pair.Value, effect))); }
            foreach (var id in state.Evolutions) { effects.AddRange(definition.Evolutions[id].Effects.Select(effect => new OwnedEffect(id, "evolution", 1, effect))); }
            return effects.OrderBy(effect => effect.Source, StringComparer.Ordinal).ThenBy(effect => effect.Definition.Id, StringComparer.Ordinal);
        }
        public int Modify(string subject, int original, EffectContext context, int minimum = 0)
        {
            var result = original;
            foreach (var effect in OwnedEffects().Where(effect => effect.Definition.Trigger == "modifier" && effect.Definition.Operation == "stat-add" && effect.Definition.Subject == subject))
            {
                if (!Matches(effect.Definition, context)) { continue; }
                var changed = Clamp((long)result + (long)effect.Definition.Amount * effect.Stacks, minimum);
                Record(effect, (long)changed - result); result = changed;
            }
            return result;
        }
        public Position PlantingPosition(string source, Position original)
        {
            var result = original;
            foreach (var effect in OwnedEffects().Where(effect => effect.Definition.Trigger == "modifier" && effect.Definition.Operation == "planting-bias"))
            {
                if (!Matches(effect.Definition, new(result, source))) { continue; }
                var target = effect.Definition.Subject == "estate-inward" ? world.Estate : world.Farms.OrderBy(farm => farm.Position.DistanceSquared(result)).ThenBy(farm => farm.Id).FirstOrDefault()?.Position;
                if (target is null) { continue; }
                var changed = result.MoveToward(target.Value, Clamp((long)effect.Definition.Amount * effect.Stacks, 0));
                Record(effect, Math.Max(Math.Abs(changed.X - result.X), Math.Abs(changed.Y - result.Y))); result = changed;
            }
            return ClampPosition(result);
        }
        public void Emit(string trigger, EffectContext context)
        {
            if (!activeTriggers.Add(trigger)) { return; }
            try
            {
                foreach (var effect in OwnedEffects().Where(effect => effect.Definition.Trigger == trigger))
                {
                    if (!Matches(effect.Definition, context) || world.Food < effect.Definition.FoodCost) { continue; }
                    world.Food -= effect.Definition.FoodCost;
                    var amount = Apply(effect, context);
                    if (amount == 0) { world.Food += effect.Definition.FoodCost; continue; }
                    Record(effect, amount);
                }
            }
            finally { activeTriggers.Remove(trigger); }
        }
        private long Apply(OwnedEffect owned, EffectContext context)
        {
            var effect = owned.Definition;
            var amount = Clamp((long)effect.Amount * owned.Stacks, 0);
            switch (effect.Operation)
            {
                case "damage-pulse":
                    var weapon = world.Equipment.Where(equipment => catalog.Weapons.ContainsKey(equipment.Id) && Tags(equipment.Id).Contains("melee", StringComparer.Ordinal)).OrderBy(equipment => equipment.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (weapon is null) { return 0; }
                    long damage = 0; var candidates = 0;
                    var visualHits = interactive is null ? null : new List<EnemyState>();
                    foreach (var enemy in spatial.Query(context.Origin, effect.Radius))
                    {
                        candidates++;
                        if (effect.Subject == "weapon-front" && !InFront(context.Origin, enemy.Position)) { continue; }
                        visualHits?.Add(enemy);
                        var dealt = Math.Min(enemy.Health, amount); enemy.Health -= dealt; damage += dealt;
                        diagnostics?.Hit("runtime-pulse", "", owned.Source, amount, dealt);
                    }
                    diagnostics?.Attack("runtime-pulse", "", owned.Source, candidates, 0);
                    var direction = new Position(world.Destination.X - world.Lord.X, world.Destination.Y - world.Lord.Y);
                    if (direction == new Position(0, 0)) { direction = new(1, 0); }
                    interactive?.Attack(world.Tick, owned.Source, context.Origin, direction, effect.Subject == "weapon-front" ? "sector180" : "disk", effect.Radius, visualHits!);
                    world.WeaponDamage += damage; return damage;
                case "repair-nearest":
                    var building = world.Buildings.Where(building => building.Built && (world.FirstPlayable is null || building.Health > 0) && building.Health < catalog.Tuning.World.Buildings.Health && Within(building.Position, context.Origin, effect.Radius)).OrderBy(building => building.Position.DistanceSquared(context.Origin)).ThenBy(building => building.Id).FirstOrDefault();
                    if (building is null) { return 0; }
                    var repaired = Math.Min(amount, catalog.Tuning.World.Buildings.Health - building.Health);
                    if (repaired > 0 && building.Health == 0) { world.Rebuilds++; }
                    var wasRuined = building.Health == 0;
                    building.Health += repaired;
                    if (repaired > 0) { Emit("repair", new(building.Position, Building: building, WasRuined: wasRuined)); }
                    return repaired;
                case "worker-buff":
                    long duration = 0;
                    foreach (var person in world.People.Where(person => person.Role == "peasant" && Within(person.Position, context.Origin, effect.Radius)).OrderBy(person => person.Id).Take(amount))
                    {
                        var until = Deadline(effect.DurationTicks); duration += Math.Max(0, until - Math.Max(person.DutyUntil, world.Tick)); person.DutyUntil = Math.Max(person.DutyUntil, until);
                    }
                    return duration;
                case "rally-returners":
                    long held = 0;
                    foreach (var person in world.People.Where(person => person.Role == "returning" && Within(person.Position, context.Origin, effect.Radius)).OrderBy(person => person.Id).Take(amount))
                    {
                        var data = Entity(person.Id); var until = Deadline(effect.DurationTicks); held += Math.Max(0, until - Math.Max(data.HoldUntil, world.Tick)); data.HoldUntil = Math.Max(data.HoldUntil, until); data.Waypoint = context.Origin;
                    }
                    return held;
                case "plant-path":
                    if ((context.Enemy is null && (world.FirstPlayable is null || context.Farm is null)) || PlantFarm is null || !world.Tools.ContainsKey(effect.Subject)) { return 0; }
                    long planted = 0;
                    for (var index = 0; index < amount; index++)
                    {
                        var distance = Math.Min(effect.Radius, catalog.Tuning.World.Farms.Spacing * (index + 1));
                        var destination = context.Enemy?.Position ?? new Position(context.Origin.X + (world.WeaponCombat?.Facing.X ?? 1) * effect.Radius, context.Origin.Y + (world.WeaponCombat?.Facing.Y ?? 0) * effect.Radius);
                        var attempts = world.FirstPlayable is not null && context.Farm is not null ? Math.Max(1, effect.Radius / catalog.Tuning.World.Farms.Spacing) : 1;
                        if (attempts > 1)
                        {
                            destination = new Position(context.Origin.X + (world.WeaponCombat?.Facing.X ?? 1) * effect.Radius, context.Origin.Y + (world.WeaponCombat?.Facing.Y ?? 0) * effect.Radius);
                        }

                        for (var attempt = 0; attempt < attempts; attempt++)
                        {
                            if (PlantFarm(effect.Subject, context.Origin.MoveToward(destination, Math.Min(effect.Radius, distance + attempt * catalog.Tuning.World.Farms.Spacing)))) { planted++; break; }
                        }
                    }
                    return planted;
                case "shield-farms":
                    long charges = 0;
                    foreach (var farm in world.Farms.Where(farm => Within(farm.Position, context.Origin, effect.Radius)))
                    {
                        var data = Entity(farm.Id); var old = data.ShieldUntil > world.Tick ? data.Shield : 0;
                        if (amount <= old) { continue; }
                        data.Shield = amount; data.ShieldUntil = Deadline(effect.DurationTicks); charges += amount - old;
                    }
                    return charges;
                case "harvest-near":
                    var ripe = world.Farms.Where(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1 && Within(farm.Position, context.Origin, effect.Radius)).OrderBy(farm => farm.Id).Take(amount).ToArray();
                    if (HarvestFarm is null) { return 0; }
                    foreach (var farm in ripe) { HarvestFarm(farm); }
                    return ripe.Length;
                case "damage-young-plots":
                    long lost = 0;
                    foreach (var farm in world.Farms.Where(farm => farm.Stage < catalog.Tuning.World.Farms.StageTicks.Length - 1 && farm.Progress > 0 && Within(farm.Position, context.Origin, effect.Radius)).OrderBy(farm => farm.Id).Take(amount)) { lost += farm.Progress; farm.Progress = 0; }
                    return lost;
                case "extend-duty":
                    if (context.Person is null && world.FirstPlayable is not null)
                    {
                        long extended = 0;
                        foreach (var person in world.People.Where(p => p.Role is "militia" or "guard" && Within(p.Position, context.Origin, effect.Radius)))
                        { var before = Math.Max(person.DutyUntil, world.Tick); person.DutyUntil = Clamp((long)before + (long)effect.DurationTicks * amount, 0); extended += person.DutyUntil - before; }
                        return extended;
                    }
                    if (context.Person is null) { return 0; }
                    var dutyStart = Math.Max(context.Person.DutyUntil, world.Tick);
                    context.Person.DutyUntil = Clamp((long)dutyStart + (long)effect.DurationTicks * amount, 0);
                    return context.Person.DutyUntil - dutyStart;
                case "guard-return":
                    if (context.Person is null || Entity(context.Person.Id).ArrivalGuardUsed) { return 0; }
                    var guard = Entity(context.Person.Id);
                    var holdStart = Math.Max(guard.HoldUntil, world.Tick);
                    guard.ArrivalGuardUsed = true;
                    guard.HoldUntil = Clamp((long)holdStart + (long)effect.DurationTicks * amount, 0);
                    return guard.HoldUntil - holdStart;
                case "pause-neighbor-growth":
                    long paused = 0;
                    foreach (var farm in world.Farms.Where(farm => farm != context.Farm && Within(farm.Position, context.Origin, effect.Radius)))
                    {
                        var data = Entity(farm.Id); var until = Deadline(effect.DurationTicks); paused += Math.Max(0, until - Math.Max(data.PauseUntil, world.Tick)); data.PauseUntil = Math.Max(data.PauseUntil, until);
                    }
                    return paused;
                case "return-via-building":
                    if (context.Person is null) { return 0; }
                    var waypoint = world.Buildings.Where(building => building.Built && building.Health > 0 && Within(building.Position, context.Origin, effect.Radius)).OrderBy(building => building.Position.DistanceSquared(context.Origin)).ThenBy(building => building.Id).FirstOrDefault();
                    if (waypoint is null || waypoint.Position == context.Person.Position || waypoint.Position == world.Estate) { return 0; }
                    Entity(context.Person.Id).Waypoint = waypoint.Position; return 1;
                default: throw new InvalidOperationException($"Unsupported runtime operation {effect.Operation}.");
            }
        }
        public bool ShieldFarm(FarmState farm, EnemyState enemy)
        {
            var data = Entity(farm.Id);
            var consumed = data.Shield > 0 && data.ShieldUntil > world.Tick;
            if (consumed) { data.Shield--; }
            Emit("farm-hit", new(farm.Position, Enemy: enemy, Farm: farm, ShieldConsumed: consumed));
            return consumed;
        }
        public bool CanDraft(PersonState person)
        {
            var data = Entity(person.Id);
            return !data.HasReturned || world.Tick - data.RestSince >= Modify("draft-min-rest", 0, new(person.Position, Person: person));
        }
        public void Returned(PersonState person)
        {
            var data = Entity(person.Id); data.HasReturned = true; data.RestSince = world.Tick; data.ArrivalGuardUsed = false; data.Waypoint = null;
        }
        public int BuildingDamage(BuildingState building, EnemyState enemy, int damage)
        {
            var facing = Entity(building.Id).Facing;
            var dot = (long)(enemy.Position.X - building.Position.X) * facing.X + (long)(enemy.Position.Y - building.Position.Y) * facing.Y;
            return Modify(dot >= 0 ? "building-front-damage" : "building-rear-damage", damage, new(building.Position, Enemy: enemy, Building: building));
        }
        private bool Matches(RuntimeEffectDefinition effect, EffectContext context) => effect.Conditions.All(condition => condition.Kind switch
        {
            "equipment-owned" => world.Equipment.Any(equipment => equipment.Id == condition.Value),
            "owned-tag" => world.Equipment.Any(equipment => Tags(equipment.Id).Contains(condition.Value, StringComparer.Ordinal)),
            "equipment-tag" => Tags(context.Equipment).Contains(condition.Value, StringComparer.Ordinal),
            "equipment-id" => context.Equipment == condition.Value,
            "equipment-kind" => condition.Value == "tool" ? catalog.Tools.ContainsKey(context.Equipment) : catalog.Weapons.ContainsKey(context.Equipment),
            "growth-target" => catalog.Tools.TryGetValue(context.Equipment, out var tool) && (tool.Growth.Target == condition.Value || definition.Equipment.TryGetValue(tool.Id, out var runtime) && runtime.GrowthActions.Any(action => action.Target == condition.Value)),
            "near-seed" => world.Farms.Any(farm => farm.Stage <= 1 && Within(farm.Position, context.Origin, effect.Radius)),
            "near-ripe" => world.Farms.Any(farm => farm.Stage == catalog.Tuning.World.Farms.StageTicks.Length - 1 && Within(farm.Position, context.Origin, effect.Radius)),
            "near-building" => world.Buildings.Any(building => building.Built && building.Health > 0 && Within(building.Position, context.Origin, effect.Radius)),
            "estate-inside" => Within(context.Origin, world.Estate, catalog.Tuning.World.Map.EstateRadius),
            "estate-outside" => !Within(context.Origin, world.Estate, catalog.Tuning.World.Map.EstateRadius),
            "building-ruined" => context.WasRuined,
            "building-new" => context.WasNew,
            "person-role" => context.Person?.Role == condition.Value,
            "enemy-target" => context.Enemy is not null && catalog.Enemies[context.Enemy.Definition].Target == condition.Value,
            "shield-consumed" => context.ShieldConsumed,
            "season" => world.Season == condition.Minimum,
            "count" => (condition.Value switch { "farms" => world.Farms.Count, "buildings" => world.Buildings.Count(building => building.Built && building.Health > 0), "people" => world.People.Sum(person => person.Members), "harvests" => world.Harvests, _ => throw new InvalidOperationException("Unknown count selector.") }) >= condition.Minimum,
            _ => throw new InvalidOperationException($"Unknown runtime condition {condition.Kind}.")
        });
        private string[] Tags(string id) => catalog.Tools.TryGetValue(id, out var tool) ? tool.Tags : catalog.Weapons.TryGetValue(id, out var weapon) ? weapon.Tags : Array.Empty<string>();
        private void Record(OwnedEffect effect, long amount)
        {
            if (amount == 0) { return; }
            if (world.FirstPlayable is { } fp)
            {
                fp.Count(effect.Kind + ":" + effect.Source + ":effect");
                if (effect.Kind == "charter")
                {
                    fp.Count("charter:" + effect.Source + (effect.Definition.Subject.StartsWith("attack-", StringComparison.Ordinal) || effect.Definition.Operation == "damage-pulse" ? ":weapon" : ":estate"));
                }
            }
            if (!state.Effects.TryGetValue(effect.Definition.Id, out var counter)) { counter = new(); state.Effects.Add(effect.Definition.Id, counter); }
            counter.Count++; counter.Total += amount; counter.First ??= world.Tick; counter.Last = world.Tick;
        }
        private bool InFront(Position origin, Position target)
        {
            var dx = world.Destination.X - world.Lord.X;
            var dy = world.Destination.Y - world.Lord.Y;
            if (dx == 0 && dy == 0) { dx = 1; }
            return (long)(target.X - origin.X) * dx + (long)(target.Y - origin.Y) * dy >= 0;
        }
        private int Deadline(int duration) => Clamp((long)world.Tick + duration, 0);
        private static int Clamp(long value, int minimum) => (int)Math.Clamp(value, minimum, int.MaxValue);
        internal static bool Within(Position first, Position second, int radius) => first.DistanceSquared(second) <= (long)radius * radius;
        private Position ClampPosition(Position position) => new(Math.Clamp(position.X, 0, catalog.Tuning.World.Map.Width), Math.Clamp(position.Y, 0, catalog.Tuning.World.Map.Height));
        public RuntimeResult Result()
        {
            var definitions = definition.Equipment.Values.SelectMany(equipment => equipment.Effects.Select(effect => new OwnedEffect(equipment.Id, catalog.Tools.ContainsKey(equipment.Id) ? "tool" : "weapon", 1, effect)))
                .Concat(definition.Charters.Values.SelectMany(charter => charter.Effects.Select(effect => new OwnedEffect(charter.Id, "charter", 1, effect))))
                .Concat(definition.Items.Values.SelectMany(item => item.Effects.Select(effect => new OwnedEffect(item.Id, "item", 1, effect))))
                .Concat(definition.Evolutions.Values.SelectMany(evolution => evolution.Effects.Select(effect => new OwnedEffect(evolution.Id, "evolution", 1, effect))))
                .OrderBy(owned => owned.Source, StringComparer.Ordinal).ThenBy(owned => owned.Definition.Id, StringComparer.Ordinal);
            var effects = definitions.Select(owned =>
            {
                var effect = owned.Definition; var counter = state.Effects.GetValueOrDefault(effect.Id);
                return new RuntimeEffectTelemetry(effect.Id, owned.Source, owned.Kind, effect.Trigger, effect.Operation, effect.Subject, effect.Amount, counter?.Count ?? 0, counter?.Total ?? 0, counter?.First, counter?.Last);
            }).ToArray();
            return new(effects, state.Loot.ToArray(), state.EvolutionEvents.ToArray(), new(world.Equipment.Where(equipment => catalog.Weapons.ContainsKey(equipment.Id)).Select(equipment => equipment.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray(), world.Equipment.Where(equipment => catalog.Tools.ContainsKey(equipment.Id)).Select(equipment => equipment.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray(), new SortedDictionary<string, int>(state.Charters, StringComparer.Ordinal), new SortedDictionary<string, int>(state.Items, StringComparer.Ordinal), state.Evolutions.ToArray()), state.Growth.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, long>)new SortedDictionary<string, long>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal));
        }
    }

}
