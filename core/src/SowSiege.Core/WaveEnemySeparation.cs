using System;
using System.Collections.Generic;

namespace SowSiege.Core
{
    // Version 1: integer, ID-ordered projected separation; no RNG or persistent simulation state.
    internal sealed class WaveEnemySeparation
    {
        private const int Sweeps = 2;
        private readonly WorldState world;
        private readonly MapTuning map;
        private readonly IReadOnlyDictionary<string, int> widths;
        private readonly SpatialHash spatial;
        private readonly List<EnemyState> candidates = new();
        private readonly int maximumWidth;
        private static readonly Comparison<EnemyState> IdOrder = (a, b) => a.Id.CompareTo(b.Id);

        internal WaveEnemySeparation(WorldState world, MapTuning map, WaveEnemySeparationDefinition definition)
        {
            if (definition.Version != 1) { throw new ArgumentException("Unsupported enemy separation version."); }
            this.world = world; this.map = map; widths = definition.BodyWidths;
            foreach (var width in widths.Values) { maximumWidth = Math.Max(maximumWidth, width); }
            spatial = new(Math.Max(1, maximumWidth));
        }

        internal void Resolve(List<EnemyState> ordered)
        {
            ordered.Sort(IdOrder);
            spatial.Rebuild(ordered);
            for (var sweep = 0; sweep < Sweeps; sweep++)
            {
                var changed = false;
                foreach (var first in ordered)
                {
                    if (first.Health <= 0 || !widths.TryGetValue(first.Definition, out var firstWidth)) { continue; }
                    spatial.Collect(first.Position, maximumWidth, candidates);
                    candidates.Sort(IdOrder);
                    foreach (var second in candidates)
                    {
                        if (second.Id <= first.Id || !widths.TryGetValue(second.Definition, out var secondWidth)) { continue; }
                        // 0.6 times mean width; integer padding prevents a rounding-only residual.
                        var minimum = (3 * (firstWidth + secondWidth) + 9) / 10 + 2;
                        var dx = (long)second.Position.X - first.Position.X;
                        var dy = (long)second.Position.Y - first.Position.Y;
                        var squared = dx * dx + dy * dy;
                        if (squared >= (long)minimum * minimum) { continue; }
                        var firstPinned = Charging(first); var secondPinned = Charging(second);
                        if (firstPinned && secondPinned) { continue; }
                        var distance = Root(squared);
                        if (distance == 0)
                        {
                            // Stable pair identity breaks exact coincidence without consuming random draws.
                            var direction = unchecked(first.Id * 73856093 ^ second.Id * 19349663) & 3;
                            dx = direction == 0 ? 1 : direction == 1 ? -1 : 0;
                            dy = direction == 2 ? 1 : direction == 3 ? -1 : 0;
                            distance = 1;
                        }
                        var correction = minimum - distance + 2;
                        var firstShare = firstPinned ? 0 : secondPinned ? correction : (correction + 1) / 2;
                        var secondShare = correction - firstShare;
                        var oldFirst = first.Position; var oldSecond = second.Position;
                        Move(first, -(int)(dx * firstShare / distance), -(int)(dy * firstShare / distance));
                        Move(second, (int)(dx * secondShare / distance), (int)(dy * secondShare / distance));
                        // Clamped displacement belongs to the other body, not to an invisible wall sink.
                        var firstLostX = first.Position.X - oldFirst.X + (int)(dx * firstShare / distance);
                        var firstLostY = first.Position.Y - oldFirst.Y + (int)(dy * firstShare / distance);
                        var secondLostX = second.Position.X - oldSecond.X - (int)(dx * secondShare / distance);
                        var secondLostY = second.Position.Y - oldSecond.Y - (int)(dy * secondShare / distance);
                        if (!secondPinned) { Move(second, firstLostX, firstLostY); }
                        if (!firstPinned) { Move(first, secondLostX, secondLostY); }
                        changed |= first.Position != oldFirst || second.Position != oldSecond;
                    }
                }
                if (!changed) { break; }
            }
            SettleResiduals(ordered);
        }

        private void SettleResiduals(List<EnemyState> ordered)
        {
            var step = Math.Max(1, maximumWidth / 8);
            var limit = (Math.Max(map.Width, map.Height) + step - 1) / step;
            foreach (var enemy in ordered)
            {
                if (enemy.Health <= 0 || !widths.ContainsKey(enemy.Definition) || Charging(enemy) || Clear(enemy, enemy.Position)) { continue; }
                var origin = enemy.Position;
                var settled = false;
                for (var ring = 1; ring <= limit && !settled; ring++)
                {
                    for (var offset = -ring; offset <= ring && !settled; offset++)
                    {
                        settled = TryPlace(enemy, origin, offset * step, -ring * step)
                            || TryPlace(enemy, origin, offset * step, ring * step);
                    }
                    for (var offset = -ring + 1; offset < ring && !settled; offset++)
                    {
                        settled = TryPlace(enemy, origin, -ring * step, offset * step)
                            || TryPlace(enemy, origin, ring * step, offset * step);
                    }
                }
                if (!settled)
                {
                    const string counter = "enemy-separation-unresolved";
                    world.WaveRuntime!.Counters.TryGetValue(counter, out var previous);
                    world.WaveRuntime.Counters[counter] = previous + 1;
                }
            }
        }

        private bool TryPlace(EnemyState enemy, Position origin, int dx, int dy)
        {
            var candidate = new Position(origin.X + dx, origin.Y + dy);
            if (candidate.X < 0 || candidate.X > map.Width || candidate.Y < 0 || candidate.Y > map.Height || !Clear(enemy, candidate)) { return false; }
            Move(enemy, candidate.X - enemy.Position.X, candidate.Y - enemy.Position.Y);
            return true;
        }

        private bool Clear(EnemyState enemy, Position candidate)
        {
            spatial.Collect(candidate, maximumWidth, candidates);
            foreach (var other in candidates)
            {
                if (other.Id == enemy.Id || other.Id > enemy.Id && !Charging(other) || !widths.TryGetValue(other.Definition, out var width)) { continue; }
                var minimum = (3 * (widths[enemy.Definition] + width) + 9) / 10 + 1;
                if (candidate.DistanceSquared(other.Position) < (long)minimum * minimum) { return false; }
            }
            return true;
        }

        private bool Charging(EnemyState enemy) => world.WaveRuntime!.EnemyActions.TryGetValue(enemy.Id, out var action) && action.Phase == "charge" && world.Tick < action.UntilTick;
        private void Move(EnemyState enemy, int dx, int dy)
        {
            if (dx == 0 && dy == 0) { return; }
            var position = new Position(Math.Clamp(enemy.Position.X + dx, 0, map.Width), Math.Clamp(enemy.Position.Y + dy, 0, map.Height));
            spatial.Move(enemy, position);
            if (world.WaveRuntime!.EnemyActions.TryGetValue(enemy.Id, out var action) && action.Phase.StartsWith("tell-", StringComparison.Ordinal))
            {
                // The locked target stays fixed; a displaced warning always starts at the actual actor.
                action.Origin = position;
            }
        }

        private static int Root(long value)
        {
            long low = 0, high = Math.Min(value, int.MaxValue);
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (middle * middle <= value) { low = middle; } else { high = middle - 1; }
            }
            return (int)low;
        }
    }
}
