using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class FieldRulesTests
{
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
    private static InteractiveOptions Options(ContentCatalog c, FieldOptions? field = null, FeelExperimentOptions? feel = null, string? target = null) =>
        new(new(30001, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", ManualCards: true, TargetMaterial: target), AimMode.Movement, new string('a', 64), feel, field);
    private static ContentCatalog NoSpawns(ContentCatalog c) => c with
    {
        WaveRuntime = c.WaveRuntime! with
        { BossSpawnTick = 100000, Enemies = c.WaveRuntime!.Enemies.ToDictionary(p => p.Key, p => p.Value with { FirstSpawnTick = 100000 }) }
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void FieldFormatFourRoundTripsFeelFlagsAndMaterial(int flags)
    {
        foreach (var target in new string?[] { null, "meta:grain" })
        {
            var catalog = Catalog();
            var feel = new FeelExperimentOptions((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
            var options = Options(catalog, new(), feel, target); var session = new InteractiveSession(catalog, options);
            using var bytes = new MemoryStream(); var header = ReplayCodec.Header(options);
            Assert.Equal(4, header.FormatVersion); Assert.Equal(1, header.RulesVersion); Assert.Equal(1, header.StateCodecVersion);
            ReplayCodec.WriteHeader(bytes, header);
            for (var tick = 0; tick < 30; tick++)
            {
                var command = new ReplayCommand(tick, tick, ReplayCommandKind.Advance, new(-1000, 0));
                session.Apply(command); ReplayCodec.WriteCommand(bytes, command);
            }
            ReplayCodec.WriteEnd(bytes, new(30, 30, ReplayEndKind.Quit, session.ComputeStateHash()));
            bytes.Position = 0; var replay = ReplayCodec.Read(bytes);
            Assert.Equal(new FieldOptions(), replay.Header.Options.Field);
            Assert.Equal(target, replay.Header.Options.Run.TargetMaterial);
            Assert.Equal(FeelExperimentOptions.Normalize(feel), replay.Header.Options.Feel);
            Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, replay).StateHash);
            Assert.Equal(30001, session.View.CaptureFrame().Field!.Seed);
        }
    }

    [Fact]
    public void FieldFormatRejectsWrongVersionAndTopologyMismatch()
    {
        var catalog = Catalog(); var options = Options(catalog, new());
        using var bytes = new MemoryStream();
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog, options with { Field = new(2) }));
        Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options) with { FormatVersion = 1 }));
        Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options) with { Options = options with { Field = null } }));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog with { WaveRuntime = null }, options));
        ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options));
        var data = bytes.ToArray(); data[^1] = 1;
        Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(new MemoryStream(data)));
    }

    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(1000, 0)]
    [InlineData(0, -1000)]
    [InlineData(0, 1000)]
    public void FieldMovementCrossesEveryOldEdgeAndKeepsHome(short x, short y)
    {
        var catalog = NoSpawns(Catalog());
        var session = new InteractiveSession(catalog, Options(catalog, new(), new(FasterLord: true)));
        var home = session.Simulation.World.Estate;
        for (var tick = 0; tick < 240; tick++) { session.Apply(new(tick, tick, ReplayCommandKind.Advance, new(x, y))); }
        var lord = session.View.CaptureFrame().Lord.Position;
        if (x < 0) { Assert.True(lord.X < 0); } else if (x > 0) { Assert.True(lord.X > 12000); }
        if (y < 0) { Assert.True(lord.Y < 0); } else if (y > 0) { Assert.True(lord.Y > 12000); }
        Assert.Equal(home, session.Simulation.World.Estate);
        Assert.Equal(x, session.View.CaptureFrame().Field!.LastTravelDirection.X);
        Assert.Equal(y, session.View.CaptureFrame().Field!.LastTravelDirection.Y);
    }

    [Fact]
    public void FieldStateChangesHashAndNullRetainsLegacyTopology()
    {
        var catalog = Catalog(); var legacy = new InteractiveSession(catalog, Options(catalog));
        Assert.Null(legacy.View.CaptureFrame().Field); Assert.Equal(1, ReplayCodec.Header(legacy.Options).FormatVersion);
        var field = new InteractiveSession(catalog, Options(catalog, new()));
        var hash = field.ComputeStateHash(); field.State.Field!.Travel = new(-1, 0); Assert.NotEqual(hash, field.ComputeStateHash());
        hash = field.ComputeStateHash(); field.State.Field.OpenArena(40, new(1, 2)); Assert.NotEqual(hash, field.ComputeStateHash());
    }

    [Fact]
    public void FieldSpawnIsOutsideCanonicalEnvelopeAndRecycleAhead()
    {
        var field = new FieldState(new(), 17); var origin = new Position(-200000, 300000);
        for (var direction = 0; direction < 16; direction++)
        { Assert.True(origin.DistanceSquared(field.Spawn(origin, direction)) >= (long)FieldRules.SpawnRadius * FieldRules.SpawnRadius); }
        field.AcceptInput(new(-1000, 0)); field.AcceptInput(default);
        for (var id = 1; id <= 1000; id++)
        {
            var recycled = field.Recycle(origin, 100, id);
            Assert.True(recycled.X <= origin.X);
            Assert.Equal(recycled, field.Recycle(origin, 100, id));
        }
    }

    [Fact]
    public void FieldRecyclingPreservesIdentityHealthCooldownAndLedgers()
    {
        var catalog = NoSpawns(Catalog()); var session = new InteractiveSession(catalog, Options(catalog, new()));
        var world = session.Simulation.World; var state = world.WaveRuntime!;
        var id = catalog.WaveRuntime!.Enemies.Values.First(e => e.Kind == WaveEnemyKind.Charger).Id;
        var enemy = new EnemyState { Id = 900, Definition = id, Health = 17, Position = new(-100000, 0), AttackTick = 456 };
        world.Enemies.Add(enemy); state.EnemyActions.Add(enemy.Id, new() { Phase = "tell-charge", UntilTick = 123, StopUntil = 99, TargetId = 100, Target = world.Lord });
        var system = new WaveEnemySystem(catalog, world, new(7, true), session.State);
        system.FinishFieldTick();
        Assert.Same(enemy, Assert.Single(world.Enemies)); Assert.Equal(900, enemy.Id); Assert.Equal(17, enemy.Health); Assert.Equal(456, enemy.AttackTick);
        Assert.Equal(123, state.EnemyActions[900].UntilTick); Assert.Equal(99, state.EnemyActions[900].StopUntil);
        Assert.Equal("recovery", state.EnemyActions[900].Phase); Assert.Equal(-1, state.EnemyActions[900].TargetId);
        Assert.Equal(0, world.SpawnedEnemies); Assert.Equal(0, world.Experience); Assert.Empty(state.Rewards);
        Assert.Single(state.Events, e => e.Kind == "enemy-relocated");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldRemoteContactProtectionExpiresOnDistanceOrCropState(bool changeCrop)
    {
        var catalog = NoSpawns(Catalog()); var session = new InteractiveSession(catalog, Options(catalog, new()));
        var world = session.Simulation.World; var state = world.WaveRuntime!;
        var rule = catalog.WaveRuntime!.Enemies.Values.First(e => e.Kind == WaveEnemyKind.SeedThief);
        var position = new Position(-100000, -100000);
        var enemy = new EnemyState { Id = 901, Definition = rule.Id, Health = 17, Position = position };
        var crop = new WaveWork { Id = 902, Kind = "grain", Health = 1, Position = position, Complete = false };
        world.Enemies.Add(enemy); state.Work.Add(crop); state.EnemyActions.Add(901, new() { Phase = "tell-strike", TargetId = 902, UntilTick = 300 });
        var system = new WaveEnemySystem(catalog, world, new(7, true), session.State);
        system.FinishFieldTick(); Assert.Equal(position, enemy.Position); Assert.Empty(state.Events);
        if (changeCrop) { crop.Complete = true; } else { enemy.Position = new(position.X + catalog.Enemies[rule.Id].Range + 1, position.Y); }
        system.FinishFieldTick(); Assert.True(enemy.Position.DistanceSquared(world.Lord) < (long)FieldRules.RecycleRadius * FieldRules.RecycleRadius);
        Assert.Single(state.Events, e => e.Kind == "enemy-relocated");
    }

    [Fact]
    public void FieldArenaProjectionAndReleaseAreDeterministic()
    {
        var field = new FieldState(new(), 7); var center = new Position(-1000000, 1000000); field.OpenArena(100, center);
        for (var i = -20; i <= 20; i++)
        {
            var point = new Position(center.X + i * 900, center.Y - i * 733);
            var projected = field.Constrain(point);
            Assert.True(projected.DistanceSquared(center) <= (long)FieldRules.ArenaRadius * FieldRules.ArenaRadius);
            Assert.Equal(projected, field.Constrain(projected));
        }
        var outside = new Position(-1200000, 1200000); field.CloseArena(); Assert.Equal(outside, field.Constrain(outside));
    }

    [Fact]
    public void FieldSignedSpatialQueriesMatchBruteForceAndRecycleEmptyBuckets()
    {
        var enemies = Enumerable.Range(0, 121).Select(i => new EnemyState { Id = i, Health = 1, Position = new(i % 11 * 55 - 275, i / 11 * 55 - 275) }).ToArray();
        var spatial = new SpatialHash(60, signed: true); spatial.Rebuild(enemies); var found = new List<EnemyState>();
        foreach (var point in new[] { new Position(-1, -1), new Position(-60, -60), new Position(0, 0), new Position(61, -61) })
        {
            var expected = enemies.Where(e => e.Position.DistanceSquared(point) <= 130 * 130).Select(e => e.Id).Order().ToArray();
            spatial.Collect(point, 130, found); Assert.Equal(expected, found.Select(e => e.Id).Order().ToArray());
            Assert.Equal(expected, spatial.Query(point, 130).Select(e => e.Id).Order().ToArray());
        }
        var traveler = new[] { enemies[0] };
        for (var tick = 0; tick < 1000; tick++) { traveler[0].Position = new(-tick * 1000, tick * 1000); spatial.Rebuild(traveler); Assert.Equal(1, spatial.BucketCount); }
    }

    [Fact]
    public void FieldGeometryUsesLongBeforeSubtraction()
    {
        var first = new Position(-2000000, -2000000); var second = new Position(2000000, 2000000);
        Assert.Equal(32000000000000L, first.DistanceSquared(second));
        Assert.Equal(new Position(-1999963, -1999963), first.MoveToward(second, 37));
        var field = new FieldState(new(), 1); var feel = new FeelExperimentState(new(FasterLord: true));
        var catalog = Catalog(); var lord = new Position(6000, 6000);
        for (var tick = 0; tick < 27000; tick++) { lord = feel.Move(lord, new(1000, 1000), 28, catalog.Tuning.World.Map, unbounded: true); }
        Assert.Equal(new Position(988800, 988800), lord);
    }
    [Fact]
    public void FieldWorksWithDifferentHeroAndEstateIds()
    {
        var catalog = NoSpawns(Catalog());
        var hero = catalog.Heroes[catalog.Tuning.DefaultHero] with { Id = "test:field_traveler" };
        var estate = catalog.Estates[catalog.Tuning.DefaultEstate] with { Id = "test:field_home" };
        catalog = catalog with { Heroes = new Dictionary<string, HeroDefinition> { [hero.Id] = hero }, Estates = new Dictionary<string, EstateDefinition> { [estate.Id] = estate } };
        var options = Options(catalog, new()); options = options with { Run = options.Run with { HeroId = hero.Id, EstateId = estate.Id } };
        var session = new InteractiveSession(catalog, options);
        session.Apply(new(0, 0, ReplayCommandKind.Advance, new(-1000, 0)));
        Assert.Equal(FieldRules.Version, session.View.CaptureFrame().Field!.Version);
        Assert.Equal(5976, session.View.CaptureFrame().Lord.Position.X);
    }

    [Fact]
    public void FieldPlantingPersistsAtTravelPositionWithoutMovingHome()
    {
        var catalog = NoSpawns(Catalog()); var session = new InteractiveSession(catalog, Options(catalog, new()));
        var world = session.Simulation.World; var home = world.Estate; world.Lord = new(-20000, -30000);
        var gear = catalog.WaveRuntime!.Gear.Values.First(g => g.Kind == WaveAttackKind.SeedFan);
        var work = new WaveWorkSystem(catalog, world, session.State);
        work.Activate(gear, world.Lord, new(-1, 0));
        var planted = Assert.Single(world.WaveRuntime!.Work); var position = planted.Position;
        Assert.True(position.X < -20000); Assert.True(position.Y < 0);
        world.Lord = new(100000, 100000); work.Tick();
        Assert.Equal(position, planted.Position); Assert.Equal(home, world.Estate);
    }

    [Fact]
    public void FieldBossOpensArenaAtTravelPositionAndDeathRemovesIt()
    {
        var original = NoSpawns(Catalog()); var catalog = original with { WaveRuntime = original.WaveRuntime! with { BossSpawnTick = 0 } };
        var session = new InteractiveSession(catalog, Options(catalog, new())); var world = session.Simulation.World;
        world.Lord = new(-50000, 75000);
        session.Apply(new(0, 0, ReplayCommandKind.Advance));
        var arena = Assert.IsType<FieldArenaFrame>(session.View.CaptureFrame().Field!.Arena);
        Assert.Equal(-50000, arena.Center.X); Assert.Equal(75000, arena.Center.Y);
        var boss = Assert.Single(world.Enemies); Assert.Equal(boss.Id, arena.BossId);
        Assert.True(boss.Position.DistanceSquared(world.Lord) <= (long)arena.Radius * arena.Radius);
        var tell = world.WaveRuntime!.EnemyActions[boss.Id];
        Assert.Equal("tell-water", tell.Phase); Assert.True(tell.Origin.X < arena.Center.X); Assert.True(tell.Target.X > arena.Center.X);
        boss.Health = 0;
        session.Apply(new(1, 1, ReplayCommandKind.Advance));
        Assert.True(world.WaveRuntime.BossDefeated); Assert.Null(session.View.CaptureFrame().Field!.Arena);
    }

    [Fact]
    public void FieldSeparationHandlesFullCapAtNegativeCoordinates()
    {
        var catalog = Catalog(); var world = new WorldState { WaveRuntime = new() }; var field = new FieldState(new(), 17);
        var widths = catalog.WaveRuntime!.EnemySeparation!;
        var id = widths.BodyWidths.First().Key;
        for (var index = 0; index < catalog.Tuning.World.Threat.EnemyCap; index++)
        { world.Enemies.Add(new() { Id = index, Definition = id, Health = 1, Position = new(-1100000, -1100000) }); }
        new WaveEnemySeparation(world, catalog.Tuning.World.Map, widths, field).Resolve(world.Enemies);
        Assert.All(world.Enemies, enemy => { Assert.InRange(enemy.Position.X, -2000000, 2000000); Assert.InRange(enemy.Position.Y, -2000000, 2000000); });
        Assert.Equal(world.Enemies.Count, world.Enemies.Select(e => e.Position).Distinct().Count());
    }

}
