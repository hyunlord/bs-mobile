using System;
using System.Collections.Generic;
using System.Linq;
namespace SowSiege.Core
{

    internal sealed class SpatialHash
    {
        private readonly int cellSize;
        private readonly bool signed;
        private readonly List<(int X, int Y)> emptyCells = new();
        private readonly Stack<List<EnemyState>> freeBuckets = new();
        internal int BucketCount => cells.Count;
        private int Cell(int value) => signed && value < 0 ? (int)(((long)value - cellSize + 1) / cellSize) : value / cellSize;
        private List<EnemyState> Bucket() => freeBuckets.Count > 0 ? freeBuckets.Pop() : new();

        public SpatialHash(int cellSize, bool signed = false)
        {
            this.cellSize = cellSize; this.signed = signed;
        }

        private readonly Dictionary<(int X, int Y), List<EnemyState>> cells = new();

        public void Rebuild(IEnumerable<EnemyState> enemies)
        {
            foreach (var bucket in cells.Values) { bucket.Clear(); }
            foreach (var enemy in enemies)
            {
                if (enemy.Health <= 0) { continue; }
                var key = (Cell(enemy.Position.X), Cell(enemy.Position.Y));
                if (!cells.TryGetValue(key, out var bucket)) { bucket = Bucket(); cells.Add(key, bucket); }
                bucket.Add(enemy);
            }
            if (signed)
            {
                emptyCells.Clear();
                foreach (var pair in cells) { if (pair.Value.Count == 0) { emptyCells.Add(pair.Key); } }
                foreach (var key in emptyCells) { var bucket = cells[key]; cells.Remove(key); freeBuckets.Push(bucket); }
            }
        }

        public void Move(EnemyState enemy, Position destination)
        {
            var oldKey = (Cell(enemy.Position.X), Cell(enemy.Position.Y));
            var newKey = (Cell(destination.X), Cell(destination.Y));
            enemy.Position = destination;
            if (oldKey == newKey) { return; }
            if (cells.TryGetValue(oldKey, out var oldBucket)) { oldBucket.Remove(enemy); }
            if (!cells.TryGetValue(newKey, out var bucket)) { bucket = Bucket(); cells.Add(newKey, bucket); }
            bucket.Add(enemy);
        }

        public void Collect(Position origin, int range, List<EnemyState> result)
        {
            result.Clear();
            var minX = Cell(origin.X - range);
            var minY = Cell(origin.Y - range);
            var maxX = Cell(origin.X + range);
            var maxY = Cell(origin.Y + range);
            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    if (!cells.TryGetValue((x, y), out var bucket)) { continue; }
                    foreach (var enemy in bucket)
                    {
                        if (enemy.Health > 0 && origin.DistanceSquared(enemy.Position) <= (long)range * range) { result.Add(enemy); }
                    }
                }
            }
        }

        public IEnumerable<EnemyState> Query(Position origin, int range)
        {
            var minX = signed ? Cell(origin.X - range) : Math.Max(0, origin.X - range) / cellSize;
            var minY = signed ? Cell(origin.Y - range) : Math.Max(0, origin.Y - range) / cellSize;
            var maxX = Cell(origin.X + range);
            var maxY = Cell(origin.Y + range);
            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    if (!cells.TryGetValue((x, y), out var bucket)) { continue; }
                    foreach (var enemy in bucket)
                    {
                        if (enemy.Health > 0 && origin.DistanceSquared(enemy.Position) <= (long)range * range) { yield return enemy; }
                    }
                }
            }
        }
    }

}
