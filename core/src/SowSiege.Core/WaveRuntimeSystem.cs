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
            subscriptions = new(catalog, world, modules);
            enemies = new(catalog, world, random, interactive, modules, subscriptions);
            work = new(catalog, world, interactive, modules, subscriptions);
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
        public bool Suppressed(string id) => state.Evolutions.Select(e => definition.Evolutions[e]).Any(e => modules.Is(e.Id, "unit:evolution-replace", "replace", "both-tool-activations") && e.InputIds.Contains(id));
        public void Tick(Position previous)
        {
            state.Events.Clear();
            foreach (var expired in state.Attacks.Where(a => a.ExpireTick <= world.Tick).Select(a => a.ActivationId).Distinct().ToArray())
            {
                state.ResolveActivation(expired);
            }

            state.Attacks.RemoveAll(a => a.ExpireTick <= world.Tick);
            if (world.Lord != previous)
            {
                state.Facing = new(world.Lord.X - previous.X, world.Lord.Y - previous.Y);
            }

            enemies.Tick();
            foreach (var equipment in world.Equipment.ToArray())
            {
                if (equipment.ReadyTick > world.Tick || Suppressed(equipment.Id))
                {
                    continue;
                }

                var gear = AtLevel(definition.Gear[equipment.Id], equipment.Level);
                equipment.ReadyTick = world.Tick + gear.CooldownTicks;
                var evolution = state.Evolutions.Select(id => definition.Evolutions[id]).FirstOrDefault(e => e.InputIds[0] == gear.Id && !modules.Is(e.Id, "unit:evolution-replace", "replace", "both-tool-activations"));
                Attack(gear, evolution);
            }
            foreach (var e in state.Evolutions.Select(id => definition.Evolutions[id]).Where(e => modules.Is(e.Id, "unit:evolution-replace", "replace", "both-tool-activations")))
            {
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
            foreach (var enemy in world.Enemies.Where(e => e.Health <= 0).ToArray())
            {
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
        private static WaveGearDefinition AtLevel(WaveGearDefinition gear, int level)
        {
            if (gear.Levels is null || gear.Levels.Length == 0)
            {
                return gear;
            }

            var p = gear.Levels[Math.Min(level, gear.Levels.Length) - 1];
            return gear with
            {
                Damage = p.Damage,
                Range = p.Range,
                CooldownTicks = p.CooldownTicks,
                Speed = p.Speed,
                Count = p.Count,
                LifetimeTicks = p.LifetimeTicks,
                Knockback = p.Knockback
            }
;
        }
        private void Attack(WaveGearDefinition gear, WaveEvolutionDefinition? evolution)
        {
            var program = modules[evolution?.Id ?? gear.Id];
            var shape = program.Value("unit:attack-shape", "shape");
            if (program.Is("unit:attack-variant", "condition", "water-empty") && state.Water <= 0)
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
                var building = state.Work.Where(w => w.Kind == "building" && w.Health > 0 && !w.Complete && w.ReadyTick == -1).OrderBy(w => w.Position.DistanceSquared(world.Lord)).ThenBy(w => w.Id).FirstOrDefault();
                if (building != null)
                {
                    origin = building.Position;
                }
            }
            facing = subscriptions.Aim(gear, origin, facing);
            var source = evolution?.Id ?? gear.Id;
            var targets = world.Enemies.Where(e => e.Health > 0 && Within(e.Position, origin, gear.Range)).OrderBy(e => e.Position.DistanceSquared(origin)).ThenBy(e => e.Id).ToArray();
            var children = shape == "homing-projectile" ? Math.Min(targets.Length, gear.Count) : 0;
            var activation = state.BeginActivation(source, children, world.Tick + (shape == "orbit" ? gear.CooldownTicks : gear.LifetimeTicks));
            var hits = new List<EnemyState>();
            if (shape == "homing-projectile")
            {
                foreach (var target in targets.Take(gear.Count))
                {
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
                var visited = new HashSet<int>();
                for (int i = 0; i < gear.Count; i++)
                {
                    var next = world.Enemies.Where(e => e.Health > 0 && !visited.Contains(e.Id) && Within(e.Position, from, gear.Range)).OrderByDescending(e => i > 0 && program.Is("unit:attack-variant", "condition", "wet-target") && state.EnemyActions.TryGetValue(e.Id, out var a) && a.WetUntil > world.Tick).ThenBy(e => e.Position.DistanceSquared(from)).ThenBy(e => e.Id).FirstOrDefault();
                    if (next == null)
                    {
                        break;
                    }

                    visited.Add(next.Id);
                    state.Emit(world.Tick, "chain-link", source, next.Id, from, next.Position);
                    Hit(next, gear.Damage, source, origin, gear.Knockback, activation);
                    if (program.Is("unit:status-apply", "status", "brief-stop") && state.EnemyActions.TryGetValue(next.Id, out var action))
                    {
                        action.StopUntil = world.Tick + definition.StopTicks;
                    }

                    hits.Add(next);
                    from = next.Position;
                }
            }
            else if (shape == "orbit")
            {
                var phase = (world.Tick / Math.Max(1, gear.CooldownTicks)) % WaveGeometry.OrbitDirections;
                var dirs = new[]{
new Position(1,0),new Position(1,1),new Position(0,1),new Position(-1,1),new Position(-1,0),new Position(-1,-1),new Position(0,-1),new Position(1,-1)}
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
                foreach (var enemy in targets.Where(e => shape == "expanding-wave" || InArc(origin, facing, e.Position, gear.Range)))
                {
                    Hit(enemy, gear.Damage, source, origin, gear.Knockback, activation);
                    hits.Add(enemy);
                }
                state.Emit(world.Tick, "attack", source, -1, origin, new(origin.X + facing.X, origin.Y + facing.Y), gear.Range);
                if (program.Has("unit:harvest-contact"))
                {
                    foreach (var plot in state.Work.Where(w => w.Kind == "grain" && w.Complete && w.Health > 0 && HarvestSource(program, w.Source) && InArc(origin, facing, w.Position, gear.Range)).ToArray())
                    {
                        work.Harvest(plot);
                        foreach (var enemy in world.Enemies.Where(e => e.Health > 0 && Within(e.Position, plot.Position, gear.Range / WaveGeometry.MidpointDivisor)).OrderBy(e => e.Id).Take(gear.Count))
                        {
                            Hit(enemy, gear.Damage, source, plot.Position, 0, activation);
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
                enemy.Position = new(Math.Clamp(enemy.Position.X + Math.Sign(enemy.Position.X - origin.X) * knockback, 0, catalog.Tuning.World.Map.Width), Math.Clamp(enemy.Position.Y + Math.Sign(enemy.Position.Y - origin.Y) * knockback, 0, catalog.Tuning.World.Map.Height));
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
            var groups = state.Attacks.GroupBy(a => a.Source).ToArray();
            foreach (var group in groups)
            {
                var evolution = definition.Evolutions.TryGetValue(group.Key, out var evolved) ? evolved : null;
                var gear = definition.Gear[evolution?.InputIds[0] ?? group.Key];
                var level = world.Equipment.First(e => e.Id == gear.Id).Level;
                gear = AtLevel(gear, level);
                var anchor = world.Lord;
                if (evolution is not null && modules.Has(evolution.Id, "unit:attack-anchor"))
                {
                    var building = state.Work.Where(w => w.Kind == "building" && w.Health > 0 && !w.Complete && w.ReadyTick == -1).OrderBy(w => w.Position.DistanceSquared(world.Lord)).ThenBy(w => w.Id).FirstOrDefault();
                    if (building is not null)
                    {
                        anchor = building.Position;
                    }
                }
                var index = 0;
                var dirs = new[]{
new Position(1,0),new Position(1,1),new Position(0,1),new Position(-1,1),new Position(-1,0),new Position(-1,-1),new Position(0,-1),new Position(1,-1)}
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
                    foreach (var enemy in world.Enemies.Where(e => e.Health > 0 && Within(e.Position, point, next.Radius)))
                    {
                        if (state.Activations[old.ActivationId].HitTargets.Add(enemy.Id))
                        {
                            Hit(enemy, gear.Damage, group.Key, anchor, gear.Knockback, old.ActivationId);
                        }
                    }
                    index++;
                }
            }
        }
        private void TickProjectiles()
        {
            foreach (var p in state.Projectiles.Where(p => !p.Hostile).ToArray())
            {
                var target = world.Enemies.FirstOrDefault(e => e.Id == p.TargetId && e.Health > 0);
                if (target == null || p.ExpireTick <= world.Tick)
                {
                    state.Projectiles.Remove(p);
                    state.ResolveChild(p.ActivationId);
                    continue;
                }
                p.Previous = p.Position;
                p.Position = p.Position.MoveToward(target.Position, p.Speed);
                if (enemies.Intercepts(p.Previous, p.Position))
                {
                    state.Projectiles.Remove(p);
                    state.ResolveChild(p.ActivationId);
                    continue;
                }
                if (Within(p.Position, target.Position, Math.Max(1, p.Speed / WaveGeometry.MidpointDivisor)))
                {
                    Hit(target, p.Damage, p.Source, p.Previous, 0, p.ActivationId);
                    state.Projectiles.Remove(p);
                    state.ResolveChild(p.ActivationId);
                }
            }
        }
        private bool HarvestSource(WavePrimitiveProgram program, string source)
        {
            if (!program.Is("unit:harvest-contact", "sourceFilter", "owned-source")) { return false; }
            var ids = program.Value("unit:harvest-contact", "sourceIds").Split('|');
            return ids.Any(id => id == source || id == "$seed" && modules.Is(source, "unit:remnant-create", "remnantKey", "grain-patch"));
        }
        private bool programHasRemnant(string source) => modules.Has(source, "unit:remnant-create");
        private bool FrontEnemy(Position origin, Position facing, int range) => world.Enemies.Any(e => e.Health > 0 && InArc(origin, facing, e.Position, range));
        internal static bool Within(Position a, Position b, int radius) => a.DistanceSquared(b) <= (long)radius * radius;
        internal static bool InArc(Position origin, Position facing, Position point, int radius)
        {
            long x = point.X - origin.X, y = point.Y - origin.Y, dot = x * facing.X + y * facing.Y;
            return Within(origin, point, radius) && dot >= 0 && WaveGeometry.FanSquaredDotFactor * dot * dot >= (x * x + y * y) * ((long)facing.X * facing.X + (long)facing.Y * facing.Y);
        }
    }
}
