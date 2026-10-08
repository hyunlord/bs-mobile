using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    internal static class FirstPlayableOrbit
    {
        internal const int QuarterTurn = 1024;
        internal const int FullTurn = 4096;
    }
    internal enum OrbitQuadrant { East, North, West, South }
    internal sealed partial class CombatSystem
    {
        private void ActivateFirstPlayableWeapon(EquipmentState equipment)
        {
            var fp = world.FirstPlayable!; var definition = catalog.FirstPlayable!.Weapons[equipment.Id];
            var level = catalog.WeaponCombat!.Weapons[equipment.Id].Levels[Math.Min(equipment.Level, catalog.WeaponCombat!.Weapons[equipment.Id].Levels.Length) - 1];
            equipment.ReadyTick = checked(world.Tick + (runtime?.Modify("attack-cooldown", level.CooldownTicks, new(world.Lord, equipment.Id), 1) ?? level.CooldownTicks));
            var facing = world.WeaponCombat!.Facing;
            diagnostics?.Attack("weapon", "", equipment.Id, spatial.Query(world.Lord, level.Range).Count(e => e.Health > 0), equipment.ReadyTick - world.Tick);
            var count = definition.Form is "chain" or "sector90" or "sector180" ? 1 : level.Count;
            fp.Count("equipment:" + equipment.Id + ":activation"); fp.Count("form:" + definition.Form + ":activation");
            var activationId = world.AllocateId();
            var activation = new AttackActivationState { Source = equipment.Id, Form = definition.Form };
            fp.Activations.Add(activationId, activation);
            for (var index = 0; index < count && fp.Attacks.Count < catalog.FirstPlayable.MaxActiveAttacks; index++)
            {
                var direction = facing;
                if (definition.Form == "volley")
                {
                    var offset = (index + index - count + 1) * definition.SpreadPermille;
                    direction = new((int)Math.Clamp((long)facing.X * PlayerInput.Scale - (long)facing.Y * offset, int.MinValue, int.MaxValue), (int)Math.Clamp((long)facing.Y * PlayerInput.Scale + (long)facing.X * offset, int.MinValue, int.MaxValue));
                }
                var origin = world.Lord;
                if (definition.Form == "field")
                {
                    var target = spatial.Query(origin, level.Range).Where(e => e.Health > 0).OrderBy(e => e.Position.DistanceSquared(origin)).ThenBy(e => e.Id).Skip(index).FirstOrDefault();
                    var originCopy = origin;
                    var cart = fp.MapEvents.Where(e => e.Health > 0 && RuntimeSystem.Within(e.Position, originCopy, level.Range)).OrderBy(e => e.Position.DistanceSquared(originCopy)).ThenBy(e => e.Id).FirstOrDefault();
                    origin = target?.Position ?? cart?.Position ?? Step(origin, direction, level.Range >> 1);
                }
                activation.Remaining++;
                fp.Attacks.Add(new() { Id = world.AllocateId(), ActivationId = activationId, Source = equipment.Id, Form = definition.Form, Origin = origin, Position = origin, Previous = origin, Direction = direction, Level = Math.Min(equipment.Level, catalog.WeaponCombat!.Weapons[equipment.Id].Levels.Length), RemainingHits = definition.Form is "sector90" or "sector180" or "chain" ? level.Count + level.Pierce : level.Pierce + 1, Phase = definition.Form == "nova" ? index * definition.LifetimeTicks / Math.Max(1, count) : definition.Form is "projectile" or "piercing" ? index * definition.BurstIntervalTicks : index * FirstPlayableOrbit.FullTurn / Math.Max(1, count) });
            }
            if (activation.Remaining == 0)
            {
                CompleteActivation(activationId);
            }
        }

        private void CompleteActivation(int id)
        {
            var fp = world.FirstPlayable!; var activation = fp.Activations[id];
            fp.Count("form:" + activation.Form + ":completed"); fp.Count("equipment:" + activation.Source + ":completed");
            if (!activation.HadHit) { fp.Count("form:" + activation.Form + ":empty"); fp.Count("equipment:" + activation.Source + ":empty"); }
            fp.Activations.Remove(id);
        }

        internal void TickFirstPlayableAttacks()
        {
            if (world.FirstPlayable is not { } fp) { return; }
            foreach (var attack in fp.Attacks.ToArray())
            {
                var definition = catalog.FirstPlayable!.Weapons[attack.Source];
                var level = catalog.WeaponCombat!.Weapons[attack.Source].Levels[attack.Level - 1];
                attack.Previous = attack.Position;
                if (attack.Form is "projectile" or "piercing" or "nova" && attack.Age < attack.Phase) { attack.Age++; continue; }
                switch (attack.Form)
                {
                    case "projectile": case "volley": case "piercing": attack.Position = Step(attack.Position, attack.Direction, definition.Speed); break;
                    case "boomerang":
                        attack.Position = attack.Age < (definition.LifetimeTicks >> 1) ? Step(attack.Position, attack.Direction, definition.Speed) : attack.Position.MoveToward(world.Lord, definition.Speed);
                        break;
                    case "orbit":
                        var phase = (int)(((long)attack.Phase + (long)attack.Age * definition.Speed) % FirstPlayableOrbit.FullTurn);
                        var side = (OrbitQuadrant)(phase / FirstPlayableOrbit.QuarterTurn); var along = phase % FirstPlayableOrbit.QuarterTurn;
                        var x = side switch { OrbitQuadrant.East => FirstPlayableOrbit.QuarterTurn - along, OrbitQuadrant.North => -along, OrbitQuadrant.West => -FirstPlayableOrbit.QuarterTurn + along, _ => along };
                        var y = side switch { 0 => along, OrbitQuadrant.North => FirstPlayableOrbit.QuarterTurn - along, OrbitQuadrant.West => -along, _ => -FirstPlayableOrbit.QuarterTurn + along };
                        attack.Position = new(world.Lord.X + x * level.Range / FirstPlayableOrbit.QuarterTurn, world.Lord.Y + y * level.Range / FirstPlayableOrbit.QuarterTurn);
                        break;
                    case "nova": attack.Position = attack.Origin; break;
                }
                var map = catalog.Tuning.World.Map;
                if (attack.Position.X < 0 || attack.Position.Y < 0 || attack.Position.X > map.Width || attack.Position.Y > map.Height)
                { attack.Position = new(Math.Clamp(attack.Position.X, 0, map.Width), Math.Clamp(attack.Position.Y, 0, map.Height)); attack.Age = definition.LifetimeTicks; }
                var hits = new List<EnemyState>();
                if (attack.Form == "chain")
                {
                    var origin = attack.Origin;
                    for (var n = 0; n < attack.RemainingHits; n++)
                    {
                        var range = n == 0 ? level.Range : definition.ChainRange;
                        var next = Targets(origin, range).Where(e => !attack.HitTicks.ContainsKey(e.Id)).OrderBy(e => e.Position.DistanceSquared(origin)).ThenBy(e => e.Id).FirstOrDefault();
                        if (next is null) { break; }
                        HitTarget(attack, next, level); hits.Add(new() { Id = next.Id, Position = next.Position }); origin = next.Position;
                    }
                }
                else
                {
                    var radius = attack.Form is "sector90" or "sector180" or "nova" ? level.Range : definition.Radius + definition.Speed;
                    foreach (var enemy in Targets(attack.Position, radius).OrderBy(e => e.Position.DistanceSquared(attack.Previous)).ThenBy(e => e.Id))
                    {
                        if (!AttackTouches(attack, enemy.Position, definition, level)) { continue; }
                        if (attack.HitTicks.TryGetValue(enemy.Id, out var last) && (attack.Form is not ("orbit" or "field" or "boomerang") || world.Tick - last < definition.HitIntervalTicks)) { continue; }
                        if (attack.Form is "projectile" or "volley" or "piercing" or "sector90" or "sector180" && attack.RemainingHits <= 0) { break; }
                        hits.Add(new() { Id = enemy.Id, Position = enemy.Position }); HitTarget(attack, enemy, level); attack.RemainingHits--;
                    }
                }
                if (hits.Count > 0 || attack.Age == 0)
                {
                    interactive?.Attack(world.Tick, attack.Source, attack.Position, attack.Direction, attack.Form, level.Range, hits);
                }

                attack.Age++;
                var finished = attack.Form is "sector90" or "sector180" or "chain" || attack.Age >= definition.LifetimeTicks
                    || attack.Form is "projectile" or "volley" or "piercing" && attack.RemainingHits <= 0;
                if (!finished) { continue; }
                var activation = fp.Activations[attack.ActivationId]; activation.HadHit |= attack.HitTicks.Count > 0; activation.Remaining--;
                fp.Count("form:" + attack.Form + ":instances-completed");
                if (activation.Remaining == 0)
                {
                    CompleteActivation(attack.ActivationId);
                }

                fp.Attacks.Remove(attack);
            }
        }

        private static Position Step(Position position, Position direction, int speed)
        {
            var scale = Math.Max(1L, Math.Max(Math.Abs((long)direction.X), Math.Abs((long)direction.Y)));
            return new((int)Math.Clamp((long)position.X + (long)direction.X * speed / scale, int.MinValue, int.MaxValue), (int)Math.Clamp((long)position.Y + (long)direction.Y * speed / scale, int.MinValue, int.MaxValue));
        }
        private static bool AttackTouches(ActiveAttackState attack, Position target, FirstPlayableWeaponDefinition definition, WeaponLevelDefinition level)
        {
            var dx = (long)target.X - attack.Origin.X; var dy = (long)target.Y - attack.Origin.Y;
            if (attack.Form is "sector90" or "sector180")
            {
                var dot = dx * attack.Direction.X + dy * attack.Direction.Y;
                var cross = dx * attack.Direction.Y - dy * attack.Direction.X;
                return attack.Origin.DistanceSquared(target) <= (long)level.Range * level.Range && (attack.Form == "sector90" ? dot >= Math.Abs(cross) : dot >= 0);
            }
            if (attack.Form == "nova")
            {
                var outer = (int)Math.Min(level.Range, ((long)attack.Age - attack.Phase + 1) * definition.Speed); var inner = Math.Max(0, outer - definition.Radius);
                var distance = attack.Origin.DistanceSquared(target); return distance <= (long)outer * outer && distance >= (long)inner * inner;
            }
            if (attack.Form is "projectile" or "volley" or "piercing" or "boomerang")
            {
                var vx = (long)attack.Position.X - attack.Previous.X; var vy = (long)attack.Position.Y - attack.Previous.Y;
                var tx = (long)target.X - attack.Previous.X; var ty = (long)target.Y - attack.Previous.Y;
                var length = vx * vx + vy * vy;
                var projection = length == 0 ? 0 : Math.Clamp(tx * vx + ty * vy, 0, length);
                var nearest = length == 0 ? attack.Position : new Position(attack.Previous.X + (int)(vx * projection / length), attack.Previous.Y + (int)(vy * projection / length));
                return nearest.DistanceSquared(target) <= (long)definition.Radius * definition.Radius;
            }
            return attack.Position.DistanceSquared(target) <= (long)definition.Radius * definition.Radius;
        }
        private void HitFirstPlayable(ActiveAttackState attack, EnemyState enemy, WeaponLevelDefinition level)
        {
            if (attack.HitTicks.Count == 0)
            {
                runtime?.Emit("attack", new(attack.Origin, attack.Source, enemy));
            }

            var requested = checked((level.Damage + random.Next(catalog.Tuning.DamageRollMax)) * catalog.Heroes[options.HeroId].DamageMultiplier);
            requested = runtime?.Modify("attack-damage", requested, new(attack.Position, attack.Source, enemy)) ?? requested;
            var dealt = Math.Min(enemy.Health, requested); enemy.Health -= dealt; world.WeaponDamage += dealt;
            diagnostics?.Hit("weapon", "", attack.Source, requested, dealt);
            attack.HitTicks[enemy.Id] = world.Tick;
            var fp = world.FirstPlayable!; fp.Count("form:" + attack.Form + ":hit"); fp.Count("equipment:" + attack.Source + ":hit");
            fp.Coverage["equipment:" + attack.Source + ":overkill"] = fp.Coverage.GetValueOrDefault("equipment:" + attack.Source + ":overkill") + requested - dealt;
            var knockback = runtime?.Modify("attack-knockback", level.Knockback, new(attack.Position, attack.Source, enemy)) ?? level.Knockback;
            if (knockback > 0)
            {
                var map = catalog.Tuning.World.Map;
                spatial.Move(enemy, new(Math.Clamp(enemy.Position.X + Math.Sign(enemy.Position.X - attack.Position.X) * knockback, 0, map.Width), Math.Clamp(enemy.Position.Y + Math.Sign(enemy.Position.Y - attack.Position.Y) * knockback, 0, map.Height)));
            }
            interactive?.Experience(world.Tick, PresentationKind.Damage, attack.Source, enemy.Position, dealt);
        }
        private sealed record AttackTarget(int Id, Position Position, EnemyState? Enemy, MapEventState? Cart);
        private IEnumerable<AttackTarget> Targets(Position origin, int radius) => spatial.Query(origin, radius).Where(e => e.Health > 0).Select(e => new AttackTarget(e.Id, e.Position, e, null))
            .Concat(world.FirstPlayable!.MapEvents.Where(e => e.Health > 0 && RuntimeSystem.Within(e.Position, origin, radius)).Select(e => new AttackTarget(e.Id, e.Position, null, e)));
        private void HitTarget(ActiveAttackState attack, AttackTarget target, WeaponLevelDefinition level)
        {
            if (target.Enemy is not null) { HitFirstPlayable(attack, target.Enemy, level); return; }
            var cart = target.Cart!;
            if (attack.HitTicks.Count == 0)
            {
                runtime?.Emit("attack", new(attack.Origin, attack.Source));
            }

            var requested = checked((level.Damage + random.Next(catalog.Tuning.DamageRollMax)) * catalog.Heroes[options.HeroId].DamageMultiplier);
            requested = runtime?.Modify("attack-damage", requested, new(attack.Position, attack.Source)) ?? requested;
            var dealt = Math.Min(cart.Health, requested); cart.Health -= dealt; world.WeaponDamage += dealt;
            diagnostics?.Hit("weapon", "", attack.Source, requested, dealt); attack.HitTicks[cart.Id] = world.Tick;
            var fp = world.FirstPlayable!; fp.Count("form:" + attack.Form + ":hit"); fp.Count("equipment:" + attack.Source + ":hit");
            fp.Coverage["equipment:" + attack.Source + ":overkill"] = fp.Coverage.GetValueOrDefault("equipment:" + attack.Source + ":overkill") + requested - dealt;
            interactive?.Experience(world.Tick, PresentationKind.Damage, attack.Source, cart.Position, dealt);
        }
    }
}
