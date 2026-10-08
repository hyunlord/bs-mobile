using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{
    internal sealed class InteractiveState
    {
        public AimMode Aim;
        public bool Invulnerable;
        public int SpawnPermille = PlayerInput.Scale;
        public long NextEventId;
        public List<PresentationEvent> Events = new();
        public void Attack(int tick, string source, Position origin, Position direction, string shape, int range, IEnumerable<EnemyState> hits, AttackGeometry? geometry = null)
        {
            var targets = hits.ToArray();
            geometry ??= Geometry(origin, direction, shape, range, targets, 0, 1, 0);
            Events.Add(new(NextEventId++, tick, PresentationKind.Attack, source, Point(origin), Point(direction), shape, range, 0,
                Array.AsReadOnly(targets.Select(enemy => Point(enemy.Position)).ToArray()), Array.AsReadOnly(targets.Select(enemy => enemy.Id).ToArray()), geometry));
        }
        internal static AttackGeometry Geometry(Position origin, Position direction, string shape, int range, IEnumerable<EnemyState> primary, int beamHalfWidth, int count, int pierce)
        {
            var rays = new List<WorldPoint>();
            if (shape == "rays" || shape == "projectile")
            {
                foreach (var enemy in primary.Take(count))
                {
                    var ray = enemy.Position == origin ? direction : new Position(enemy.Position.X - origin.X, enemy.Position.Y - origin.Y);
                    rays.Add(shape == "projectile" ? Point(enemy.Position) : RayEnd(origin, ray, range));
                }
                if (rays.Count == 0) { rays.Add(RayEnd(origin, direction, range)); }
            }
            return new(shape == "orbit" ? range >> 1 : 0, beamHalfWidth, count, pierce, Array.AsReadOnly(rays.ToArray()));
        }
        private static WorldPoint RayEnd(Position origin, Position direction, int range)
        {
            var dx = (long)direction.X; var dy = (long)direction.Y;
            if (dx == 0 && dy == 0) { dx = 1; }
            var squared = dx * dx + dy * dy;
            long low = 1; var high = Math.Abs(dx) + Math.Abs(dy);
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (middle < squared / middle || middle * middle < squared) { low = middle + 1; } else { high = middle; }
            }
            return new(checked(origin.X + (int)(dx * range / low)), checked(origin.Y + (int)(dy * range / low)));
        }
        public void Experience(int tick, PresentationKind kind, string source, Position position, long amount) => Events.Add(new(NextEventId++, tick, kind, source, Point(position), default, "", 0, amount, Array.AsReadOnly(Array.Empty<WorldPoint>()), Array.AsReadOnly(Array.Empty<int>())));
        internal static WorldPoint Point(Position position) => new(position.X, position.Y);
    }
}
