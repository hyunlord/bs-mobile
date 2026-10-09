using System;
using System.Linq;

namespace SowSiege.Core
{
    internal sealed class WaveEnemySystem
    {
        private readonly ContentCatalog catalog;
        private readonly WorldState world;
        private readonly TrackedRandom random;
        private readonly InteractiveState? interactive;
        private WaveRuntimeState State => world.WaveRuntime!;
        private WaveRuntimeDefinition Definition => catalog.WaveRuntime!;

        internal WaveEnemySystem(ContentCatalog catalog, WorldState world, TrackedRandom random, InteractiveState? interactive)
        { this.catalog = catalog; this.world = world; this.random = random; this.interactive = interactive; }

        internal void Tick()
        {
            Spawn();
            State.Detours.RemoveAll(d => d.UntilTick <= world.Tick);
            foreach (var enemy in world.Enemies.Where(e => e.Health > 0).OrderBy(e => e.Id))
            {
                if (!Definition.Enemies.TryGetValue(enemy.Definition, out var rule)) { continue; }
                if (!State.EnemyActions.TryGetValue(enemy.Id, out var action))
                { action = new WaveEnemyAction { Phase = "approach", Origin = enemy.Position, Target = world.Lord, TargetId = -1 }; State.EnemyActions.Add(enemy.Id, action); }
                if (action.StopUntil > world.Tick) { continue; }
                Advance(enemy, rule, action);
            }
            foreach (var shot in State.Projectiles.Where(p => p.Hostile).ToArray())
            {
                shot.Previous = shot.Position;
                shot.Position = shot.Position.MoveToward(shot.Direction, shot.Speed);
                var radius = catalog.Enemies[shot.Source].Range;
                if (OnSegment(world.Lord, shot.Previous, shot.Position, radius))
                { Hurt(shot.Source, shot.Damage); State.Projectiles.Remove(shot); }
                else if (shot.Position == shot.Direction || world.Tick >= shot.ExpireTick) { State.Projectiles.Remove(shot); }
            }
        }

        private void Spawn()
        {
            var cap = catalog.Tuning.World.Threat.EnemyCap;
            foreach (var rule in Definition.Enemies.Values.OrderBy(e => e.Id, StringComparer.Ordinal))
            {
                var first = rule.Kind == WaveEnemyKind.FloodBoss ? Definition.BossSpawnTick : rule.FirstSpawnTick;
                if (world.Tick < first || (rule.Kind == WaveEnemyKind.FloodBoss ? State.BossSpawned : (world.Tick - first) % catalog.Tuning.World.Threat.SpawnPeriodTicks != 0)) { continue; }
                if (world.Enemies.Count >= cap)
                {
                    if (rule.Kind != WaveEnemyKind.FloodBoss) { continue; }
                    var displaced = world.Enemies.OrderByDescending(e => e.Id).FirstOrDefault(e => Definition.Enemies[e.Definition].Kind != WaveEnemyKind.FloodBoss);
                    if (displaced is null) { continue; }
                    world.Enemies.Remove(displaced); State.EnemyActions.Remove(displaced.Id);
                }
                var map = catalog.Tuning.World.Map;
                var edge = random.Next(4);
                var position = edge switch { 0 => new Position(0, random.Next(map.Height)), 1 => new Position(map.Width, random.Next(map.Height)), 2 => new Position(random.Next(map.Width), 0), _ => new Position(random.Next(map.Width), map.Height) };
                var enemy = new EnemyState { Id = world.AllocateId(), Definition = rule.Id, Position = position, Health = catalog.Enemies[rule.Id].Health };
                world.Enemies.Add(enemy); world.SpawnedEnemies++;
                if (rule.Kind == WaveEnemyKind.FloodBoss) { State.BossSpawned = true; }
                State.Emit(world.Tick, "enemy-spawn", rule.Id, enemy.Id, position, position, 1);
            }
        }

        private void Advance(EnemyState enemy, WaveEnemyDefinition rule, WaveEnemyAction action)
        {
            var body = catalog.Enemies[enemy.Definition];
            var speed = action.WetUntil > world.Tick ? Math.Max(1, body.Speed / 2) : body.Speed;
            if (action.Phase == "recovery")
            {
                if (rule.Kind == WaveEnemyKind.Ranged) { enemy.Position = Clamp(enemy.Position.MoveToward(new Position(enemy.Position.X * 2 - world.Lord.X, enemy.Position.Y * 2 - world.Lord.Y), speed)); }
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
                if (rule.Kind == WaveEnemyKind.FloodBoss)
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
                if (action.Phase == "tell-charge") { action.Phase = "charge"; action.UntilTick = world.Tick + rule.ActiveTicks; action.Hit = false; if (rule.Kind == WaveEnemyKind.FloodBoss) { action.BossPhase = 0; } return; }
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
                    var crop = State.Work.FirstOrDefault(w => w.Id == action.TargetId && w.Health > 0 && w.Kind == "grain" && (rule.Kind == WaveEnemyKind.SeedThief ? !w.Complete : w.Complete));
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
                            crop.Health = 0; State.Emit(world.Tick, rule.Kind == WaveEnemyKind.SeedThief ? "seed-theft" : "ripe-consumed", rule.Id, crop.Id, crop.Position, crop.Position, 1);
                            if (rule.Kind == WaveEnemyKind.SeedThief)
                            {
                                var item = State.Items.Select(id => Definition.Items[id]).FirstOrDefault(i => i.Kind == WaveItemKind.SeedDetour && i.EquipmentIds.Contains(crop.Source));
                                if (item is not null)
                                {
                                    var gear = Definition.Gear[crop.Source];
                                    State.Detours.Add(new WaveDetour { Source = item.Id, Position = crop.Position, Radius = gear.WorkRadius, UntilTick = world.Tick + gear.WorkTicks });
                                    State.Emit(world.Tick, "seed-detour", item.Id, crop.Id, crop.Position, crop.Position, gear.WorkRadius);
                                }
                            }
                        }
                    }
                }
                else if (world.Lord.DistanceSquared(enemy.Position) <= (long)body.Range * body.Range) { Hurt(rule.Id, body.Damage); }
                Recover(rule, action); return;
            }
            if (rule.Kind == WaveEnemyKind.FloodBoss)
            {
                if (action.BossPhase < 2)
                {
                    var map = catalog.Tuning.World.Map;
                    // A finite lane through the center leaves both edge exits open.
                    Tell(enemy, action, rule, "water", new Position(map.Width * 3 / 4, map.Height * (action.BossPhase + 1) / 3), new Position(map.Width / 4, map.Height * (action.BossPhase + 1) / 3));
                }
                else { Tell(enemy, action, rule, "charge", world.Lord); }
                return;
            }
            if (rule.Kind == WaveEnemyKind.Charger) { Tell(enemy, action, rule, "charge", world.Lord); return; }
            if (rule.Kind == WaveEnemyKind.Shield)
            {
                var facing = new Position(action.Target.X - action.Origin.X, action.Target.Y - action.Origin.Y);
                var toward = new Position(world.Lord.X - enemy.Position.X, world.Lord.Y - enemy.Position.Y);
                if ((long)facing.X * toward.X + (long)facing.Y * toward.Y <= 0)
                { action.Phase = "turn"; action.UntilTick = world.Tick + rule.TellTicks; return; }
            }
            var target = world.Lord; action.TargetId = -1;
            var defender = State.Groups.Where(g => g.Health > 0 && g.Phase != "idle" && OnSegment(g.Position, enemy.Position, enemy.Position, body.Range)).OrderBy(g => g.Phase == "returning" ? 0 : 1).ThenBy(g => g.Id).FirstOrDefault();
            if (defender is not null && rule.Kind != WaveEnemyKind.Ranged)
            { action.TargetId = defender.Id; Tell(enemy, action, rule, "intercept", defender.Position); return; }
            if (rule.Kind == WaveEnemyKind.SeedThief || rule.Kind == WaveEnemyKind.RipeGrazer)
            {
                var crop = State.Work.Where(w => w.Kind == "grain" && w.Health > 0 && (rule.Kind == WaveEnemyKind.SeedThief ? !w.Complete : w.Complete)).OrderBy(w => w.Position.DistanceSquared(enemy.Position)).ThenBy(w => w.Id).FirstOrDefault();
                if (crop is not null) { target = crop.Position; action.TargetId = crop.Id; }
            }
            if (rule.Kind == WaveEnemyKind.Ranged && enemy.Position.DistanceSquared(world.Lord) <= (long)body.Range * body.Range * 16)
            { Tell(enemy, action, rule, "shot", world.Lord); return; }
            if (enemy.Position.DistanceSquared(target) <= (long)body.Range * body.Range) { Tell(enemy, action, rule, "strike", target); return; }
            if (action.TargetId < 0 && (rule.Kind == WaveEnemyKind.Pursuer || rule.Kind == WaveEnemyKind.SeedThief) && State.Paths.Count > 1)
            {
                var connected = State.Paths.Where(p => State.Paths.Any(other => other != p && other.DistanceSquared(p) <= 4L * Definition.PathSpacing * Definition.PathSpacing)).OrderBy(p => p.DistanceSquared(enemy.Position)).ToArray();
                if (connected.Length > 0)
                {
                    var path = connected[0];
                    if (path.DistanceSquared(enemy.Position) > (long)Definition.PathSpacing * Definition.PathSpacing && path.DistanceSquared(target) < enemy.Position.DistanceSquared(target)) { target = path; }
                }
            }
            if (rule.Kind == WaveEnemyKind.Pursuer || rule.Kind == WaveEnemyKind.SeedThief || rule.Kind == WaveEnemyKind.RipeGrazer)
            {
                var detour = State.Detours.FirstOrDefault(d => OnSegment(d.Position, enemy.Position, target, d.Radius));
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
            foreach (var enemy in world.Enemies.Where(e => e.Health > 0))
            {
                if (!Definition.Enemies.TryGetValue(enemy.Definition, out var rule) || rule.Kind != WaveEnemyKind.Shield || !State.EnemyActions.TryGetValue(enemy.Id, out var action) || action.Phase == "turn" || action.Phase == "recovery") { continue; }
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
