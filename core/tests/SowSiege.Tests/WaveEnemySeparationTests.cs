using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SowSiege.Tests;

public sealed class WaveEnemySeparationTests
{
    private readonly ITestOutputHelper output;
    public WaveEnemySeparationTests(ITestOutputHelper output) { this.output = output; }
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15000, 10000)]
    public void DenseCoincidentCrowdHasNoPairOverlappingForOneSecond(int x, int y)
    {
        var catalog = Catalog();
        var map = catalog.Tuning.World.Map;
        var world = new WorldState { WaveRuntime = new() };
        var contract = catalog.WaveRuntime!.EnemySeparation!;
        var ids = contract.BodyWidths.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        for (var id = 0; id < 850; id++)
        {
            world.Enemies.Add(new() { Id = id + 1, Definition = ids[id % ids.Length], Position = new(Math.Min(x, map.Width), Math.Min(y, map.Height)), Health = 100 });
        }
        var separation = new WaveEnemySeparation(world, map, contract);
        var overlaps = new Dictionary<(int, int), int>();
        var maxDuration = 0;
        for (var tick = 0; tick < 90; tick++)
        {
            separation.Resolve(world.Enemies);
            for (var a = 0; a < world.Enemies.Count; a++)
            {
                for (var b = a + 1; b < world.Enemies.Count; b++)
                {
                    var first = world.Enemies[a]; var second = world.Enemies[b];
                    var sum = contract.BodyWidths[first.Definition] + contract.BodyWidths[second.Definition];
                    var key = (first.Id, second.Id);
                    var count = first.Position.DistanceSquared(second.Position) * 100 < (long)sum * sum * 9 ? overlaps.GetValueOrDefault(key) + 1 : 0;
                    overlaps[key] = count; maxDuration = Math.Max(maxDuration, count);
                }
            }
        }
        output.WriteLine($"850 nonboss actors; origin={x},{y}; ticks=90; maximum consecutive overlap={maxDuration}; unresolved={world.WaveRuntime!.Counters.GetValueOrDefault("enemy-separation-unresolved")}");
        Assert.Equal(0, world.WaveRuntime!.Counters.GetValueOrDefault("enemy-separation-unresolved"));
        Assert.True(maxDuration <= 30, $"Persistent overlap lasted {maxDuration} ticks.");
        Assert.All(world.Enemies, enemy => { Assert.InRange(enemy.Position.X, 0, map.Width); Assert.InRange(enemy.Position.Y, 0, map.Height); });
    }

    [Fact]
    public void TellOriginTracksCorrectionAndChargeAndBossRemainUnmoved()
    {
        var catalog = Catalog(); var contract = catalog.WaveRuntime!.EnemySeparation!;
        var world = new WorldState { WaveRuntime = new() };
        var charger = catalog.WaveRuntime.Enemies.Values.Single(e => e.Kind == WaveEnemyKind.Charger).Id;
        var boss = catalog.WaveRuntime.BossId;
        world.Enemies.Add(new() { Id = 1, Definition = charger, Position = new(5000, 5000), Health = 100 });
        world.Enemies.Add(new() { Id = 2, Definition = charger, Position = new(5000, 5000), Health = 100 });
        world.Enemies.Add(new() { Id = 3, Definition = boss, Position = new(5000, 5000), Health = 100 });
        world.WaveRuntime.EnemyActions[1] = new() { Phase = "tell-charge", Origin = new(5000, 5000), Target = new(6000, 5000) };
        world.WaveRuntime.EnemyActions[2] = new() { Phase = "charge", Origin = new(5000, 5000), Target = new(6000, 5000), UntilTick = 30 };
        new WaveEnemySeparation(world, catalog.Tuning.World.Map, contract).Resolve(world.Enemies);
        Assert.Equal(world.Enemies[0].Position, world.WaveRuntime.EnemyActions[1].Origin);
        Assert.Equal(new Position(6000, 5000), world.WaveRuntime.EnemyActions[1].Target);
        Assert.Equal(new Position(5000, 5000), world.Enemies[1].Position);
        Assert.Equal(new Position(5000, 5000), world.Enemies[2].Position);
    }

    [Fact]
    public void RepeatedStopCannotPinExpiredChargesOrResumeAStaleHit()
    {
        var catalog = Catalog();
        var rule = catalog.WaveRuntime!.Enemies.Values.Single(e => e.Kind == WaveEnemyKind.Charger);
        catalog = catalog with { WaveRuntime = catalog.WaveRuntime with { BossSpawnTick = 100000, Enemies = catalog.WaveRuntime.Enemies.ToDictionary(pair => pair.Key, pair => pair.Value with { FirstSpawnTick = 100000 }) } };
        var world = new WorldState { Lord = new(5060, 5000), LordHealth = 1000, WaveRuntime = new() };
        for (var id = 1; id <= 2; id++)
        {
            world.Enemies.Add(new() { Id = id, Definition = rule.Id, Position = new(5000, 5000), Health = 100 });
            world.WaveRuntime.EnemyActions[id] = new() { Phase = "charge", Origin = new(5000, 5000), Target = world.Lord, UntilTick = rule.ActiveTicks, StopUntil = rule.ActiveTicks + 60 };
        }
        var system = new WaveEnemySystem(catalog, world, new(7, true), null);
        var maximumOverlap = 0; var consecutive = 0;
        var width = catalog.WaveRuntime.EnemySeparation!.BodyWidths[rule.Id];
        for (var tick = 0; tick < rule.ActiveTicks + 60; tick++)
        {
            world.Tick = tick;
            foreach (var action in world.WaveRuntime.EnemyActions.Values) { action.StopUntil = tick + catalog.Tuning.TickRate; }
            system.Tick();
            consecutive = world.Enemies[0].Position.DistanceSquared(world.Enemies[1].Position) * 100 < (long)width * width * 36 ? consecutive + 1 : 0;
            maximumOverlap = Math.Max(maximumOverlap, consecutive);
            Assert.Equal(1000, world.LordHealth);
        }
        Assert.True(maximumOverlap <= catalog.Tuning.TickRate);
        Assert.All(world.WaveRuntime.EnemyActions.Values, action => Assert.Equal("recovery", action.Phase));
        var positions = world.Enemies.Select(enemy => enemy.Position).ToArray();
        world.Tick = rule.ActiveTicks + 60;
        foreach (var action in world.WaveRuntime.EnemyActions.Values) { action.StopUntil = world.Tick; }
        system.Tick();
        Assert.All(world.WaveRuntime.EnemyActions.Values, action => Assert.Equal("tell-charge", action.Phase));
        Assert.Equal(positions, world.Enemies.Select(enemy => enemy.Position));
        Assert.Equal(1000, world.LordHealth);
    }

    [Fact]
    public void IdOrderAndCornerResidualAreIndependentOfInsertionOrder()
    {
        var catalog = Catalog(); var contract = catalog.WaveRuntime!.EnemySeparation!;
        var id = contract.BodyWidths.Keys.First();
        WorldState Make(bool reverse)
        {
            var world = new WorldState { WaveRuntime = new() };
            foreach (var number in reverse ? new[] { 2, 1 } : new[] { 1, 2 })
            {
                world.Enemies.Add(new() { Id = number, Definition = id, Position = new(0, 0), Health = 100 });
            }
            new WaveEnemySeparation(world, catalog.Tuning.World.Map, contract).Resolve(world.Enemies);
            return world;
        }
        var first = Make(false); var reversed = Make(true);
        Assert.Equal(first.Enemies.Select(e => e.Position), reversed.Enemies.Select(e => e.Position));
        var minimum = contract.BodyWidths[id] * 6 / 10;
        Assert.True(first.Enemies[0].Position.DistanceSquared(first.Enemies[1].Position) >= (long)minimum * minimum);
    }

    [Fact]
    public void ExhaustedMapKeepsRunningAndRecordsUnresolvedOverlap()
    {
        var catalog = Catalog(); var contract = catalog.WaveRuntime!.EnemySeparation!;
        var id = contract.BodyWidths.Keys.First();
        var world = new WorldState { WaveRuntime = new() };
        world.Enemies.Add(new() { Id = 1, Definition = id, Position = new(0, 0), Health = 100 });
        world.Enemies.Add(new() { Id = 2, Definition = id, Position = new(0, 0), Health = 100 });
        new WaveEnemySeparation(world, catalog.Tuning.World.Map with { Width = 1, Height = 1 }, contract).Resolve(world.Enemies);
        Assert.True(world.WaveRuntime.Counters["enemy-separation-unresolved"] > 0);
        Assert.All(world.Enemies, enemy => Assert.Equal(100, enemy.Health));
    }

    [Fact]
    public void OptionalContractChangesHashWithoutChangingOtherFields()
    {
        var current = Catalog();
        var historical = current with { WaveRuntime = current.WaveRuntime! with { EnemySeparation = null } };
        output.WriteLine($"Current catalog hash: {PortableStateCodec.HashCatalog(current)}; absent-contract catalog hash: {PortableStateCodec.HashCatalog(historical)}");
        Assert.Equal("D108BA8D527BFE93A00C5FB78111AE2B8D4F53B4047CEA269AFE78D551CFC58E", PortableStateCodec.HashCatalog(historical));
        Assert.NotEqual(PortableStateCodec.HashCatalog(current), PortableStateCodec.HashCatalog(historical));
        Assert.Equal(current.Enemies, historical.Enemies); Assert.Equal(current.Tuning, historical.Tuning);
    }
}
