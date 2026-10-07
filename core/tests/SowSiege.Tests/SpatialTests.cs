using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class SpatialTests
{
    [Fact]
    public void MovingAcrossCellsUpdatesSubsequentCollisionQueries()
    {
        var enemy = new EnemyState { Id = 1, Health = 10, Position = new(0, 0) };
        var spatial = new SpatialHash(10);
        spatial.Rebuild([enemy]);
        spatial.Move(enemy, new(20, 0));
        Assert.Same(enemy, Assert.Single(spatial.Query(new(20, 0), 1)));
        Assert.Empty(spatial.Query(new(0, 0), 1));
    }

    [Fact]
    public void QueryReturnsBoundaryNeighborOnceAndExcludesDistantEnemy()
    {
        var near = new EnemyState { Id = 1, Health = 10, Position = new(11, 0) };
        var far = new EnemyState { Id = 2, Health = 10, Position = new(15, 0) };
        var spatial = new SpatialHash(10);
        spatial.Rebuild([near, far]);
        Assert.Same(near, Assert.Single(spatial.Query(new(9, 0), 2)));
    }
}
