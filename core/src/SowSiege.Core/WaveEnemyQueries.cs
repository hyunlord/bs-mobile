using System;
using System.Collections.Generic;

namespace SowSiege.Core
{
    // Transient indexes are deliberately outside the serialized world and catalog.
    internal sealed class WaveEnemyQueries
    {
        private readonly WorldState world;
        private readonly SpatialHash spatial;
        private readonly Dictionary<int, EnemyState> byId = new();
        private readonly Dictionary<int, int> ordinal = new();
        private readonly DistanceComparer distance = new();
        private readonly Comparison<EnemyState> worldOrder;
        private static readonly Comparison<EnemyState> IdOrder = (a, b) => a.Id.CompareTo(b.Id);

        internal WaveEnemyQueries(WorldState world, int cellSize)
        {
            this.world = world;
            spatial = new(Math.Max(1, cellSize));
            worldOrder = (a, b) => ordinal[a.Id].CompareTo(ordinal[b.Id]);
        }
        internal void Rebuild()
        {
            spatial.Rebuild(world.Enemies);
            byId.Clear(); ordinal.Clear();
            for (var i = 0; i < world.Enemies.Count; i++)
            {
                var enemy = world.Enemies[i];
                byId.Add(enemy.Id, enemy); ordinal.Add(enemy.Id, i);
            }
        }
        internal EnemyState? Find(int id) => byId.TryGetValue(id, out var enemy) && enemy.Health > 0 ? enemy : null;
        internal void Move(EnemyState enemy, Position destination) => spatial.Move(enemy, destination);
        internal void Collect(List<EnemyState> result, Position origin, int range) => spatial.Collect(origin, range, result);
        internal void ById(List<EnemyState> result, Position origin, int range)
        { Collect(result, origin, range); result.Sort(IdOrder); }
        internal void ByWorldOrder(List<EnemyState> result, Position origin, int range)
        { Collect(result, origin, range); result.Sort(worldOrder); }
        internal void ByDistance(List<EnemyState> result, Position origin, int range, Position rankingOrigin)
        { Collect(result, origin, range); distance.Origin = rankingOrigin; result.Sort(distance); }

        private sealed class DistanceComparer : IComparer<EnemyState>
        {
            internal Position Origin;
            public int Compare(EnemyState? a, EnemyState? b)
            {
                var comparison = a!.Position.DistanceSquared(Origin).CompareTo(b!.Position.DistanceSquared(Origin));
                return comparison != 0 ? comparison : a.Id.CompareTo(b.Id);
            }
        }
    }
}
