using System;
using System.Collections.Generic;

namespace SowSiege.Core
{
    // Private to separation: bucket order is not observable by attacks or target selection.
    internal sealed class WaveSeparationGrid
    {
        private readonly int cellSize;
        private readonly bool signed;
        private readonly Dictionary<long, List<EnemyState>> cells = new(CellKeyComparer.Instance);
        private readonly Dictionary<int, int> slots = new();
        private readonly Stack<List<EnemyState>> freeBuckets = new();
        private const int CoordinateBits = 32;
        private const int HashFirstShift = 30;
        private const int HashSecondShift = 27;
        private const int HashFinalShift = 31;
        private const ulong HashFirstMultiplier = 13787848793156543929UL;
        private const ulong HashSecondMultiplier = 10723151780598845931UL;
        private const int WindowRadix = 2;
        private const int PaddedInclusiveSpan = 3;
        private const int MaximumDenseCells = 16384;
        private const int SparseCloudWindowSide = 64;
        private List<EnemyState>?[] dense = Array.Empty<List<EnemyState>?>();
        private readonly List<List<EnemyState>> activeBuckets = new();
        private int denseX, denseY, denseWidth, denseHeight;

        private sealed class CellKeyComparer : IEqualityComparer<long>
        {
            internal static readonly CellKeyComparer Instance = new();
            public bool Equals(long first, long second) => first == second;
            public int GetHashCode(long key)
            {
                // Mix both signed cell coordinates instead of XORing equal diagonal halves.
                unchecked
                {
                    var value = (ulong)key;
                    value = (value ^ (value >> HashFirstShift)) * HashFirstMultiplier;
                    value = (value ^ (value >> HashSecondShift)) * HashSecondMultiplier;
                    value ^= value >> HashFinalShift;
                    return (int)(value ^ (value >> CoordinateBits));
                }
            }
        }

        internal WaveSeparationGrid(int cellSize, bool signed)
        { this.cellSize = cellSize; this.signed = signed; }

        private int Cell(int value) => signed && value < 0 ? (int)(((long)value - cellSize + 1) / cellSize) : value / cellSize;
        private (int X, int Y) Key(Position p) => (Cell(p.X), Cell(p.Y));
        private static long Pack(int x, int y) => ((long)x << CoordinateBits) | (uint)y;
        private int DenseIndex(int x, int y)
        {
            var localX = (long)x - denseX; var localY = (long)y - denseY;
            return localX >= 0 && localX < denseWidth && localY >= 0 && localY < denseHeight
                ? (int)localX * denseHeight + (int)localY : -1;
        }

        private List<EnemyState>? FindBucket(int x, int y)
        {
            var index = DenseIndex(x, y);
            if (index >= 0) { return dense[index]; }
            return cells.TryGetValue(Pack(x, y), out var bucket) ? bucket : null;
        }

        private List<EnemyState> Bucket(int x, int y)
        {
            var index = DenseIndex(x, y);
            List<EnemyState>? bucket;
            if (index >= 0) { bucket = dense[index]; if (bucket != null) { return bucket; } }
            else if (cells.TryGetValue(Pack(x, y), out bucket)) { return bucket; }
            bucket = freeBuckets.Count == 0 ? new() : freeBuckets.Pop();
            activeBuckets.Add(bucket);
            if (index >= 0) { dense[index] = bucket; } else { cells.Add(Pack(x, y), bucket); }
            return bucket;
        }

        private static int WindowSize(long span)
        {
            if (span > MaximumDenseCells) { return MaximumDenseCells + 1; }
            var size = 1;
            while (size < span) { size *= WindowRadix; }
            return size;
        }

        private void ConfigureWindow(List<EnemyState> enemies)
        {
            var minX = int.MaxValue; var minY = int.MaxValue;
            var maxX = int.MinValue; var maxY = int.MinValue;
            EnemyState? first = null;
            foreach (var enemy in enemies)
            {
                if (enemy.Health <= 0) { continue; }
                if (first == null) { first = enemy; }
                var key = Key(enemy.Position);
                minX = Math.Min(minX, key.X); minY = Math.Min(minY, key.Y);
                maxX = Math.Max(maxX, key.X); maxY = Math.Max(maxY, key.Y);
            }
            if (first == null) { denseWidth = denseHeight = 0; return; }
            var width = WindowSize((long)maxX - minX + PaddedInclusiveSpan);
            var height = WindowSize((long)maxY - minY + PaddedInclusiveSpan);
            if ((long)width * height > MaximumDenseCells)
            {
                // A distant outlier must not force unbounded memory or constrain motion.
                var key = Key(first.Position);
                width = height = SparseCloudWindowSide;
                minX = (int)Math.Max(int.MinValue + 1L, (long)key.X - width / WindowRadix + 1);
                minY = (int)Math.Max(int.MinValue + 1L, (long)key.Y - height / WindowRadix + 1);
            }
            denseX = minX == int.MinValue ? minX : minX - 1;
            denseY = minY == int.MinValue ? minY : minY - 1;
            denseWidth = width; denseHeight = height;
            var required = checked(width * height);
            if (dense.Length < required) { dense = new List<EnemyState>?[required]; }
        }

        internal void Rebuild(List<EnemyState> enemies)
        {
            foreach (var bucket in activeBuckets) { bucket.Clear(); freeBuckets.Push(bucket); }
            activeBuckets.Clear();
            Array.Clear(dense, 0, dense.Length); cells.Clear(); slots.Clear();
            ConfigureWindow(enemies);
            foreach (var enemy in enemies)
            {
                if (enemy.Health <= 0) { continue; }
                var key = Key(enemy.Position); var bucket = Bucket(key.X, key.Y);
                slots.Add(enemy.Id, bucket.Count); bucket.Add(enemy);
            }
        }

        internal void Move(EnemyState enemy, Position destination)
        {
            var oldKey = Key(enemy.Position); var newKey = Key(destination);
            enemy.Position = destination;
            if (oldKey == newKey) { return; }
            var oldBucket = FindBucket(oldKey.X, oldKey.Y)!; var slot = slots[enemy.Id];
            var last = oldBucket[oldBucket.Count - 1];
            oldBucket[slot] = last; slots[last.Id] = slot; oldBucket.RemoveAt(oldBucket.Count - 1);
            var bucket = Bucket(newKey.X, newKey.Y); slots[enemy.Id] = bucket.Count; bucket.Add(enemy);
        }

        internal void Collect(Position origin, int range, List<EnemyState> result)
        {
            result.Clear();
            var squaredRange = (long)range * range;
            var min = Key(new(origin.X - range, origin.Y - range));
            var max = Key(new(origin.X + range, origin.Y + range));
            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    var bucket = FindBucket(x, y); if (bucket == null) { continue; }
                    foreach (var enemy in bucket)
                    { if (enemy.Health > 0 && origin.DistanceSquared(enemy.Position) <= squaredRange) { result.Add(enemy); } }
                }
            }
        }

        internal bool Clear(EnemyState enemy, Position candidate, int range,
            Dictionary<int, (int Width, bool Charging)> bodies, int numerator, int denominator)
        {
            var width = bodies[enemy.Id].Width;
            var squaredRange = (long)range * range;
            var min = Key(new(candidate.X - range, candidate.Y - range));
            var max = Key(new(candidate.X + range, candidate.Y + range));
            for (var x = min.X; x <= max.X; x++)
            {
                for (var y = min.Y; y <= max.Y; y++)
                {
                    var bucket = FindBucket(x, y); if (bucket == null) { continue; }
                    foreach (var other in bucket)
                    {
                        if (other.Health <= 0 || other.Id == enemy.Id || !bodies.TryGetValue(other.Id, out var body)
                            || other.Id > enemy.Id && !body.Charging) { continue; }
                        var distance = candidate.DistanceSquared(other.Position);
                        if (distance > squaredRange) { continue; }
                        var minimum = (numerator * (width + body.Width) + denominator - 1) / denominator + 1;
                        if (distance < (long)minimum * minimum) { return false; }
                    }
                }
            }
            return true;
        }
    }
}
