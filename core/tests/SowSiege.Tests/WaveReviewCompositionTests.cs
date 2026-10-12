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
        for (var tick = 0; tick < 360; tick++)
        {
            var input = ShowcaseFixtureCli.WalkingInput(tick);
            Assert.Equal(PlayerInput.Scale, Math.Abs(input.X) + Math.Abs(input.Y));
            x += input.X; y += input.Y;
            Assert.Equal(input, ShowcaseFixtureCli.WalkingInput(tick + 360));
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
            { if (e.Kind == "hit" && e.Source == id && e.Amount > 0)
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
