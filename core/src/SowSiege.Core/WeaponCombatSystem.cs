using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SowSiege.Core
{
    internal sealed partial class CombatSystem
    {
        private void ActivateWeapon(EquipmentState equipment)
        {
            if (catalog.FirstPlayable is not null) { ActivateFirstPlayableWeapon(equipment); return; }
            var definition = catalog.WeaponCombat!.Weapons[equipment.Id];
            var level = definition.Levels[Math.Min(equipment.Level, definition.Levels.Length) - 1];
            equipment.ReadyTick = checked(world.Tick + (runtime?.Modify("attack-cooldown", level.CooldownTicks, new(world.Lord, equipment.Id), 1) ?? level.CooldownTicks));
            var candidates = spatial.Query(world.Lord, level.Range).Where(enemy => enemy.Health > 0)
                .OrderBy(enemy => enemy.Position.DistanceSquared(world.Lord)).ThenBy(enemy => enemy.Id).ToArray();
            var selected = WeaponTargets.Select(definition, level, world.Lord, world.WeaponCombat!.Facing, candidates);
            diagnostics?.Attack("weapon", "", equipment.Id, candidates.Length, equipment.ReadyTick - world.Tick, selected.Eligible);
            interactive?.Attack(world.Tick, equipment.Id, world.Lord, world.WeaponCombat!.Facing, definition.AttackModel, level.Range, selected.Hits,
                InteractiveState.Geometry(world.Lord, world.WeaponCombat.Facing, definition.AttackModel, level.Range, candidates, definition.BeamHalfWidth, level.Count, level.Pierce));
            foreach (var enemy in selected.Hits)
            {
                var requested = checked((level.Damage + random.Next(catalog.Tuning.DamageRollMax)) * catalog.Heroes[options.HeroId].DamageMultiplier);
                requested = runtime?.Modify("attack-damage", requested, new(world.Lord, equipment.Id, enemy)) ?? requested;
                var dealt = Math.Min(enemy.Health, requested);
                enemy.Health -= dealt;
                world.WeaponDamage += dealt;
                var knockback = runtime?.Modify("attack-knockback", level.Knockback, new(world.Lord, equipment.Id, enemy)) ?? level.Knockback;
                var before = enemy.Position;
                if (knockback > 0)
                {
                    var map = catalog.Tuning.World.Map;
                    spatial.Move(enemy, new(Math.Clamp(enemy.Position.X + Math.Sign(enemy.Position.X - world.Lord.X) * knockback, 0, map.Width),
                        Math.Clamp(enemy.Position.Y + Math.Sign(enemy.Position.Y - world.Lord.Y) * knockback, 0, map.Height)));
                }
                diagnostics?.Hit("weapon", "", equipment.Id, requested, dealt,
                    knockback: Math.Abs((long)enemy.Position.X - before.X) + Math.Abs((long)enemy.Position.Y - before.Y));
            }
            runtime?.Emit("attack", new(world.Lord, equipment.Id, selected.Hits.FirstOrDefault()));
        }
    }

    internal static class WeaponTargets
    {
        internal static (EnemyState[] Hits, int Eligible) Select(WeaponCombatWeaponDefinition definition, WeaponLevelDefinition level, Position origin, Position facing, EnemyState[] candidates)
        {
            if (definition.AttackModel == "rays") { return Rays(definition, level, origin, facing, candidates); }
            var eligible = candidates.Where(enemy => InShape(definition.AttackModel, origin, facing, enemy.Position)).ToArray();
            return (eligible.Take(level.Count).ToArray(), eligible.Length);
        }

        private static bool InShape(string model, Position origin, Position facing, Position target)
        {
            var dx = (long)target.X - origin.X; var dy = (long)target.Y - origin.Y;
            var dot = dx * facing.X + dy * facing.Y;
            var cross = dx * facing.Y - dy * facing.X;
            return model switch
            {
                "sector90" => dot >= Math.Abs(cross),
                "sector180" => dot >= 0,
                "disk" => true,
                _ => throw new InvalidOperationException($"Unknown weapon attack model {model}")
            };
        }

        private static (EnemyState[] Hits, int Eligible) Rays(WeaponCombatWeaponDefinition definition, WeaponLevelDefinition level, Position origin, Position facing, EnemyState[] candidates)
        {
            var hits = new List<EnemyState>();
            var hitIds = new HashSet<int>();
            var eligibleIds = new HashSet<int>();
            foreach (var primary in candidates.Take(level.Count))
            {
                var direction = primary.Position == origin ? facing : new Position(primary.Position.X - origin.X, primary.Position.Y - origin.Y);
                var ray = candidates.Where(enemy => InRay(origin, direction, enemy.Position, definition.BeamHalfWidth))
                    .OrderBy(enemy => ((long)enemy.Position.X - origin.X) * direction.X + ((long)enemy.Position.Y - origin.Y) * direction.Y)
                    .ThenBy(enemy => enemy.Id).ToArray();
                foreach (var enemy in ray) { eligibleIds.Add(enemy.Id); }
                foreach (var enemy in ray.Where(enemy => !hitIds.Contains(enemy.Id)).Take(level.Pierce + 1))
                {
                    hitIds.Add(enemy.Id); hits.Add(enemy);
                }
            }
            return (hits.ToArray(), eligibleIds.Count);
        }

        private static bool InRay(Position origin, Position direction, Position target, int halfWidth)
        {
            var dx = (long)target.X - origin.X; var dy = (long)target.Y - origin.Y;
            if (dx * direction.X + dy * direction.Y < 0) { return false; }
            // Squaring the cross product exceeds Int64 at valid content bounds.
            var cross = (BigInteger)dx * direction.Y - (BigInteger)dy * direction.X;
            var lengthSquared = (BigInteger)direction.X * direction.X + (BigInteger)direction.Y * direction.Y;
            return cross * cross <= (BigInteger)halfWidth * halfWidth * lengthSquared;
        }
    }
}
