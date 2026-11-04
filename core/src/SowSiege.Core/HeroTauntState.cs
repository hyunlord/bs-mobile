using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SowSiege.Core
{
    internal sealed class HeroTauntState
    {
        private sealed class Instance
        {
            internal bool Used, Charged;
            internal int Definition, Start, End, Count;
            internal long Sequence;
            internal Position Center, Direction;
            internal readonly int[] Claims;
            internal Instance(int capacity) { Claims = new int[capacity]; }
        }
        private readonly HeroActiveOptions options;
        private readonly HeroActiveDefinition[] definitions;
        private readonly int[] preparation, ready;
        private readonly Instance[] instances;
        private readonly HashSet<string> excluded = new(StringComparer.Ordinal);
        private WorldCombatState? battle;
        internal void AttachBattle(WorldCombatState value) { battle = value; }
        private int count;
        internal HeroTauntState(ContentCatalog catalog, HeroActiveOptions options)
        {
            this.options = options;
            definitions = options.Definitions.Where(d => d.Taunt is not null).ToArray();
            preparation = new int[definitions.Length]; ready = new int[definitions.Length];
            var capacity = definitions[0].Taunt!.MaximumInstances; var targets = 0;
            var modules = new WavePrimitiveModules(catalog.WaveRuntime!);
            foreach (var definition in definitions)
            {
                var p = definition.Taunt!; targets = Math.Max(targets, p.MaximumTargets);
                foreach (var policy in p.EnemyPolicies)
                {
                    if (!catalog.Enemies.ContainsKey(policy.EnemyId) || !catalog.WaveRuntime!.Enemies.ContainsKey(policy.EnemyId))
                    { throw new ArgumentException("Unknown taunt enemy policy."); }
                    if (modules.Enemy(policy.EnemyId).Boss) { excluded.Add(policy.EnemyId); }
                }
            }
            // Validate has already checked the aggregate product, before any runtime arrays.
            instances = new Instance[capacity];
            for (var i = 0; i < capacity; i++) { instances[i] = new(targets); }
        }
        internal void Clear()
        {
            Array.Clear(preparation, 0, preparation.Length); count = 0;
            foreach (var instance in instances) { instance.Used = instance.Charged = false; instance.Count = 0; }
        }
        internal void BeginTick(int tick)
        {
            foreach (var instance in instances)
            { if (instance.Used && tick >= instance.End) { instance.Used = instance.Charged = false; instance.Count = 0; count--; } }
        }
        internal void AfterMovement(WorldState world, Position before, ref long sequence)
        {
            if (world.Lord != before) { Clear(); return; }
            for (var i = 0; i < definitions.Length; i++)
            {
                var p = definitions[i].Taunt!;
                if (world.Tick < ready[i]) { preparation[i] = 0; continue; }
                if (preparation[i] < p.PreparationTicks) { preparation[i]++; }
                if (preparation[i] < p.PreparationTicks || count >= instances.Length) { continue; }
                var nearest = Nearest(world, i, world.Lord, null);
                if (nearest is null) { continue; }
                Instance? slot = null;
                foreach (var instance in instances) { if (!instance.Used) { slot = instance; break; } }
                if (slot is null) { throw new InvalidOperationException("Active instance accounting mismatch."); }
                slot.Used = slot.Charged = true; slot.Definition = i; slot.Start = world.Tick;
                slot.End = checked(world.Tick + p.DurationTicks); slot.Sequence = sequence = checked(sequence + 1);
                slot.Center = world.Lord; slot.Direction = new(nearest.Position.X - world.Lord.X, nearest.Position.Y - world.Lord.Y);
                if (slot.Direction == new Position(0, 0)) { slot.Direction = world.WaveRuntime!.Facing; }
                slot.Count = 0; count++; preparation[i] = 0; ready[i] = checked(world.Tick + p.CooldownTicks);
            }
        }
        private bool Eligible(EnemyState enemy, int definition)
        {
            if (enemy.Health <= 0 || excluded.Contains(enemy.Definition) || battle?.CanTauntEnemy(enemy.Id) != true) { return false; }
            foreach (var policy in definitions[definition].Taunt!.EnemyPolicies)
            { if (policy.EnemyId == enemy.Definition) { return policy.Eligible; } }
            return false;
        }
        private bool Valid(Instance instance, EnemyState enemy)
        {
            var radius = definitions[instance.Definition].Taunt!.Radius;
            return Eligible(enemy, instance.Definition) && enemy.Position.DistanceSquared(instance.Center) <= (long)radius * radius;
        }
        private static bool Contains(Instance instance, int id)
        { for (var i = 0; i < instance.Count; i++) { if (instance.Claims[i] == id) { return true; } } return false; }
        private EnemyState? Nearest(WorldState world, int definition, Position center, Instance? retained)
        {
            EnemyState? nearest = null; var best = long.MaxValue; var radius = definitions[definition].Taunt!.Radius;
            foreach (var enemy in world.Enemies)
            {
                if (!Eligible(enemy, definition) || retained is not null && Contains(retained, enemy.Id)) { continue; }
                var distance = enemy.Position.DistanceSquared(center);
                if (distance > (long)radius * radius || distance > best || distance == best && nearest is not null && enemy.Id > nearest.Id) { continue; }
                nearest = enemy; best = distance;
            }
            return nearest;
        }
        internal void RefreshMembership(WorldState world)
        {
            foreach (var enemy in world.Enemies) { NotifyEnemyChanged(enemy); }
            foreach (var instance in instances)
            {
                if (!instance.Used) { continue; }
                var maximum = definitions[instance.Definition].Taunt!.MaximumTargets;
                while (instance.Count < maximum)
                {
                    var enemy = Nearest(world, instance.Definition, instance.Center, instance); if (enemy is null) { break; }
                    var index = instance.Count++;
                    while (index > 0 && instance.Claims[index - 1] > enemy.Id) { instance.Claims[index] = instance.Claims[index - 1]; index--; }
                    instance.Claims[index] = enemy.Id;
                }
            }
        }
        internal void NotifyEnemyChanged(EnemyState enemy)
        {
            foreach (var instance in instances)
            {
                if (!instance.Used || Valid(instance, enemy)) { continue; }
                for (var i = 0; i < instance.Count; i++)
                {
                    if (instance.Claims[i] != enemy.Id) { continue; }
                    for (var j = i + 1; j < instance.Count; j++) { instance.Claims[j - 1] = instance.Claims[j]; }
                    instance.Count--; break;
                }
            }
        }
        internal bool TryApproach(int enemyId, out Position center, out int stopRadius)
        {
            Instance? winner = null;
            foreach (var instance in instances)
            {
                if (!instance.Used || !Contains(instance, enemyId)) { continue; }
                if (winner is null || instance.Start < winner.Start || instance.Start == winner.Start && instance.Sequence < winner.Sequence) { winner = instance; }
            }
            center = winner?.Center ?? default;
            stopRadius = winner is null ? 0 : definitions[winner.Definition].Taunt!.StopRadius;
            return winner is not null;
        }
        internal int ReduceDamage(int damage, Position hero, Position? origin, out int prevented)
        {
            prevented = 0;
            if (damage <= 0 || origin is null || origin.Value == hero) { return damage; }
            var dx = (long)origin.Value.X - hero.X; var dy = (long)origin.Value.Y - hero.Y;
            if (Math.Abs(dx) > HeroActiveGeometry.CoordinateLimit || Math.Abs(dy) > HeroActiveGeometry.CoordinateLimit) { return damage; }
            var incoming = new Position((int)dx, (int)dy); long raw = 0;
            foreach (var instance in instances)
            {
                if (!instance.Used || !instance.Charged) { continue; }
                var p = definitions[instance.Definition].Taunt!;
                if (p.DamageReductionPermille <= 0 || !InsideCone(instance.Direction, incoming, p.ShieldHalfAngleCosPermille)) { continue; }
                raw = checked(raw + p.DamageReductionPermille); instance.Charged = false;
            }
            var cap = definitions[0].Taunt!.DamageReductionCapPermille;
            var result = (int)((long)damage * (PlayerInput.Scale - Math.Min(raw, cap)) / PlayerInput.Scale);
            prevented = damage - result; return result;
        }
        internal static bool InsideCone(Position direction, Position incoming, int cosine)
        {
            var dot = (long)direction.X * incoming.X + (long)direction.Y * incoming.Y;
            var directionLength = (long)direction.X * direction.X + (long)direction.Y * direction.Y;
            var incomingLength = (long)incoming.X * incoming.X + (long)incoming.Y * incoming.Y;
            if (directionLength == 0 || incomingLength == 0) { return false; }
            if (cosine >= 0 && dot < 0) { return false; }
            if (cosine <= 0 && dot >= 0) { return true; }
            var comparison = CompareFractions((decimal)dot * dot, (decimal)directionLength * incomingLength,
                (decimal)cosine * cosine, (decimal)PlayerInput.Scale * PlayerInput.Scale);
            return cosine >= 0 ? comparison >= 0 : comparison <= 0;
        }
        private static int CompareFractions(decimal a, decimal b, decimal c, decimal d)
        {
            var sign = 1;
            while (true)
            {
                var left = (a - a % b) / b; var right = (c - c % d) / d;
                if (left != right) { return sign * left.CompareTo(right); }
                var remainderLeft = a % b; var remainderRight = c % d;
                if (remainderLeft == 0 || remainderRight == 0) { return sign * remainderLeft.CompareTo(remainderRight); }
                a = b; b = remainderLeft; c = d; d = remainderRight; sign = -sign;
            }
        }
        internal HeroActiveEffectsFrame Capture(long sequence)
        {
            var cooldowns = new HeroActiveCooldownFrame[definitions.Length];
            for (var i = 0; i < definitions.Length; i++) { cooldowns[i] = new(definitions[i].Id, preparation[i], ready[i]); }
            var views = new List<HeroTauntInstanceFrame>(count);
            foreach (var instance in instances)
            {
                if (!instance.Used) { continue; }
                var claims = new int[instance.Count]; Array.Copy(instance.Claims, claims, claims.Length);
                views.Add(new(options.OwnerId, definitions[instance.Definition].Id, instance.Sequence, instance.Start, instance.End,
                    new(instance.Center.X, instance.Center.Y), new(instance.Direction.X, instance.Direction.Y), instance.Charged, Array.AsReadOnly(claims)));
            }
            views.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            return new(sequence, Array.AsReadOnly(cooldowns), views.AsReadOnly());
        }
        internal void Write(BinaryWriter writer, long sequence)
        {
            var frame = Capture(sequence);
            writer.Write(frame.Definitions.Count);
            foreach (var definition in frame.Definitions) { writer.Write(definition.DefinitionId); writer.Write(definition.PreparationTicks); writer.Write(definition.CooldownReadyTick); }
            writer.Write(frame.Taunts.Count);
            foreach (var instance in frame.Taunts)
            {
                writer.Write(instance.OwnerId); writer.Write(instance.DefinitionId); writer.Write(instance.Sequence); writer.Write(instance.StartTick); writer.Write(instance.EndTick);
                writer.Write(instance.Center.X); writer.Write(instance.Center.Y); writer.Write(instance.Direction.X); writer.Write(instance.Direction.Y); writer.Write(instance.ShieldAvailable);
                writer.Write(instance.Claims.Count); foreach (var claim in instance.Claims) { writer.Write(claim); }
            }
        }
    }
}
