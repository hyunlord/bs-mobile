using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;
public sealed class WaveReviewCompositionTests
{
    [Fact]
    public void ShowcaseWalkingInputUsesIdenticalClosedSquareForAllStages()
    {
        var x = 0; var y = 0;
        for (var tick = 0; tick < 120; tick++)
        {
            var input = ShowcaseFixtureCli.WalkingInput(tick);
            Assert.Equal(PlayerInput.Scale, Math.Abs(input.X) + Math.Abs(input.Y));
            x += input.X; y += input.Y;
            Assert.Equal(input, ShowcaseFixtureCli.WalkingInput(tick + 120));
        }
        Assert.Equal(0, x); Assert.Equal(0, y);
    }
    [Fact]
    public void ReviewFactoryIsImmutableAndHashesStageAndRealDamage()
    {
        var baseline = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
        var originalHash = ReviewShowcaseCatalog.Hash(baseline);
        var id = "core:iron_blade";
        var reference = "data/review/m1-weapon-combat-cases.json#" + id + "/max";
        var gear = baseline.WaveRuntime!.Gear[id] with { DesignRef = reference, Aim = "nearest-enemy", Range = 6000, Damage = 100, Count = 1 };
        var proposal = new WaveShowcaseCase(id, reference, "m1-review-composition-v1", "max", "hypothesis", gear, new("core:raider", 2, 400, 100, 60, 600, 1, 1000000, 1000000));
        var catalog = ReviewShowcaseCatalog.Create(baseline, proposal);
        Assert.Equal(originalHash, ReviewShowcaseCatalog.Hash(baseline));
        Assert.NotEqual(originalHash, ReviewShowcaseCatalog.Hash(catalog));
        Assert.NotEqual(ReviewShowcaseCatalog.Hash(catalog), ReviewShowcaseCatalog.Hash(ReviewShowcaseCatalog.Create(baseline, proposal with { Stage = "evolved" })));
        var configured = ReviewShowcaseCatalog.Create(baseline, proposal with { Arena = proposal.Arena with { SuppressedUntilTick = 2345, BaseExperience = 6789, EnemyDamage = 2 } });
        Assert.Equal(2345, configured.WaveRuntime!.BossSpawnTick);
        Assert.All(configured.WaveRuntime.Gear.Where(e => e.Key != id), e => Assert.Equal(2345, e.Value.CooldownTicks));
        Assert.All(configured.WaveRuntime.Enemies.Where(e => e.Key != proposal.Arena.EnemyId), e => Assert.Equal(2345, e.Value.FirstSpawnTick));
        Assert.Equal(6789, configured.Tuning.World.Progression.BaseExperience);
        Assert.Equal(2, configured.Enemies[proposal.Arena.EnemyId].Damage);
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.Create(baseline, proposal with { Arena = proposal.Arena with { SuppressedUntilTick = 600 } }));
        Assert.Throws<ArgumentException>(() => ReviewShowcaseCatalog.Create(baseline, proposal with { Gear = gear with { Count = WaveAttackComposition.MaximumImpacts + 1 } }));
        var options = new InteractiveOptions(new(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, ReviewShowcaseCatalog.Hash(catalog), MeleeAim: new());
        var session = new InteractiveSession(catalog, options);
        var hits = 0; var kills = 0;
        using var replayBytes = new MemoryStream(); ReplayCodec.WriteHeader(replayBytes, ReplayCodec.Header(options));
        for (var tick = 0; tick < 600; tick++)
        {
            var command = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance);
            session.Apply(command); ReplayCodec.WriteCommand(replayBytes, command);
            foreach (var e in ((IWaveRunView)session.View).CaptureWaveRuntime()!.Events)
            {
                if (e.Kind == "hit" && e.Source == id && e.Amount > 0)
                {
                    hits++;
                }

                if (e.Kind == "enemy-killed")
                {
                    kills++;
                }
            }
        }
        Assert.True(hits > 0); Assert.True(kills > 0);
        ReplayCodec.WriteEnd(replayBytes, new(session.NextSequence, 600, ReplayEndKind.Duration, session.ComputeStateHash()));
        replayBytes.Position = 0;
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, ReplayCodec.Read(replayBytes)).StateHash);
    }
    [Fact]
    public void DelayedCircleHasRealDamageOnlyAtImpactAndExposesLockedGeometry()
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with
        { Composition = new(1, "nearest-target", 3, 0, 0, 80), Count = 1, CooldownTicks = 100 };
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        system.Tick(world.Lord);
        Assert.Equal(100, enemy.Health);
        var tell = Assert.Single(world.WaveRuntime!.Events, e => e.Kind == "review-impact-tell");
        Assert.Equal(enemy.Position.X, tell.Position.X);
        for (var tick = 1; tick <= 3; tick++) { world.Tick = tick; system.Tick(world.Lord); }
        Assert.Equal(90, enemy.Health);
        Assert.Single(world.WaveRuntime.Events, e => e.Kind == "hit" && e.Amount == 10);
        Assert.Empty(world.WaveRuntime.PendingImpacts);
        Assert.Equal(1, world.WaveRuntime.Counters["successful-activation"]);
    }
    [Fact]
    public void ForwardLineLocksSpacedCentersAndResolvesInDeclaredTickOrder()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with
        { Composition = new(1, "forward-line", 2, 2, 100, 30), Count = 3, CooldownTicks = 100 };
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        system.Tick(world.Lord);
        Assert.Equal(new[] { 600, 700, 800 }, world.WaveRuntime!.PendingImpacts.Select(p => p.Position.X));
        Assert.Equal(new[] { 2, 4, 6 }, world.WaveRuntime.PendingImpacts.Select(p => p.Tick));
        for (var tick = 1; tick <= 6; tick++) { world.Tick = tick; system.Tick(world.Lord); }
        Assert.Empty(world.WaveRuntime.PendingImpacts);
        Assert.Equal(3, world.WaveRuntime.Counters["review-impact:" + gear.Id]);
    }
    [Theory]
    [InlineData(false, 90)]
    [InlineData(true, 70)]
    public void PatchPulsesStrikeAStandingEnemyOnceOrEveryPulseWithRehit(bool rehit, int health)
    {
        // ADR0061: a patch at the lord (forward-line, spacing 0) pulsing three times. Off: an activation strikes an enemy once.
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with
        { Composition = new(1, "forward-line", 1, 2, 0, 1000, rehit ? true : null), Count = 3, CooldownTicks = 100 };
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        system.Tick(world.Lord);
        for (var tick = 1; tick <= 6; tick++) { world.Tick = tick; system.Tick(world.Lord); }
        Assert.Equal(health, enemy.Health);
        Assert.Equal(3, world.WaveRuntime!.Counters["review-impact:" + gear.Id]);
    }
    [Fact]
    public void RehitOptionIsAbsentFromHashesUntilSet()
    {
        var (catalog, _, _, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gears = (Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime!.Gear;
        var gear = gears.Values.Single();
        gears[gear.Id] = gear with { Composition = new(1, "forward-line", 1, 2, 0, 1000) };
        var unset = PortableStateCodec.HashCatalog(catalog);
        gears[gear.Id] = gear with { Composition = new(1, "forward-line", 1, 2, 0, 1000, null) };
        Assert.Equal(unset, PortableStateCodec.HashCatalog(catalog));
        gears[gear.Id] = gear with { Composition = new(1, "forward-line", 1, 2, 0, 1000, true) };
        Assert.NotEqual(unset, PortableStateCodec.HashCatalog(catalog));
    }
    static (WaveRuntimeSystem System, WorldState World, string Gear, EnemyState[] Enemies) RicochetArena(int count, int? bounces, params (int X, int Y)[] at)
    {
        // ADR0062: the lord at (500, 500), range 200, so each bounce must stay within 200 of the last impact.
        var (catalog, _, world, first) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with
        { Composition = new(1, "ricochet", 2, 3, 0, 20, null, bounces), Count = count, CooldownTicks = 100 };
        var enemies = new EnemyState[at.Length];
        for (var i = 0; i < at.Length; i++)
        {
            enemies[i] = i == 0 ? first : new EnemyState { Id = 10 + i, Definition = first.Definition, Health = 100 };
            enemies[i].Position = new(at[i].X, at[i].Y);
            if (i > 0) { world.Enemies.Add(enemies[i]); }
        }
        return (new WaveRuntimeSystem(catalog, world, new(1, true), null), world, gear.Id, enemies);
    }
    static List<WaveEvent> Run(WaveRuntimeSystem system, WorldState world, int ticks)
    {
        var events = new List<WaveEvent>();
        for (var tick = 0; tick <= ticks; tick++) { world.Tick = tick; system.Tick(world.Lord); events.AddRange(world.WaveRuntime!.Events); }
        return events;
    }
    [Theory]
    [InlineData(0, new[] { 90, 100, 100 })]
    [InlineData(1, new[] { 90, 90, 100 })]
    [InlineData(2, new[] { 90, 90, 90 })]
    [InlineData(3, new[] { 90, 90, 90 })]
    public void RicochetArrowStrikesItsTargetThenBouncesToTheNearestEnemyItHasNotStruck(int bounces, int[] health)
    {
        // The second and third enemies are out of the lord's range but each within range of the previous impact.
        var (system, world, gear, enemies) = RicochetArena(1, bounces, (600, 500), (750, 500), (900, 500));
        var events = Run(system, world, 12);
        Assert.Equal(health, enemies.Select(e => e.Health));
        Assert.Equal(Math.Min(bounces, 2) + 1, world.WaveRuntime!.Counters["review-impact:" + gear]);
        Assert.Equal(new[] { 2, 5, 8 }.Take(Math.Min(bounces, 2) + 1), events.Where(e => e.Kind == "review-impact").Select(e => e.Tick));
        Assert.Empty(world.WaveRuntime.PendingImpacts);
        Assert.Equal(1, world.WaveRuntime.Counters["successful-activation"]);
        var tells = events.Where(e => e.Kind == "review-impact-tell").ToArray();
        Assert.Equal(500, tells[0].Position.X);
        if (bounces > 0) { Assert.Equal((600, 750), (tells[1].Position.X, tells[1].Target.X)); }
    }
    [Fact]
    public void RicochetArrowsStrikeAnEnemyOncePerArrowButEachArrowMayStrikeIt()
    {
        var (system, world, gear, enemies) = RicochetArena(2, 1, (600, 500), (650, 500));
        Run(system, world, 8);
        Assert.Equal(new[] { 80, 80 }, enemies.Select(e => e.Health));
        Assert.Equal(4, world.WaveRuntime!.Counters["review-impact:" + gear]);
    }
    [Fact]
    public void RicochetArrowFollowsItsTargetUntilImpact()
    {
        var (system, world, _, enemies) = RicochetArena(1, 0, (600, 500));
        system.Tick(world.Lord);
        enemies[0].Position = new(640, 560);
        world.Tick = 1; var events = Run(system, world, 3);
        Assert.Equal(90, enemies[0].Health);
        Assert.Equal(640, events.Single(e => e.Kind == "review-impact").Position.X);
    }
    [Fact]
    public void RicochetBouncesAreAbsentFromHashesUntilSetAndOnlyValidForRicochet()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gears = (Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime!.Gear;
        var gear = gears.Values.Single();
        gears[gear.Id] = gear with { Composition = new(1, "nearest-target", 2, 0, 0, 20) };
        var unset = PortableStateCodec.HashCatalog(catalog);
        gears[gear.Id] = gear with { Composition = new(1, "nearest-target", 2, 0, 0, 20, null, null) };
        Assert.Equal(unset, PortableStateCodec.HashCatalog(catalog));
        foreach (var bad in new WaveAttackComposition[] { new(1, "nearest-target", 2, 0, 0, 20, null, 1), new(1, "ricochet", 2, 3, 0, 20), new(1, "ricochet", 2, 0, 0, 20, null, 1), new(1, "ricochet", 2, 3, 0, 20, true, 1), new(1, "ricochet", 2, 3, 0, 20, null, -1) })
        {
            gears[gear.Id] = gear with { Composition = bad, CooldownTicks = 100 };
            world.Equipment[0].ReadyTick = 0;
            var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
            Assert.Throws<ArgumentException>(() => system.Tick(world.Lord));
        }
    }
    [Fact]
    public void LureSackDrawsEnemiesWithinItsRadiusThenBurstsOnce()
    {
        // ADR0063: lands after 2 ticks where the nearest enemy stood at the cast (580 after its first step), lures for 4 ticks,
        // then bursts over radius 200.
        var (catalog, _, world, near) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with
        { Composition = new(1, "lure-bomb", 2, 0, 0, 200, null, null, 4), Count = 1, CooldownTicks = 100 };
        ((Dictionary<string, EnemyDefinition>)catalog.Enemies)[near.Definition] = catalog.Enemies[near.Definition] with { Speed = 20 };
        var inside = new EnemyState { Id = 11, Definition = near.Definition, Position = new(800, 500), Health = 100 };
        var outside = new EnemyState { Id = 12, Definition = near.Definition, Position = new(1000, 500), Health = 100 };
        world.Enemies.Add(inside); world.Enemies.Add(outside);
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        var events = Run(system, world, 2);
        var lure = Assert.Single(events, e => e.Kind == "review-lure");
        Assert.Equal((2, 580, 200), (lure.Tick, lure.Position.X, lure.Amount));
        Assert.Single(world.WaveRuntime!.Lures);
        var before = inside.Position.X;
        for (var tick = 3; tick <= 8; tick++)
        {
            world.Tick = tick; system.Tick(world.Lord); events.AddRange(world.WaveRuntime.Events);
            if (tick == 6) { Assert.Equal(before - 80, inside.Position.X); }   // four lured steps of 20 toward the sack
        }
        Assert.Equal(new[] { 6 }, events.Where(e => e.Kind == "review-impact").Select(e => e.Tick));
        Assert.Equal((90, 90, 100), (near.Health, inside.Health, outside.Health));
        Assert.Empty(world.WaveRuntime.PendingImpacts);
        Assert.Equal(1, world.WaveRuntime.Counters["successful-activation"]);
    }
    [Fact]
    public void LureTicksAreAbsentFromHashesUntilSetAndOnlyValidForLureBomb()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gears = (Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime!.Gear;
        var gear = gears.Values.Single();
        gears[gear.Id] = gear with { Composition = new(1, "nearest-target", 2, 0, 0, 20) };
        var unset = PortableStateCodec.HashCatalog(catalog);
        gears[gear.Id] = gear with { Composition = new(1, "nearest-target", 2, 0, 0, 20, null, null, null) };
        Assert.Equal(unset, PortableStateCodec.HashCatalog(catalog));
        foreach (var bad in new WaveAttackComposition[] { new(1, "nearest-target", 2, 0, 0, 20, null, null, 4), new(1, "lure-bomb", 2, 0, 0, 20), new(1, "lure-bomb", 2, 0, 0, 20, null, null, 0), new(1, "lure-bomb", 2, 1, 0, 20, null, null, 4), new(1, "lure-bomb", 2, 0, 0, 20, true, null, 4), new(1, "lure-bomb", 2, 0, 0, 20, null, 1, 4) })
        {
            gears[gear.Id] = gear with { Composition = bad, CooldownTicks = 100 };
            world.Equipment[0].ReadyTick = 0;
            var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
            Assert.Throws<ArgumentException>(() => system.Tick(world.Lord));
        }
    }
    [Fact]
    public void MovingOutOfLockedImpactAvoidsDamage()
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with
        { Composition = new(1, "nearest-target", 3, 0, 0, 20), Count = 1, CooldownTicks = 100 };
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        system.Tick(world.Lord); enemy.Position = new(900, 900);
        for (var tick = 1; tick <= 3; tick++) { world.Tick = tick; system.Tick(world.Lord); }
        Assert.Equal(100, enemy.Health);
        Assert.Equal(1, world.WaveRuntime!.Counters["empty-activation"]);
    }
}
