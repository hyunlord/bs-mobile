using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    internal sealed class HeroActiveState
    {
        internal HeroActiveOptions Options { get; }
        private readonly HeroDashParameters? dash;
        private readonly HeroTauntState? taunt;
        private HeroDashParameters Dash => dash ?? throw new InvalidOperationException("Dash is not enabled.");
        private int preparation, readyTick, startTick, endTick, directionX, directionY, distance;
        private long sequence, remainderX, remainderY;
        private bool active;
        internal bool SweptThisTick { get; private set; }
        internal Position PathStart { get; private set; }
        internal Position PathEnd { get; private set; }
        internal int ExistingEnemyIdLimit { get; private set; }
        internal HeroActiveState(ContentCatalog catalog, InteractiveOptions options)
        {
            HeroActiveCodec.ValidateBinding(options);
            Options = HeroActiveCodec.Freeze(options.HeroActive!);
            if (catalog.HeroActive is null || !HeroActiveCodec.Encode(catalog.HeroActive).SequenceEqual(HeroActiveCodec.Encode(Options)))
            { throw new ArgumentException("Issued active definitions differ from the authoritative catalog."); }
            var wave = catalog.WaveRuntime ?? throw new ArgumentException("Active combat requires a wave catalog.");
            dash = Options.Definitions.FirstOrDefault(d => d.Kind == HeroActiveKind.Dash)?.Dash;
            if (Options.Definitions.Any(d => d.Kind == HeroActiveKind.TauntShield)) { taunt = new(catalog, Options); }
            if (Options.StartingSlotId != catalog.Tuning.World.Progression.StartingWeapon || !catalog.Weapons.ContainsKey(Options.AttackDefinitionId) || !wave.Gear.ContainsKey(Options.AttackDefinitionId) || Options.AttackDefinitionId == catalog.Heroes[options.Run.HeroId].StartingTool)
            { throw new ArgumentException("Invalid starting weapon slot binding."); }
            foreach (var evolution in wave.Evolutions.Values)
            {
                if (Array.IndexOf(evolution.InputIds, Options.StartingSlotId) >= 0 && Options.AttackDefinitionId != Options.StartingSlotId)
                { throw new ArgumentException("Replacement must retain a resolvable starting-slot evolution lineage."); }
            }
            var field = options.WorldCombat!.Snapshot.Battlefield; var radius = Options.HeroRadius;
            if ((long)field.MaxX - field.MinX <= (long)radius + radius || (long)field.MaxY - field.MinY <= (long)radius + radius)
            { throw new ArgumentException("Hero body does not fit the battlefield."); }
            var center = new Position(catalog.Tuning.World.Map.Width >> 1, catalog.Tuning.World.Map.Height >> 1);
            if (center.X < field.MinX + radius || center.X > field.MaxX - radius || center.Y < field.MinY + radius || center.Y > field.MaxY - radius)
            { throw new ArgumentException("Hero starts outside collision bounds."); }
            foreach (var wall in Options.Walls)
            {
                if (wall.MinX < field.MinX || wall.MaxX > field.MaxX || wall.MinY < field.MinY || wall.MaxY > field.MaxY ||
                    center.X >= wall.MinX - radius && center.X <= wall.MaxX + radius && center.Y >= wall.MinY - radius && center.Y <= wall.MaxY + radius)
                { throw new ArgumentException("Invalid wall placement or occupied hero start."); }
            }
            foreach (var body in Options.EnemyBodies) { if (!catalog.Enemies.ContainsKey(body.EnemyId)) { throw new ArgumentException("Unknown active enemy body."); } }
        }
        internal HeroActiveFrame Frame() => new(preparation, readyTick, sequence, active, startTick, endTick, directionX, directionY, remainderX, remainderY, distance);
        internal bool Suppress(string id) => (active || SweptThisTick) && id == Options.AttackDefinitionId;
        private void ClearDash() { active = false; preparation = 0; remainderX = 0; remainderY = 0; }
        internal void Clear() { ClearDash(); taunt?.Clear(); }
        internal void NotifyHeroMoved(Position before, Position after) { if (before != after) { taunt?.Clear(); } }
        internal void AttachBattle(WorldCombatState battle) => taunt?.AttachBattle(battle);
        internal void RefreshMembership(WorldState world) => taunt?.RefreshMembership(world);
        internal void NotifyEnemyChanged(EnemyState enemy) => taunt?.NotifyEnemyChanged(enemy);
        internal bool TryApproach(int enemyId, out Position center, out int stopRadius)
        {
            center = default; stopRadius = 0;
            return taunt?.TryApproach(enemyId, out center, out stopRadius) == true;
        }
        internal int ReduceDamage(int damage, Position hero, Position? origin, out int prevented)
        {
            prevented = 0; return taunt is null ? damage : taunt.ReduceDamage(damage, hero, origin, out prevented);
        }
        internal HeroActiveEffectsFrame EffectsFrame() => taunt?.Capture(sequence) ?? new(sequence,
            Array.Empty<HeroActiveCooldownFrame>(), Array.Empty<HeroTauntInstanceFrame>());
        internal void BeginTick(WorldState world)
        {
            taunt?.BeginTick(world.Tick);
            SweptThisTick = false; PathStart = PathEnd = world.Lord; ExistingEnemyIdLimit = -1;
            foreach (var enemy in world.Enemies) { ExistingEnemyIdLimit = Math.Max(ExistingEnemyIdLimit, enemy.Id); }
            if (world.LordHealth <= 0) { Clear(); }
        }
        internal bool MoveDash(WorldState world, PlayerInput input, WorldBattlefield field)
        {
            if (!active) { return false; }
            if (input.X == 0 && input.Y == 0 || world.Tick >= endTick || distance >= Dash.MaximumDistance || world.LordHealth <= 0)
            { ClearDash(); return false; }
            if (world.Tick < startTick) { return false; }
            var amount = Math.Min(Dash.SpeedPerTick, Dash.MaximumDistance - distance);
            var scale = Math.Max(Math.Abs(directionX), Math.Abs(directionY));
            var nx = checked((long)directionX * amount + remainderX); var ny = checked((long)directionY * amount + remainderY);
            var deltaX = (int)(nx / scale); var deltaY = (int)(ny / scale); remainderX = nx % scale; remainderY = ny % scale;
            var origin = world.Lord; var proposed = new Position(checked(origin.X + deltaX), checked(origin.Y + deltaY));
            var end = Constrain(origin, proposed, field, world, true, out var blocked);
            SweptThisTick = true; PathStart = origin; PathEnd = end;
            world.Lord = end; world.Destination = end;
            distance = checked(distance + Math.Max(Math.Abs(end.X - origin.X), Math.Abs(end.Y - origin.Y)));
            if (blocked || distance >= Dash.MaximumDistance || world.Tick + 1 >= endTick) { active = false; preparation = 0; remainderX = remainderY = 0; }
            return true;
        }
        internal Position Constrain(Position origin, Position proposed, WorldBattlefield field, WorldState world, bool enemyBodies, out bool blocked)
        {
            var radius = Options.HeroRadius;
            blocked = HeroActiveGeometry.TryExitBox(origin, proposed, field.MinX + radius, field.MinY + radius, field.MaxX - radius, field.MaxY - radius, out var first);
            foreach (var wall in Options.Walls)
            {
                if (HeroActiveGeometry.TryHitBox(origin, proposed, wall.MinX - radius, wall.MinY - radius, wall.MaxX + radius, wall.MaxY + radius, out var hit) && (!blocked || hit.CompareTo(first) < 0))
                { first = hit; blocked = true; }
            }
            if (enemyBodies)
            {
                foreach (var enemy in world.Enemies)
                {
                    if (enemy.Health <= 0) { continue; }
                    var body = Body(enemy.Definition);
                    if (HeroActiveGeometry.TryHitCircle(origin, proposed, enemy.Position, checked(radius + body.Radius), out var hit) && (!blocked || hit.CompareTo(first) < 0))
                    { first = hit; blocked = true; }
                }
            }
            if (!blocked) { return proposed; }
            var end = HeroActiveGeometry.BeforeHit(origin, proposed, first);
            foreach (var wall in Options.Walls)
            {
                if (HeroActiveGeometry.TryHitBox(origin, end, wall.MinX - radius, wall.MinY - radius, wall.MaxX + radius, wall.MaxY + radius, out var check) && check.Numerator < check.Denominator)
                { return origin; }
            }
            if (enemyBodies)
            {
                foreach (var enemy in world.Enemies)
                {
                    if (enemy.Health > 0 && HeroActiveGeometry.TryHitCircle(origin, end, enemy.Position, checked(radius + Body(enemy.Definition).Radius), out _)) { return origin; }
                }
            }
            return end;
        }
        internal void AfterMovement(WorldState world, Position origin, PlayerInput input, bool movementBlocked)
        {
            if (world.LordHealth <= 0) { Clear(); return; }
            taunt?.AfterMovement(world, origin, ref sequence);
            if (dash is null) { return; }
            if (active || SweptThisTick || world.Tick < readyTick) { preparation = 0; return; }
            if (movementBlocked || world.Lord == origin || input.X == 0 && input.Y == 0) { preparation = 0; return; }
            if (preparation < Dash.PreparationTicks) { preparation++; }
            if (preparation != Dash.PreparationTicks) { return; }
            var range = (long)Dash.ThreatRange * Dash.ThreatRange; var eligible = false;
            foreach (var enemy in world.Enemies)
            {
                if (enemy.Health > 0 && Body(enemy.Definition).EligibleTarget && enemy.Position.DistanceSquared(world.Lord) <= range) { eligible = true; break; }
            }
            if (!eligible) { return; }
            sequence = checked(sequence + 1); active = true; preparation = 0; directionX = input.X; directionY = input.Y;
            remainderX = remainderY = 0; distance = 0; startTick = checked(world.Tick + 1); endTick = checked(startTick + Dash.DurationTicks); readyTick = checked(world.Tick + Dash.CooldownTicks);
        }
        internal HeroActiveEnemyBody Body(string id)
        {
            foreach (var body in Options.EnemyBodies) { if (body.EnemyId == id) { return body; } }
            throw new InvalidOperationException("Encounter enemy has no issued collision body.");
        }
        internal bool CrossesEnemyAttack(EnemyState enemy, int range)
            => SweptThisTick && enemy.Id <= ExistingEnemyIdLimit && HeroActiveGeometry.IntersectsCircle(PathStart, PathEnd, enemy.Position, range);
        internal byte[] Encode()
        {
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8, true);
            w.Write(taunt is null ? "hero-active-runtime-v1" : "hero-active-runtime-v2"); w.Write(taunt is null ? 1 : HeroActiveCodec.EnvelopeSchema); w.Write(preparation); w.Write(readyTick); w.Write(sequence); w.Write(active);
            w.Write(startTick); w.Write(endTick); w.Write(directionX); w.Write(directionY); w.Write(remainderX); w.Write(remainderY); w.Write(distance);
            taunt?.Write(w, sequence);
            w.Flush(); return stream.ToArray();
        }
    }
}
