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
        internal const int ArenaSpawnInset = 600;
    }

    internal sealed class FieldState
    {
        // ADR0051 freezes this integer direction lattice and hash mapping for field-v1 replays.
        private const int DirectionNearAxis = 924;
        private const int DirectionFarAxis = 383;
        private const int DirectionDiagonal = 707;
        private const uint SeedHashMultiplier = 73856093;
        private const uint TickHashMultiplier = 19349663;
        private const uint EnemyHashMultiplier = 83492791;
        internal static int DirectionCount => Directions.Length;
        private static readonly Position[] Directions =
        {
            new(PlayerInput.Scale, 0), new(DirectionNearAxis, DirectionFarAxis), new(DirectionDiagonal, DirectionDiagonal), new(DirectionFarAxis, DirectionNearAxis),
            new(0, PlayerInput.Scale), new(-DirectionFarAxis, DirectionNearAxis), new(-DirectionDiagonal, DirectionDiagonal), new(-DirectionNearAxis, DirectionFarAxis),
            new(-PlayerInput.Scale, 0), new(-DirectionNearAxis, -DirectionFarAxis), new(-DirectionDiagonal, -DirectionDiagonal), new(-DirectionFarAxis, -DirectionNearAxis),
            new(0, -PlayerInput.Scale), new(DirectionFarAxis, -DirectionNearAxis), new(DirectionDiagonal, -DirectionDiagonal), new(DirectionNearAxis, -DirectionFarAxis)
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
            var radius = ArenaActive ? FieldRules.ArenaRadius - FieldRules.ArenaSpawnInset : FieldRules.SpawnRadius;
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
            var hash = unchecked((uint)Seed * SeedHashMultiplier ^ (uint)tick * TickHashMultiplier ^ (uint)enemyId * EnemyHashMultiplier);
            var direction = (int)(hash & (uint)(DirectionCount - 1));
            var candidate = Directions[direction];
            if ((long)candidate.X * Travel.X + (long)candidate.Y * Travel.Y < 0) { direction = (direction + DirectionCount / WaveGeometry.MidpointDivisor) & (DirectionCount - 1); }
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
            long low = 1, high = Math.Min(value, (long)int.MaxValue * WaveGeometry.MidpointDivisor);
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
