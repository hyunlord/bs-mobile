using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WaveEnemyQueriesTests
{
    [Fact]
    public void NearestRetainsSortedFirstAcrossSignedCellsTiesMovementAndDeaths()
    {
        var world = new WorldState();
        for (var id = 31; id >= 0; id--)
        {
            world.Enemies.Add(new EnemyState { Id = id, Health = 10, Position = new((id % 8 - 4) * 17, (id / 8 - 2) * 17) });
        }
        var queries = new WaveEnemyQueries(world, 16, signed: true);
        queries.Rebuild();
        var sorted = new List<EnemyState>();
        var scratch = new List<EnemyState>();
        void Compare()
        {
            foreach (var origin in new[] { new Position(0, 0), new Position(-31, 12), new Position(1000, 1000) })
            {
                foreach (var rank in new[] { origin, new Position(11, -19) })
                {
                    foreach (var range in new[] { 0, 17, 80 })
                    {
                        queries.ByDistance(sorted, origin, range, rank);
                        Assert.Same(sorted.FirstOrDefault(), queries.Nearest(scratch, origin, range, rank));
                    }
                }
            }
        }
        Compare();
        queries.Move(world.Enemies[0], new(-17, 0));
        world.Enemies[1].Health = 0;
        Compare();
        world.Enemies.Clear(); queries.Rebuild(); Compare();
    }

    [Fact]
    public void CandidateOrdersRetainDistanceIdAndWorldOrdinalSeparately()
    {
        var world = new WorldState();
        var first = new EnemyState { Id = 20, Health = 1, Position = new(150, 100) };
        var second = new EnemyState { Id = 10, Health = 1, Position = new(50, 100) };
        world.Enemies.Add(first); world.Enemies.Add(second);
        var queries = new WaveEnemyQueries(world, 32); queries.Rebuild();
        var candidates = new List<EnemyState>();
        queries.ByDistance(candidates, new(100, 100), 50, new(100, 100));
        Assert.Equal(new[] { 10, 20 }, candidates.Select(e => e.Id));
        queries.ByWorldOrder(candidates, new(100, 100), 50);
        Assert.Equal(new[] { 20, 10 }, candidates.Select(e => e.Id));
        queries.ByDistance(candidates, new(100, 100), 50, new(200, 100));
        Assert.Equal(new[] { 20, 10 }, candidates.Select(e => e.Id));
    }

    [Fact]
    public void LegacyDefinitionExpansionInvalidatesPrivateOperationCaches()
    {
        var (catalog, _, _, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var definition = catalog.WaveRuntime!;
        var modules = new WavePrimitiveModules(definition);
        var gear = definition.Gear.Values.Single();
        Assert.Equal("melee-fan", modules.Attack(gear.Id).Shape);
        var definitions = (Dictionary<string, WaveGearDefinition>)definition.Gear;
        definitions[gear.Id] = gear with { Kind = WaveAttackKind.Chain };
        definitions.Add("test:added", gear with { Id = "test:added" });
        Assert.Equal("melee-fan", modules.Attack("test:added").Shape);
        Assert.Equal("chain", modules.Attack(gear.Id).Shape);
        Assert.True(modules.Attack(gear.Id).WetPriority);
        var enemy = definition.Enemies.Values.Single();
        ((Dictionary<string, WaveEnemyDefinition>)definition.Enemies).Add("test:new-enemy", enemy with { Id = "test:new-enemy", Kind = WaveEnemyKind.Ranged });
        Assert.Equal("standoff", modules.Enemy("test:new-enemy").Movement);
    }

    [Fact]
    public void KnockbackReindexesImmediatelyAndDeadTargetsAreExcluded()
    {
        var world = new WorldState();
        var enemy = new EnemyState { Id = 7, Health = 10, Position = new(10, 10) };
        world.Enemies.Add(enemy);
        var queries = new WaveEnemyQueries(world, 32); queries.Rebuild();
        var candidates = new List<EnemyState>();
        queries.Move(enemy, new(110, 10));
        queries.ById(candidates, new(10, 10), 10); Assert.Empty(candidates);
        queries.ById(candidates, new(100, 10), 10); Assert.Same(enemy, Assert.Single(candidates));
        Assert.Same(enemy, queries.Find(7));
        enemy.Health = 0;
        queries.ById(candidates, new(100, 10), 10); Assert.Empty(candidates);
        Assert.Null(queries.Find(7));
        world.Enemies.Clear(); queries.Rebuild(); Assert.Null(queries.Find(7));
    }
}
