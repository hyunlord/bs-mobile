using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    internal sealed class WaveEnemySystem
    {
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly TrackedRandom random;
        private readonly InteractiveState? interactive;
        private readonly WavePrimitiveModules modules;
        private readonly WaveItemSubscriptions subscriptions;
        private readonly List<EnemyState> liveEnemies = new();
        private readonly WaveEnemyQueries queries;
        private readonly bool ownsQueries;
        private readonly List<EnemyState> interceptCandidates = new();
        private readonly int interceptRange;
        private readonly List<Position> connectedPaths = new();
        private readonly List<WaveEnemyDefinition> spawnRules = new();
        private static readonly Comparison<WaveEnemyDefinition> SpawnOrder = (a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id);
        private static readonly Comparison<EnemyState> EnemyOrder = (a, b) => a.Id.CompareTo(b.Id);
        private WaveRuntimeState State => world.WaveRuntime!;
        private WaveRuntimeDefinition Definition => catalog.WaveRuntime!;

        internal WaveEnemySystem(ContentCatalog catalog, WorldState world, TrackedRandom random, InteractiveState? interactive, WavePrimitiveModules? modules = null, WaveItemSubscriptions? subscriptions = null, WaveEnemyQueries? queries = null)
        { this.catalog = catalog; this.world = world; this.random = random; this.interactive = interactive; this.modules = modules ?? new(catalog.WaveRuntime!); this.subscriptions = subscriptions ?? new(catalog, world, this.modules); ownsQueries = queries is null; this.queries = queries ?? new(world, catalog.Tuning.World.Farms.Spacing); foreach (var enemy in catalog.Enemies.Values) { interceptRange = Math.Max(interceptRange, enemy.Range); } }

        internal void Tick()
        {
            Spawn();
            var pathsCollected = false;
            State.Detours.RemoveAll(d => d.UntilTick <= world.Tick);
            liveEnemies.Clear();
            foreach (var enemy in world.Enemies) { if (enemy.Health > 0) { liveEnemies.Add(enemy); } }
            liveEnemies.Sort(EnemyOrder);
            foreach (var enemy in liveEnemies)
            {
                if (!Definition.Enemies.TryGetValue(enemy.Definition, out var rule)) { continue; }
                if (!State.EnemyActions.TryGetValue(enemy.Id, out var action))
                { action = new WaveEnemyAction { Phase = "approach", Origin = enemy.Position, Target = world.Lord, TargetId = -1 }; State.EnemyActions.Add(enemy.Id, action); }
                if (action.StopUntil > world.Tick) { continue; }
                Advance(enemy, rule, action, ref pathsCollected);
            }
            for (var index = 0; index < State.Projectiles.Count;)
            {
                var shot = State.Projectiles[index];
                if (!shot.Hostile) { index++; continue; }
                shot.Previous = shot.Position;
                shot.Position = shot.Position.MoveToward(shot.Direction, shot.Speed);
                var radius = catalog.Enemies[shot.Source].Range;
                if (OnSegment(world.Lord, shot.Previous, shot.Position, radius))
                { Hurt(shot.Source, shot.Damage); State.Projectiles.RemoveAt(index); }
                else if (shot.Position == shot.Direction || world.Tick >= shot.ExpireTick) { State.Projectiles.RemoveAt(index); }
                else { index++; }
            }
        }

        private void Spawn()
        {
            var cap = catalog.Tuning.World.Threat.EnemyCap;
            spawnRules.Clear();
            foreach (var rule in Definition.Enemies.Values) { spawnRules.Add(rule); }
            spawnRules.Sort(SpawnOrder);
            foreach (var rule in spawnRules)
            {
                var operations = modules.Enemy(rule.Id);
                var first = operations.Boss ? Definition.BossSpawnTick : rule.FirstSpawnTick;
                if (world.Tick < first || (operations.Boss ? State.BossSpawned : (world.Tick - first) % catalog.Tuning.World.Threat.SpawnPeriodTicks != 0)) { continue; }
                if (world.Enemies.Count >= cap)
                {
                    if (!operations.Boss) { continue; }
                    EnemyState? displaced = null;
                    foreach (var candidate in world.Enemies)
                    { if (!modules.Enemy(candidate.Definition).Boss && (displaced is null || candidate.Id > displaced.Id)) { displaced = candidate; } }
                    if (displaced is null) { continue; }
                    world.Enemies.Remove(displaced); State.EnemyActions.Remove(displaced.Id);
                }
                var map = catalog.Tuning.World.Map;
                var edge = random.Next(WaveGeometry.RectangleSides);
                var position = edge switch { 0 => new Position(0, random.Next(map.Height)), 1 => new Position(map.Width, random.Next(map.Height)), WaveGeometry.TopEdge => new Position(random.Next(map.Width), 0), _ => new Position(random.Next(map.Width), map.Height) };
                var enemy = new EnemyState { Id = world.AllocateId(), Definition = rule.Id, Position = position, Health = catalog.Enemies[rule.Id].Health };
                world.Enemies.Add(enemy); world.SpawnedEnemies++;
                if (operations.Boss) { State.BossSpawned = true; }
                State.Emit(world.Tick, "enemy-spawn", rule.Id, enemy.Id, position, position, 1);
            }
        }

        private void Advance(EnemyState enemy, WaveEnemyDefinition rule, WaveEnemyAction action, ref bool pathsCollected)
        {
            var body = catalog.Enemies[enemy.Definition];
            var operations = modules.Enemy(rule.Id);
            var speed = action.WetUntil > world.Tick ? Math.Max(1, body.Speed / Definition.Behavior.WetSpeedDivisor) : body.Speed;
            if (action.Phase == "recovery")
            {
                if (operations.Movement == "standoff") { enemy.Position = Clamp(enemy.Position.MoveToward(new Position(enemy.Position.X * WaveGeometry.MidpointDivisor - world.Lord.X, enemy.Position.Y * WaveGeometry.MidpointDivisor - world.Lord.Y), speed)); }
                if (world.Tick < action.UntilTick) { return; }
                action.Phase = "approach";
            }
            if (action.Phase == "turn")
            {
                if (world.Tick < action.UntilTick) { return; }
                action.Target = world.Lord; action.Origin = enemy.Position; action.Phase = "approach";
            }
            if (action.Phase == "charge")
            {
                var previous = enemy.Position;
                enemy.Position = enemy.Position.MoveToward(action.Target, rule.ProjectileSpeed);
                if (operations.Boss)
                {
                    foreach (var building in State.Work.Where(w => w.Kind == "building" && w.Health > 0 && OnSegment(w.Position, previous, enemy.Position, body.Range)))
                    { if (State.Completed.Add("charge-building:" + enemy.Id + ":" + action.UntilTick + ":" + building.Id)) { DamageBuilding(building, rule.Id, body.Damage); } }
                }
                if (!action.Hit && OnSegment(world.Lord, previous, enemy.Position, body.Range)) { Hurt(rule.Id, body.Damage); action.Hit = true; }
                if (enemy.Position == action.Target || world.Tick >= action.UntilTick) { Recover(rule, action); }
                return;
            }
            if (action.Phase == "water")
            {
                if (!action.Hit && OnSegment(world.Lord, action.Origin, action.Target, body.Range)) { Hurt(rule.Id, body.Damage); action.Hit = true; }
                foreach (var work in State.Work.Where(w => w.Kind == "grain" && !w.Complete && w.Health > 0 && w.DormantUntil < action.UntilTick && OnSegment(w.Position, action.Origin, action.Target, body.Range)))
                { work.DormantUntil = action.UntilTick; State.Emit(world.Tick, "growth-dormant", rule.Id, work.Id, work.Position, work.Position, action.UntilTick - world.Tick); }
                if (world.Tick >= action.UntilTick) { action.BossPhase++; Recover(rule, action); }
                return;
            }
            if (action.Phase.StartsWith("tell-", StringComparison.Ordinal))
            {
                if (world.Tick < action.UntilTick) { return; }
                if (action.Phase == "tell-charge") { action.Phase = "charge"; action.UntilTick = world.Tick + rule.ActiveTicks; action.Hit = false; if (operations.Boss) { action.BossPhase = 0; } return; }
                if (action.Phase == "tell-water") { action.Phase = "water"; action.UntilTick = world.Tick + rule.ActiveTicks; action.Hit = false; return; }
                if (action.Phase == "tell-shot")
                {
                    State.Projectiles.Add(new WaveProjectile { Id = world.AllocateId(), Source = rule.Id, Position = enemy.Position, Previous = enemy.Position, Direction = action.Target, Damage = body.Damage, Speed = rule.ProjectileSpeed, ExpireTick = world.Tick + rule.ActiveTicks, Hostile = true });
                }
                else if (action.Phase == "tell-intercept")
                {
                    var group = State.Groups.FirstOrDefault(g => g.Id == action.TargetId && g.Health > 0 && g.Phase != "idle");
                    if (group is not null && OnSegment(group.Position, enemy.Position, enemy.Position, body.Range))
                    {
                        var cover = group.Phase == "returning" ? CoverFor(enemy, group, body.Range) : null;
                        var recipient = cover ?? group;
                        var dealt = Math.Min(recipient.Health, body.Damage); recipient.Health -= dealt;
                        State.Emit(world.Tick, "group-hit", rule.Id, recipient.Id, recipient.Position, recipient.Position, dealt);
                        if (cover is not null) { State.Emit(world.Tick, "group-cover-intercept", cover.Source, cover.Id, cover.Position, group.Position, dealt); }
                    }
                }
                else if (action.TargetId >= 0)
                {
                    var crop = State.Work.FirstOrDefault(w => w.Id == action.TargetId && w.Health > 0 && w.Kind == "grain" && (operations.Action == "consume-seed" ? !w.Complete : w.Complete));
                    if (crop is not null && crop.Position.DistanceSquared(enemy.Position) <= (long)body.Range * body.Range)
                    {
                        if (crop.Protected && crop.ParentId >= 0)
                        {
                            State.Completed.Add("roof-used:" + crop.ParentId);
                            foreach (var sibling in State.Work.Where(w => w.ParentId == crop.ParentId)) { sibling.Protected = false; }
                            var roof = State.Work.FirstOrDefault(w => w.Id == crop.ParentId);
                            if (roof is not null) { DamageBuilding(roof, rule.Id, body.Damage); }
                            State.Emit(world.Tick, "roof-block", rule.Id, crop.Id, crop.Position, crop.Position, 1);
                        }
                        else
                        {
                            crop.Health = 0; State.Emit(world.Tick, operations.Action == "consume-seed" ? "seed-theft" : "ripe-consumed", rule.Id, crop.Id, crop.Position, crop.Position, 1);
                            if (operations.Action == "consume-seed")
                            {
                                subscriptions.SeedEaten(crop);
                            }
                        }
                    }
                }
                else if (world.Lord.DistanceSquared(enemy.Position) <= (long)body.Range * body.Range) { Hurt(rule.Id, body.Damage); }
                Recover(rule, action); return;
            }
            if (operations.Boss)
            {
                if (action.BossPhase < WaveGeometry.WaterLaneCount)
                {
                    var map = catalog.Tuning.World.Map;
                    // A finite lane through the center leaves both edge exits open.
                    Tell(enemy, action, rule, "water", new Position(map.Width * WaveGeometry.FarLaneQuarter / WaveGeometry.RectangleSides, map.Height * (action.BossPhase + 1) / WaveGeometry.LanePartitions), new Position(map.Width / WaveGeometry.RectangleSides, map.Height * (action.BossPhase + 1) / WaveGeometry.LanePartitions));
                }
                else { Tell(enemy, action, rule, "charge", world.Lord); }
                return;
            }
            if (operations.Movement == "charge-locked-line") { Tell(enemy, action, rule, "charge", world.Lord); return; }
            if (operations.Movement == "hold-front")
            {
                var facing = new Position(action.Target.X - action.Origin.X, action.Target.Y - action.Origin.Y);
                var toward = new Position(world.Lord.X - enemy.Position.X, world.Lord.Y - enemy.Position.Y);
                if ((long)facing.X * toward.X + (long)facing.Y * toward.Y <= 0)
                { action.Phase = "turn"; action.UntilTick = world.Tick + rule.TellTicks; return; }
            }
            var target = world.Lord; action.TargetId = -1;
            WaveGroup? defender = null;
            foreach (var candidate in State.Groups)
            {
                if (candidate.Health <= 0 || candidate.Phase == "idle" || !OnSegment(candidate.Position, enemy.Position, enemy.Position, body.Range)) { continue; }
                var priority = candidate.Phase == "returning" ? 0 : 1;
                var bestPriority = defender?.Phase == "returning" ? 0 : 1;
                if (defender is null || priority < bestPriority || priority == bestPriority && candidate.Id < defender.Id) { defender = candidate; }
            }
            if (defender is not null && operations.Movement != "standoff")
            { action.TargetId = defender.Id; Tell(enemy, action, rule, "intercept", defender.Position); return; }
            if (operations.Action == "consume-seed" || operations.Action == "consume-ripe")
            {
                WaveWork? crop = null;
                var bestDistance = long.MaxValue;
                foreach (var candidate in State.Work)
                {
                    if (candidate.Kind != "grain" || candidate.Health <= 0 || (operations.Action == "consume-seed" ? candidate.Complete : !candidate.Complete)) { continue; }
                    var distance = candidate.Position.DistanceSquared(enemy.Position);
                    if (distance < bestDistance || distance == bestDistance && (crop is null || candidate.Id < crop.Id))
                    { crop = candidate; bestDistance = distance; }
                }
                if (crop is not null) { target = crop.Position; action.TargetId = crop.Id; }
            }
            if (operations.Movement == "standoff")
            {
                if (enemy.Position.DistanceSquared(world.Lord) > (long)body.Range * body.Range * Definition.Behavior.RangedRangeMultiplier * Definition.Behavior.RangedRangeMultiplier)
                { enemy.Position = enemy.Position.MoveToward(world.Lord, speed); }
                else if (State.Work.Any(w => w.Kind == "grain" && w.Health > 0 && w.Complete
                    && OnSegment(world.Lord, w.Position, w.Position, catalog.Tuning.World.Farms.Spacing)))
                { Tell(enemy, action, rule, "shot", world.Lord); }
                return;
            }
            if (enemy.Position.DistanceSquared(target) <= (long)body.Range * body.Range) { Tell(enemy, action, rule, "strike", target); return; }
            if (action.TargetId < 0 && ((operations.Target == "lord" && operations.Movement == "pursue") || operations.Action == "consume-seed") && State.Paths.Count > 1)
            {
                // Paths change only in the work phase after all enemies advance.
                if (!pathsCollected)
                {
                    connectedPaths.Clear();
                    var connectionRange = (long)Definition.Behavior.PathConnectionMultiplier * Definition.Behavior.PathConnectionMultiplier * Definition.PathSpacing * Definition.PathSpacing;
                    foreach (var path in State.Paths)
                    {
                        foreach (var other in State.Paths)
                        {
                            if (other != path && other.DistanceSquared(path) <= connectionRange) { connectedPaths.Add(path); break; }
                        }
                    }
                    pathsCollected = true;
                }
                if (connectedPaths.Count > 0)
                {
                    var path = connectedPaths[0];
                    var distance = path.DistanceSquared(enemy.Position);
                    for (var i = 1; i < connectedPaths.Count; i++)
                    {
                        var candidateDistance = connectedPaths[i].DistanceSquared(enemy.Position);
                        if (candidateDistance < distance) { path = connectedPaths[i]; distance = candidateDistance; }
                    }
                    if (distance > (long)Definition.PathSpacing * Definition.PathSpacing && path.DistanceSquared(target) < enemy.Position.DistanceSquared(target)) { target = path; }
                }
            }
            if ((operations.Target == "lord" && operations.Movement == "pursue") || operations.Action == "consume-seed" || operations.Action == "consume-ripe")
            {
                WaveDetour? detour = null;
                foreach (var candidate in State.Detours)
                {
                    if (OnSegment(candidate.Position, enemy.Position, target, candidate.Radius)) { detour = candidate; break; }
                }
                if (detour is not null)
                {
                    var side = enemy.Position.Y <= detour.Position.Y ? -1 : 1;
                    target = Clamp(new Position(detour.Position.X + Math.Sign(target.X - enemy.Position.X) * (detour.Radius + speed), detour.Position.Y + side * (detour.Radius + speed)));
                }
            }
            enemy.Position = enemy.Position.MoveToward(target, speed);
        }

        private WaveGroup? CoverFor(EnemyState enemy, WaveGroup returning, int range)
        {
            var dx = (long)returning.Position.X - enemy.Position.X; var dy = (long)returning.Position.Y - enemy.Position.Y;
            var length = dx * dx + dy * dy;
            return State.Groups.Where(g => g.Id != returning.Id && g.Health > 0 && g.Training > 0 && g.Formation == "cover" && g.Phase != "idle" && g.Phase != "returning")
                .Where(g =>
                {
                    var dot = ((long)g.Position.X - enemy.Position.X) * dx + ((long)g.Position.Y - enemy.Position.Y) * dy;
                    return dot > 0 && dot < length && OnSegment(g.Position, enemy.Position, returning.Position, range)
                        && OnSegment(g.Position, enemy.Position, enemy.Position, range);
                }).OrderBy(g => g.Position.DistanceSquared(enemy.Position)).ThenBy(g => g.Id).FirstOrDefault();
        }

        private void DamageBuilding(WaveWork building, string source, int damage)
        {
            var dealt = Math.Min(building.Health, damage); building.Health -= dealt;
            building.Complete = false; building.Progress = 0; building.ReadyTick = -1;
            foreach (var crop in State.Work.Where(w => w.ParentId == building.Id)) { crop.Protected = false; }
            State.Emit(world.Tick, "building-hit", source, building.Id, building.Position, building.Position, dealt);
        }

        private void Tell(EnemyState enemy, WaveEnemyAction action, WaveEnemyDefinition rule, string kind, Position target, Position? origin = null)
        { action.Phase = "tell-" + kind; action.Origin = origin ?? enemy.Position; action.Target = target; action.UntilTick = world.Tick + rule.TellTicks; State.Emit(world.Tick, "enemy-tell", rule.Id, enemy.Id, action.Origin, target, rule.TellTicks); }
        private void Recover(WaveEnemyDefinition rule, WaveEnemyAction action)
        { action.Phase = "recovery"; action.UntilTick = world.Tick + rule.RecoveryTicks; }
        private Position Clamp(Position p) => new(Math.Clamp(p.X, 0, catalog.Tuning.World.Map.Width), Math.Clamp(p.Y, 0, catalog.Tuning.World.Map.Height));
        private void Hurt(string source, int damage)
        {
            if (interactive?.Invulnerable == true) { return; }
            var dealt = Math.Min(world.LordHealth, damage); world.LordHealth -= dealt;
            if (world.LordHealth == 0) { world.DeathCause = source; }
            State.Emit(world.Tick, "lord-hit", source, 0, world.Lord, world.Lord, dealt);
            interactive?.Experience(world.Tick, PresentationKind.LordHit, source, world.Lord, dealt);
        }
        internal bool Intercepts(Position from, Position to)
        {
            if (ownsQueries) { queries.Rebuild(); }
            var center = new Position(from.X + (to.X - from.X) / WaveGeometry.MidpointDivisor, from.Y + (to.Y - from.Y) / WaveGeometry.MidpointDivisor);
            var radius = (Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) + WaveGeometry.MidpointDivisor) / WaveGeometry.MidpointDivisor + interceptRange;
            queries.Collect(interceptCandidates, center, radius);
            foreach (var enemy in interceptCandidates)
            {
                if (!Definition.Enemies.TryGetValue(enemy.Definition, out var rule) || modules.Enemy(rule.Id).Movement != "hold-front" || !State.EnemyActions.TryGetValue(enemy.Id, out var action) || action.Phase == "turn" || action.Phase == "recovery") { continue; }
                var dx = action.Target.X - action.Origin.X; var dy = action.Target.Y - action.Origin.Y;
                if ((long)(from.X - enemy.Position.X) * dx + (long)(from.Y - enemy.Position.Y) * dy > 0 && OnSegment(enemy.Position, from, to, catalog.Enemies[enemy.Definition].Range)) { return true; }
            }
            return false;
        }
        internal static bool OnSegment(Position point, Position from, Position to, int radius)
        {
            var dx = (long)to.X - from.X; var dy = (long)to.Y - from.Y;
            var px = (long)point.X - from.X; var py = (long)point.Y - from.Y;
            var length = dx * dx + dy * dy; var dot = px * dx + py * dy;
            if (length == 0 || dot <= 0) { return from.DistanceSquared(point) <= (long)radius * radius; }
            if (dot >= length) { return to.DistanceSquared(point) <= (long)radius * radius; }
            var cross = px * dy - py * dx;
            return (decimal)cross * cross <= (decimal)radius * radius * length;
        }
    }
}
