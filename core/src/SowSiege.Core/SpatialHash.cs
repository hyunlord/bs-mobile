namespace SowSiege.Core;

internal sealed class SpatialHash(int cellSize)
{
    private readonly Dictionary<(int X, int Y), List<EnemyState>> cells = [];

    public void Rebuild(IEnumerable<EnemyState> enemies)
    {
        foreach (var bucket in cells.Values) { bucket.Clear(); }
        foreach (var enemy in enemies)
        {
            if (enemy.Health <= 0) { continue; }
            var key = (enemy.Position.X / cellSize, enemy.Position.Y / cellSize);
            if (!cells.TryGetValue(key, out var bucket)) { bucket = []; cells.Add(key, bucket); }
            bucket.Add(enemy);
        }
    }

    public void Move(EnemyState enemy, Position destination)
    {
        var oldKey = (enemy.Position.X / cellSize, enemy.Position.Y / cellSize);
        var newKey = (destination.X / cellSize, destination.Y / cellSize);
        enemy.Position = destination;
        if (oldKey == newKey) { return; }
        if (cells.TryGetValue(oldKey, out var oldBucket)) { oldBucket.Remove(enemy); }
        if (!cells.TryGetValue(newKey, out var bucket)) { bucket = []; cells.Add(newKey, bucket); }
        bucket.Add(enemy);
    }

    public IEnumerable<EnemyState> Query(Position origin, int range)
    {
        var minX = Math.Max(0, origin.X - range) / cellSize;
        var minY = Math.Max(0, origin.Y - range) / cellSize;
        var maxX = (origin.X + range) / cellSize;
        var maxY = (origin.Y + range) / cellSize;
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
