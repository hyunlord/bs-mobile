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
        private readonly List<long> emptyCells = new();

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
                    value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
                    value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
                    value ^= value >> 31;
                    return (int)(value ^ (value >> 32));
                }
            }
        }

        internal WaveSeparationGrid(int cellSize, bool signed)
        { this.cellSize = cellSize; this.signed = signed; }

        private int Cell(int value) => signed && value < 0 ? (int)(((long)value - cellSize + 1) / cellSize) : value / cellSize;
        private (int X, int Y) Key(Position p) => (Cell(p.X), Cell(p.Y));
        private static long Pack(int x, int y) => ((long)x << 32) | (uint)y;
        private List<EnemyState> Bucket(long key)
        {
            if (cells.TryGetValue(key, out var bucket)) { return bucket; }
            bucket = freeBuckets.Count == 0 ? new() : freeBuckets.Pop();
            cells.Add(key, bucket); return bucket;
        }

        internal void Rebuild(List<EnemyState> enemies)
        {
            foreach (var bucket in cells.Values) { bucket.Clear(); }
            slots.Clear();
            foreach (var enemy in enemies)
            {
                if (enemy.Health <= 0) { continue; }
                var key = Key(enemy.Position); var bucket = Bucket(Pack(key.X, key.Y));
                slots.Add(enemy.Id, bucket.Count); bucket.Add(enemy);
            }
            emptyCells.Clear();
            foreach (var cell in cells) { if (cell.Value.Count == 0) { emptyCells.Add(cell.Key); } }
            foreach (var key in emptyCells) { freeBuckets.Push(cells[key]); cells.Remove(key); }
        }

        internal void Move(EnemyState enemy, Position destination)
        {
            var oldKey = Key(enemy.Position); var newKey = Key(destination);
            enemy.Position = destination;
            if (oldKey == newKey) { return; }
            var oldBucket = cells[Pack(oldKey.X, oldKey.Y)]; var slot = slots[enemy.Id];
            var last = oldBucket[oldBucket.Count - 1];
            oldBucket[slot] = last; slots[last.Id] = slot; oldBucket.RemoveAt(oldBucket.Count - 1);
            var bucket = Bucket(Pack(newKey.X, newKey.Y)); slots[enemy.Id] = bucket.Count; bucket.Add(enemy);
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
                    if (!cells.TryGetValue(Pack(x, y), out var bucket)) { continue; }
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
                    if (!cells.TryGetValue(Pack(x, y), out var bucket)) { continue; }
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
