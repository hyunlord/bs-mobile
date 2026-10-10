using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    internal sealed class WaveRuntimeSystem
    {
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly WaveRuntimeDefinition definition;
        private readonly WaveRuntimeState state;
        private readonly InteractiveState? interactive;
        private readonly WaveEnemySystem enemies;
        private readonly WaveWorkSystem work;
        private readonly WavePrimitiveModules modules;
        private readonly WaveItemSubscriptions subscriptions;
        private readonly WaveEnemyQueries queries;
        private readonly List<EnemyState> targets = new();
        private readonly List<EnemyState> secondaryTargets = new();
        private readonly List<EnemyState> frontTargets = new();
        private readonly List<WaveWork> harvestPlots = new();
        private readonly HashSet<int> visited = new();
        private readonly HashSet<int> expiredActivations = new();
        private readonly Dictionary<string, List<WaveAttackView>> orbitGroups = new(StringComparer.Ordinal);
        private readonly List<string> orbitSources = new();
        private readonly Dictionary<(string Id, int Level), (WaveGearDefinition Source, WaveGearLevel Parameters, WaveGearDefinition Value)> levels = new();
        private static readonly Position[] OrbitDirections = { new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1), new(0, -1), new(1, -1) };
        public WaveRuntimeSystem(ContentCatalog catalog, WorldState world, TrackedRandom random, InteractiveState? interactive)
        {
            this.catalog = catalog;
            this.world = world;
            this.interactive = interactive;
            definition = catalog.WaveRuntime!;
            state = world.WaveRuntime = new()
            {
                Water = definition.WaterCapacity,
                Timber = definition.InitialTimber,
                TimberOrigin = world.Estate,
                AvailableWorkers = definition.InitialWorkers
            };
            modules = new(definition);
            queries = new(world, catalog.Tuning.World.Farms.Spacing);
            subscriptions = new(catalog, world, modules, queries);
            enemies = new(catalog, world, random, interactive, modules, subscriptions, queries);
            work = new(catalog, world, interactive, modules, subscriptions, queries);
        }
        public int MovementSpeed(int original) => original + subscriptions.Modifier("move-speed");
        public bool EligibleEvolution(WaveEvolutionDefinition e) => !state.Evolutions.Contains(e.Id) && e.InputIds.All(id => world.Equipment.Any(x => x.Id == id)) &&
            WaveGrowthPrimitives.Eligible(modules[e.Id], state);
        public IEnumerable<string> ExtraCards() => definition.Items.Values.Where(i => !state.Items.Contains(i.Id) && (i.EquipmentIds.Length == 0 || i.EquipmentIds.Any(id => world.Equipment.Any(e => e.Id == id)))).Select(i => i.Id).Concat(definition.Evolutions.Values.Where(EligibleEvolution).Select(e => e.Id));
        public bool Select(string id)
        {
            if (definition.Items.ContainsKey(id))
            {
                state.Items.Add(id);
                state.Emit(world.Tick, "item-selected", id, -1, world.Lord, world.Lord);
                return true;
            }
            if (!definition.Evolutions.TryGetValue(id, out var e))
            {
                return false;
            }

            if (!EligibleEvolution(e))
            {
                throw new InvalidOperationException("Evolution conditions not fulfilled.");
            }

            state.Evolutions.Add(id);
            state.Emit(world.Tick, "evolution-activated", id, -1, world.Lord, world.Lord);
            return true;
        }
        public bool Suppressed(string id)
        {
            foreach (var evolutionId in state.Evolutions)
            {
                var evolution = definition.Evolutions[evolutionId];
                if (modules.Is(evolution.Id, "unit:evolution-replace", "replace", "both-tool-activations") && Array.IndexOf(evolution.InputIds, id) >= 0) { return true; }
            }
            return false;
        }
        public void Tick(Position previous)
        {
            state.Events.Clear();
            expiredActivations.Clear();
            foreach (var attack in state.Attacks)
            {
                if (attack.ExpireTick <= world.Tick && expiredActivations.Add(attack.ActivationId)) { state.ResolveActivation(attack.ActivationId); }
            }

            state.Attacks.RemoveAll(a => a.ExpireTick <= world.Tick);
            if (world.Lord != previous)
            {
                state.Facing = new(world.Lord.X - previous.X, world.Lord.Y - previous.Y);
            }

            enemies.Tick();
            queries.Rebuild();
            foreach (var equipment in world.Equipment)
            {
                if (equipment.ReadyTick > world.Tick || Suppressed(equipment.Id))
                {
                    continue;
                }

                var gear = AtLevel(definition.Gear[equipment.Id], equipment.Level);
                equipment.ReadyTick = world.Tick + gear.CooldownTicks;
                WaveEvolutionDefinition? evolution = null;
                foreach (var id in state.Evolutions)
                {
                    var candidate = definition.Evolutions[id];
                    if (candidate.InputIds[0] == gear.Id && !modules.Is(candidate.Id, "unit:evolution-replace", "replace", "both-tool-activations")) { evolution = candidate; break; }
                }
                Attack(gear, evolution);
            }
            foreach (var id in state.Evolutions)
            {
                var e = definition.Evolutions[id];
                if (!modules.Is(e.Id, "unit:evolution-replace", "replace", "both-tool-activations")) { continue; }
                var first = world.Equipment.First(x => x.Id == e.InputIds[0]);
                if (first.ReadyTick > world.Tick)
                {
                    continue;
                }

                var gear = AtLevel(definition.Gear[e.InputIds[1]], first.Level);
                first.ReadyTick = world.Tick + gear.CooldownTicks;
                Attack(gear with
                {
                    Id = e.Id
                }, e);
            }
            TickOrbits();
            TickProjectiles();
            work.Tick();
            foreach (var enemy in world.Enemies)
            {
                if (enemy.Health > 0) { continue; }
                work.OnKill(enemy);
                var xp = catalog.Enemies[enemy.Definition].Experience;
                world.Experience += xp;
                world.KillExperience += xp;
                state.Emit(world.Tick, "enemy-killed", enemy.Definition, enemy.Id, enemy.Position, enemy.Position, xp);
                if (enemy.Definition == definition.BossId)
                {
                    state.BossDefeated = true;
                }

                state.EnemyActions.Remove(enemy.Id);
            }
            world.Enemies.RemoveAll(e => e.Health <= 0);
        }
        private WaveGearDefinition AtLevel(WaveGearDefinition gear, int level)
        {
            if (gear.Levels is null || gear.Levels.Length == 0)
            {
                return gear;
            }

            var levelIndex = Math.Min(level, gear.Levels.Length) - 1;
            var p = gear.Levels[levelIndex];
            var key = (gear.Id, levelIndex);
            if (levels.TryGetValue(key, out var cached) && ReferenceEquals(cached.Source, gear) && ReferenceEquals(cached.Parameters, p)) { return cached.Value; }
            var value = gear with
            {
                Damage = p.Damage,
                Range = p.Range,
                CooldownTicks = p.CooldownTicks,
                Speed = p.Speed,
                Count = p.Count,
                LifetimeTicks = p.LifetimeTicks,
                Knockback = p.Knockback
            };
            levels[key] = (gear, p, value);
            return value;
        }
        private void Attack(WaveGearDefinition gear, WaveEvolutionDefinition? evolution)
        {
            var program = modules[evolution?.Id ?? gear.Id];
            var operations = modules.Attack(evolution?.Id ?? gear.Id);
            var shape = operations.Shape;
            if (operations.DryVariant && state.Water <= 0)
            {
                gear = gear with
                {
                    Damage = Math.Max(1, gear.Damage / definition.Behavior.DryDamageDivisor),
                    Knockback = 0
                }
;
            }

            var origin = world.Lord;
            var facing = state.Facing;
            if (evolution is not null && modules.Has(evolution.Id, "unit:attack-anchor"))
            {
                var building = RepairAnchor();
                if (building != null)
                {
                    origin = building.Position;
                }
            }
            facing = subscriptions.Aim(gear, origin, facing);
            var source = evolution?.Id ?? gear.Id;
            targets.Clear();
            if (shape != "chain" && shape != "orbit") { queries.ByDistance(targets, origin, gear.Range, origin); }
            var children = shape == "homing-projectile" ? Math.Min(targets.Count, gear.Count) : 0;
            var activation = state.BeginActivation(source, children, world.Tick + (shape == "orbit" ? gear.CooldownTicks : gear.LifetimeTicks));
            if (shape == "homing-projectile")
            {
                for (var targetIndex = 0; targetIndex < Math.Min(targets.Count, gear.Count); targetIndex++)
                {
                    var target = targets[targetIndex];
                    state.Projectiles.Add(new()
                    {
                        Id = world.AllocateId(),
                        Source = source,
                        ActivationId = activation,
                        Position = origin,
                        Previous = origin,
                        TargetId = target.Id,
                        Speed = gear.Speed,
                        Damage = gear.Damage,
                        ExpireTick = world.Tick + gear.LifetimeTicks
                    }
                    );
                    state.Emit(world.Tick, "projectile-launched", source, target.Id, origin, target.Position);
                }
            }
            else if (shape == "chain")
            {
                var from = origin;
                visited.Clear();
                for (int i = 0; i < gear.Count; i++)
                {
                    queries.ByDistance(secondaryTargets, from, gear.Range, from);
                    EnemyState? next = null;
                    foreach (var candidate in secondaryTargets)
                    {
                        if (visited.Contains(candidate.Id)) { continue; }
                        next ??= candidate;
                        if (i == 0 || !operations.WetPriority || state.EnemyActions.TryGetValue(candidate.Id, out var wet) && wet.WetUntil > world.Tick)
                        { next = candidate; break; }
                    }
                    if (next == null)
                    {
                        break;
                    }

                    visited.Add(next.Id);
                    state.Emit(world.Tick, "chain-link", source, next.Id, from, next.Position);
                    Hit(next, gear.Damage, source, origin, gear.Knockback, activation);
                    if (operations.BriefStop && state.EnemyActions.TryGetValue(next.Id, out var action))
                    {
                        action.StopUntil = world.Tick + definition.StopTicks;
                    }

                    from = next.Position;
                }
            }
            else if (shape == "orbit")
            {
                var phase = (world.Tick / Math.Max(1, gear.CooldownTicks)) % WaveGeometry.OrbitDirections;
                var dirs = OrbitDirections
                ;
                for (int i = 0; i < gear.Count; i++)
                {
                    var direction = subscriptions.FrontOrbit(gear.Id) && FrontEnemy(origin, facing, gear.Range) ? new Position(facing.X + (i % WaveGeometry.MidpointDivisor == 0 ? -facing.Y : facing.Y), facing.Y + (i % WaveGeometry.MidpointDivisor == 0 ? facing.X : -facing.X)) : dirs[(phase + i * WaveGeometry.OrbitDirections / gear.Count) % WaveGeometry.OrbitDirections];
                    var point = origin.MoveToward(new(origin.X + direction.X * gear.Range, origin.Y + direction.Y * gear.Range), gear.Range);
                    state.Attacks.Add(new(source, "orbit", new(origin.X, origin.Y), new(point.X, point.Y), Math.Max(1, gear.Speed), world.Tick + gear.CooldownTicks, activation));
                    state.Emit(world.Tick, "orbit-fragment", source, -1, origin, point, Math.Max(1, gear.Speed));
                }
            }
            else if (shape is "melee-fan" or "ground-slam" or "expanding-wave")
            {
                foreach (var enemy in targets)
                {
                    if (shape != "expanding-wave" && !InArc(origin, facing, enemy.Position, gear.Range)) { continue; }
                    Hit(enemy, gear.Damage, source, origin, gear.Knockback, activation);
                }
                state.Emit(world.Tick, "attack", source, -1, origin, new(origin.X + facing.X, origin.Y + facing.Y), gear.Range);
                if (operations.Harvest)
                {
                    harvestPlots.Clear();
                    foreach (var plot in state.Work)
                    {
                        if (plot.Kind == "grain" && plot.Complete && plot.Health > 0 && HarvestSource(operations, plot.Source) && InArc(origin, facing, plot.Position, gear.Range)) { harvestPlots.Add(plot); }
                    }
                    foreach (var plot in harvestPlots)
                    {
                        work.Harvest(plot);
                        queries.ById(secondaryTargets, plot.Position, gear.Range / WaveGeometry.MidpointDivisor);
                        for (var fragment = 0; fragment < Math.Min(secondaryTargets.Count, gear.Count); fragment++)
                        {
                            Hit(secondaryTargets[fragment], gear.Damage, source, plot.Position, 0, activation);
                        }

                        state.Emit(world.Tick, "harvest-fragments", source, plot.Id, plot.Position, plot.Position, gear.Range / WaveGeometry.MidpointDivisor);
                    }
                }
            }
            else { throw new ArgumentException("Unsupported attack shape: " + shape); }
            if (world.Tools.TryGetValue(gear.Id, out var ledger))
            {
                ledger.Activations++;
            }

            if (evolution is not null && modules.Is(evolution.Id, "unit:remnant-create", "placement", "attack-footprint"))
            {
                work.PlantSweep(evolution.InputIds[1], origin, facing, gear.Range, Math.Max(WaveGeometry.MinimumFanSamples, gear.Count));
            }
            else if (program.Has("unit:remnant-create")) { work.Activate(gear, origin, facing); }
            if (shape != "orbit" && (shape != "homing-projectile" || children == 0))
            {
                state.ResolveActivation(activation);
            }
        }

        private void Hit(EnemyState enemy, int damage, string source, Position origin, int knockback, int activation)
        {
            var actual = Math.Min(enemy.Health, damage);
            if (actual <= 0)
            {
                return;
            }

            enemy.Health -= actual;
            world.WeaponDamage += actual;
            state.RecordDamage(activation, damage, actual);
            if (knockback > 0)
            {
                queries.Move(enemy, new(Math.Clamp(enemy.Position.X + Math.Sign(enemy.Position.X - origin.X) * knockback, 0, catalog.Tuning.World.Map.Width), Math.Clamp(enemy.Position.Y + Math.Sign(enemy.Position.Y - origin.Y) * knockback, 0, catalog.Tuning.World.Map.Height)));
            }

            if (enemy.Health <= 0 && definition.Gear.TryGetValue(source, out var gear) && !programHasRemnant(source) && modules.Is(source, "unit:attack-shape", "shape", "melee-fan") && !modules.Has(source, "unit:harvest-contact") && state.Work.Any(w => w.Kind == "grain" && Within(w.Position, enemy.Position, catalog.Tuning.World.Farms.Spacing)))
            {
                state.BladePlotKill = true;
            }

            state.Emit(world.Tick, "hit", source, enemy.Id, enemy.Position, enemy.Position, actual);
            subscriptions.Hit(source, enemy);
        }
        private void TickOrbits()
        {
            orbitSources.Clear();
            foreach (var group in orbitGroups.Values) { group.Clear(); }
            foreach (var attack in state.Attacks)
            {
                if (!orbitGroups.TryGetValue(attack.Source, out var group)) { group = new(); orbitGroups.Add(attack.Source, group); }
                if (group.Count == 0) { orbitSources.Add(attack.Source); }
                group.Add(attack);
            }
            foreach (var source in orbitSources)
            {
                var group = orbitGroups[source];
                var evolution = definition.Evolutions.TryGetValue(source, out var evolved) ? evolved : null;
                var gear = definition.Gear[evolution?.InputIds[0] ?? source];
                var level = world.Equipment.First(e => e.Id == gear.Id).Level;
                gear = AtLevel(gear, level);
                var anchor = world.Lord;
                if (evolution is not null && modules.Has(evolution.Id, "unit:attack-anchor"))
                {
                    var building = RepairAnchor();
                    if (building is not null)
                    {
                        anchor = building.Position;
                    }
                }
                var index = 0;
                var dirs = OrbitDirections
;
                foreach (var old in group)
                {
                    var phase = (world.Tick / Math.Max(1, gear.CooldownTicks / WaveGeometry.OrbitDirections) + index * WaveGeometry.OrbitDirections / gear.Count) % WaveGeometry.OrbitDirections;
                    var facing = state.Facing;
                    var direction = subscriptions.FrontOrbit(gear.Id) && FrontEnemy(anchor, facing, gear.Range) ? new Position(facing.X + (index % WaveGeometry.MidpointDivisor == 0 ? -facing.Y : facing.Y), facing.Y + (index % WaveGeometry.MidpointDivisor == 0 ? facing.X : -facing.X)) : dirs[phase];
                    var point = anchor.MoveToward(new(anchor.X + direction.X * gear.Range, anchor.Y + direction.Y * gear.Range), gear.Range);
                    var next = old with
                    {
                        Origin = new(anchor.X, anchor.Y),
                        Position = new(point.X, point.Y)
                    }
;
                    state.Attacks[state.Attacks.IndexOf(old)] = next;
                    queries.ByWorldOrder(secondaryTargets, point, next.Radius);
                    foreach (var enemy in secondaryTargets)
                    {
                        if (state.Activations[old.ActivationId].HitTargets.Add(enemy.Id))
                        {
                            Hit(enemy, gear.Damage, source, anchor, gear.Knockback, old.ActivationId);
                        }
                    }
                    index++;
                }
            }
        }
        private void TickProjectiles()
        {
            for (var index = 0; index < state.Projectiles.Count;)
            {
                var p = state.Projectiles[index];
                if (p.Hostile) { index++; continue; }
                var target = queries.Find(p.TargetId);
                if (target == null || p.ExpireTick <= world.Tick)
                {
                    state.Projectiles.RemoveAt(index);
                    state.ResolveChild(p.ActivationId);
                    continue;
                }
                p.Previous = p.Position;
                p.Position = p.Position.MoveToward(target.Position, p.Speed);
                if (enemies.Intercepts(p.Previous, p.Position))
                {
                    state.Projectiles.RemoveAt(index);
                    state.ResolveChild(p.ActivationId);
                    continue;
                }
                if (Within(p.Position, target.Position, Math.Max(1, p.Speed / WaveGeometry.MidpointDivisor)))
                {
                    Hit(target, p.Damage, p.Source, p.Previous, 0, p.ActivationId);
                    state.Projectiles.RemoveAt(index);
                    state.ResolveChild(p.ActivationId);
                    continue;
                }
                index++;
            }
        }
        private WaveWork? RepairAnchor()
        {
            WaveWork? nearest = null;
            var bestDistance = long.MaxValue;
            foreach (var work in state.Work)
            {
                if (work.Kind != "building" || work.Health <= 0 || work.Complete || work.ReadyTick != -1) { continue; }
                var distance = work.Position.DistanceSquared(world.Lord);
                if (distance < bestDistance || distance == bestDistance && (nearest is null || work.Id < nearest.Id))
                { nearest = work; bestDistance = distance; }
            }
            return nearest;
        }
        private bool HarvestSource(WaveAttackOperations operations, string source)
        {
            if (!operations.OwnedHarvestSource) { return false; }
            foreach (var id in operations.HarvestSources)
            {
                if (id == source || id == "$seed" && modules.Is(source, "unit:remnant-create", "remnantKey", "grain-patch")) { return true; }
            }
            return false;
        }
        private bool programHasRemnant(string source) => modules.Has(source, "unit:remnant-create");
        private bool FrontEnemy(Position origin, Position facing, int range)
        {
            queries.Collect(frontTargets, origin, range);
            foreach (var enemy in frontTargets) { if (InArc(origin, facing, enemy.Position, range)) { return true; } }
            return false;
        }
        internal static bool Within(Position a, Position b, int radius) => a.DistanceSquared(b) <= (long)radius * radius;
        internal static bool InArc(Position origin, Position facing, Position point, int radius)
        {
            long x = point.X - origin.X, y = point.Y - origin.Y, dot = x * facing.X + y * facing.Y;
            return Within(origin, point, radius) && dot >= 0 && WaveGeometry.FanSquaredDotFactor * dot * dot >= (x * x + y * y) * ((long)facing.X * facing.X + (long)facing.Y * facing.Y);
        }
    }
}
