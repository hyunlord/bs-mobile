using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WaveSeparationGridTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedCellMovesKeepTheSameCapturedCandidates(bool signed)
    {
        var enemies = Enumerable.Range(1, 80).Select(id => new EnemyState
        { Id = id, Health = 10, Position = new((id % 9 - (signed ? 5 : 0)) * 17, (id % 7) * 13) }).ToList();
        var grid = new WaveSeparationGrid(31, signed);
        grid.Rebuild(enemies);
        var actual = new List<EnemyState>();
        for (var iteration = 0; iteration < 160; iteration++)
        {
            var enemy = enemies[iteration % enemies.Count];
            grid.Move(enemy, new(enemy.Position.X + (iteration % 2 == 0 ? 37 : -29), enemy.Position.Y - 41));
            var center = enemy.Position;
            grid.Collect(center, 83, actual);
            var captured = actual.OrderBy(e => e.Id).ToArray();
            Assert.Equal(enemies.Where(e => e.Health > 0 && center.DistanceSquared(e.Position) <= 83L * 83).Select(e => e.Id).OrderBy(id => id), captured.Select(e => e.Id));
            // A later move changes references' positions, never captured membership.
            var ids = captured.Select(e => e.Id).ToArray();
            grid.Move(enemy, new(enemy.Position.X + 200, enemy.Position.Y));
            Assert.Equal(ids, captured.Select(e => e.Id));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5000, 5000)]
    [InlineData(-7000, -9000)]
    public void DenseMixedBodiesExactlyMatchFrozenV1AfterEveryResolve(int x, int y)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        var definition = catalog.WaveRuntime!.EnemySeparation!;
        var names = definition.BodyWidths.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        WorldState Make()
        {
            var world = new WorldState { WaveRuntime = new() };
            for (var id = 160; id >= 1; id--)
            {
                world.Enemies.Add(new() { Id = id, Definition = id % 29 == 0 ? catalog.WaveRuntime.BossId : names[id % names.Length],
                    Health = id % 23 == 0 ? 0 : 100, Position = new(x + id % 4, y + id % 3) });
                world.WaveRuntime.EnemyActions[id] = new() { Phase = id % 17 == 0 ? "charge" : "tell-strike", UntilTick = 2, Origin = new(x, y), Target = new(x + 700, y + 700) };
            }
            return world;
        }
        var expected = Make(); var actual = Make();
        var reference = new ReferenceWaveEnemySeparation(expected, catalog.Tuning.World.Map, definition, x < 0 ? new FieldState(new(), 17) : null);
        var optimized = new WaveEnemySeparation(actual, catalog.Tuning.World.Map, definition, x < 0 ? new FieldState(new(), 17) : null);
        for (var tick = 0; tick < 5; tick++)
        {
            expected.Tick = actual.Tick = tick;
            reference.Resolve(expected.Enemies); optimized.Resolve(actual.Enemies);
            Assert.Equal(expected.Enemies.Select(e => (e.Id, e.Position, e.Health)), actual.Enemies.Select(e => (e.Id, e.Position, e.Health)));
            Assert.Equal(expected.WaveRuntime!.EnemyActions.OrderBy(p => p.Key).Select(p => (p.Key, p.Value.Origin, p.Value.Target)),
                actual.WaveRuntime!.EnemyActions.OrderBy(p => p.Key).Select(p => (p.Key, p.Value.Origin, p.Value.Target)));
            Assert.Equal(expected.WaveRuntime.Counters.OrderBy(p => p.Key), actual.WaveRuntime.Counters.OrderBy(p => p.Key));
        }
    }
}
