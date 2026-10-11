// Frozen v1 oracle for #218: intentionally retains the original grid and scan.
using System;
using System.Collections.Generic;

namespace SowSiege.Core
{
    // Version 1: integer, ID-ordered projected separation; no RNG or persistent simulation state.
    internal sealed class ReferenceWaveEnemySeparation
    {
        private const int RelaxationSweeps = 2;
        private const int MinimumDistanceWidthSumNumerator = 3;
        private const int MinimumDistanceWidthSumDenominator = 10;
        private const int IntegerResolutionPadding = 2;
        private const int PairHashFirstMultiplier = 73856093;
        private const int PairHashSecondMultiplier = 19349663;
        private const int PositiveYDirection = 2;
        private const int NegativeYDirection = 3;
        private const int ResidualGridDivisions = 8;
        private readonly WorldState world;
        private readonly FieldState? field;
        private readonly MapTuning map;
        private readonly IReadOnlyDictionary<string, int> widths;
        private readonly SpatialHash spatial;
        private readonly List<EnemyState> candidates = new();
        private readonly int maximumWidth;
        private readonly Dictionary<int, (int Width, bool Charging)> bodies = new();
        private static readonly Comparison<EnemyState> IdOrder = (a, b) => a.Id.CompareTo(b.Id);

        internal ReferenceWaveEnemySeparation(WorldState world, MapTuning map, WaveEnemySeparationDefinition definition, FieldState? field = null)
        {
            if (definition.Version != 1) { throw new ArgumentException("Unsupported enemy separation version."); }
            this.world = world; this.map = map; this.field = field; widths = definition.BodyWidths;
            foreach (var width in widths.Values) { maximumWidth = Math.Max(maximumWidth, width); }
            spatial = new(Math.Max(1, maximumWidth), field is not null);
        }

        internal void Resolve(List<EnemyState> ordered)
        {
            ordered.Sort(IdOrder);
            bodies.Clear();
            foreach (var enemy in ordered)
            {
                if (widths.TryGetValue(enemy.Definition, out var width))
                {
                    var charging = world.WaveRuntime!.EnemyActions.TryGetValue(enemy.Id, out var action) && action.Phase == "charge" && world.Tick < action.UntilTick;
                    bodies.Add(enemy.Id, (width, charging));
                }
            }
            spatial.Rebuild(ordered);
            for (var sweep = 0; sweep < RelaxationSweeps; sweep++)
            {
                var changed = false;
                foreach (var first in ordered)
                {
                    if (first.Health <= 0 || !bodies.TryGetValue(first.Id, out var firstBody)) { continue; }
                    spatial.Collect(first.Position, maximumWidth, candidates);
                    candidates.Sort(IdOrder);
                    foreach (var second in candidates)
                    {
                        if (second.Id <= first.Id || !bodies.TryGetValue(second.Id, out var secondBody)) { continue; }
                        // 0.6 times mean width; integer padding prevents a rounding-only residual.
                        var minimum = (MinimumDistanceWidthSumNumerator * (firstBody.Width + secondBody.Width) + MinimumDistanceWidthSumDenominator - 1) / MinimumDistanceWidthSumDenominator + IntegerResolutionPadding;
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
                            var direction = unchecked(first.Id * PairHashFirstMultiplier ^ second.Id * PairHashSecondMultiplier) & (WaveGeometry.RectangleSides - 1);
                            dx = direction == 0 ? 1 : direction == 1 ? -1 : 0;
                            dy = direction == PositiveYDirection ? 1 : direction == NegativeYDirection ? -1 : 0;
                            distance = 1;
                        }
                        var correction = minimum - distance + IntegerResolutionPadding;
                        var firstShare = firstPinned ? 0 : secondPinned ? correction : (correction + 1) / WaveGeometry.MidpointDivisor;
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
            var step = Math.Max(1, maximumWidth / ResidualGridDivisions);
            var limit = ((field is null ? Math.Max(map.Width, map.Height) : FieldRules.SectorSize) + step - 1) / step;
            foreach (var enemy in ordered)
            {
                if (enemy.Health <= 0 || !bodies.ContainsKey(enemy.Id) || Charging(enemy) || Clear(enemy, enemy.Position)) { continue; }
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
            if ((field is null ? candidate.X < 0 || candidate.X > map.Width || candidate.Y < 0 || candidate.Y > map.Height : field.Constrain(candidate) != candidate) || !Clear(enemy, candidate)) { return false; }
            Move(enemy, candidate.X - enemy.Position.X, candidate.Y - enemy.Position.Y);
            return true;
        }

        private bool Clear(EnemyState enemy, Position candidate)
        {
            spatial.Collect(candidate, maximumWidth, candidates);
            foreach (var other in candidates)
            {
                if (other.Id == enemy.Id || other.Id > enemy.Id && !Charging(other) || !bodies.TryGetValue(other.Id, out var body)) { continue; }
                var minimum = (MinimumDistanceWidthSumNumerator * (bodies[enemy.Id].Width + body.Width) + MinimumDistanceWidthSumDenominator - 1) / MinimumDistanceWidthSumDenominator + 1;
                if (candidate.DistanceSquared(other.Position) < (long)minimum * minimum) { return false; }
            }
            return true;
        }

        private bool Charging(EnemyState enemy) => bodies.TryGetValue(enemy.Id, out var body) && body.Charging;
        private void Move(EnemyState enemy, int dx, int dy)
        {
            if (dx == 0 && dy == 0) { return; }
            var position = field is null ? new Position(Math.Clamp(enemy.Position.X + dx, 0, map.Width), Math.Clamp(enemy.Position.Y + dy, 0, map.Height))
                : field.Constrain(new(checked(enemy.Position.X + dx), checked(enemy.Position.Y + dy)));
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
                var middle = low + (high - low + 1) / WaveGeometry.MidpointDivisor;
                if (middle * middle <= value) { low = middle; } else { high = middle - 1; }
            }
            return (int)low;
        }
    }
}
