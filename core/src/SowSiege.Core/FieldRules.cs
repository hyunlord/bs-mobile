using System;

namespace SowSiege.Core
{
    public sealed record FieldOptions(int Version = 1);
    public sealed record FieldArenaFrame(int BossId, WorldPoint Center, int Radius);
    public sealed record FieldFrame(int Version, int Seed, WorldPoint Home, WorldPoint LastTravelDirection, FieldArenaFrame? Arena);

    public static class FieldRules
    {
        public const int Version = 1;
        public const int SpawnRadius = 16200;
        public const int RecycleRadius = 32400;
        public const int ArenaRadius = 6000;
        public const int SectorSize = 12000;
    }

    internal sealed class FieldState
    {
        private static readonly Position[] Directions =
        {
            new(1000, 0), new(924, 383), new(707, 707), new(383, 924),
            new(0, 1000), new(-383, 924), new(-707, 707), new(-924, 383),
            new(-1000, 0), new(-924, -383), new(-707, -707), new(-383, -924),
            new(0, -1000), new(383, -924), new(707, -707), new(924, -383)
        };
        internal int Seed { get; }
        internal Position Travel = new(1, 0);
        internal int ArenaBossId = -1;
        internal Position ArenaCenter;
        internal bool ArenaActive => ArenaBossId >= 0;
        internal FieldState(FieldOptions options, int seed)
        {
            if (options.Version != FieldRules.Version) { throw new ArgumentException("Unsupported field version."); }
            Seed = seed;
        }
        internal void AcceptInput(PlayerInput input)
        {
            if (input.X != 0 || input.Y != 0) { Travel = new(input.X, input.Y); }
        }
        internal FieldFrame Frame(Position home) => new(FieldRules.Version, Seed, InteractiveState.Point(home), InteractiveState.Point(Travel),
            ArenaActive ? new(ArenaBossId, InteractiveState.Point(ArenaCenter), FieldRules.ArenaRadius) : null);
        internal void OpenArena(int bossId, Position center) { ArenaBossId = bossId; ArenaCenter = center; }
        internal void CloseArena() { ArenaBossId = -1; ArenaCenter = default; }
        internal Position Constrain(Position point) => ArenaActive ? Project(point, ArenaCenter, FieldRules.ArenaRadius) : point;
        internal Position Spawn(Position lord, int direction)
        {
            var center = ArenaActive ? ArenaCenter : lord;
            var radius = ArenaActive ? FieldRules.ArenaRadius - 600 : FieldRules.SpawnRadius;
            var delta = Directions[direction & (Directions.Length - 1)];
            var squared = (long)delta.X * delta.X + (long)delta.Y * delta.Y;
            var length = CeilingRoot(squared);
            if (length * length != squared) { length--; }
            // Round outward so every ordinary field spawn is beyond the canonical viewport envelope.
            static long Outward(long value, long denominator) => value >= 0 ? (value + denominator - 1) / denominator : -((-value + denominator - 1) / denominator);
            var position = new Position(checked(center.X + (int)Outward((long)delta.X * radius, length)), checked(center.Y + (int)Outward((long)delta.Y * radius, length)));
            return Constrain(position);
        }
        internal Position Recycle(Position lord, int tick, int enemyId)
        {
            var hash = unchecked((uint)Seed * 73856093u ^ (uint)tick * 19349663u ^ (uint)enemyId * 83492791u);
            var direction = (int)(hash & 15);
            var candidate = Directions[direction];
            if ((long)candidate.X * Travel.X + (long)candidate.Y * Travel.Y < 0) { direction = (direction + 8) & 15; }
            return Spawn(lord, direction);
        }
        internal static Position Project(Position point, Position center, int radius)
        {
            var dx = (long)point.X - center.X; var dy = (long)point.Y - center.Y;
            var squared = dx * dx + dy * dy;
            if (squared <= (long)radius * radius) { return point; }
            var length = CeilingRoot(squared);
            return new(checked(center.X + (int)(dx * radius / length)), checked(center.Y + (int)(dy * radius / length)));
        }
        internal static long CeilingRoot(long value)
        {
            if (value <= 0) { return 0; }
            long low = 1, high = Math.Min(value, int.MaxValue * 2L);
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (middle < value / middle || middle == value / middle && value % middle != 0) { low = middle + 1; }
                else { high = middle; }
            }
            return low;
        }
    }
}
