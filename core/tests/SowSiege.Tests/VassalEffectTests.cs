using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalEffectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialAndDisplayedHealthUseTheSameFinalMaximum(bool readableHealth)
    {
        var (catalog, _, _, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var run = new RunOptions(1, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "random", ManualCards: true);
        var options = new InteractiveOptions(run, AimMode.Movement, new string('A', 64), Feel: readableHealth ? new(ReadableHealth: true) : null,
            Vassals: VassalRunTests.Options(MetaAbility.Health));
        var session = new InteractiveSession(catalog, options);
        var expected = (readableHealth ? 100 : catalog.Tuning.World.Map.LordHealth) * 1150 / 1000;
        Assert.Equal(expected, session.Simulation.World.LordHealth);
        Assert.Equal(expected, session.View.CaptureFrame().Lord.MaxHealth);
    }

    [Theory]
    [InlineData(100, 1000, 109)]
    [InlineData(100, 105, 105)]
    [InlineData(0, 1000, 0)]
    public void AttackScalesCandidateOnceBeforeHealthClamp(int requested, int health, int expected)
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with { Damage = requested };
        enemy.Health = health;
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), new() { Vassals = State(MetaAbility.Attack) });
        system.Tick(world.Lord);
        Assert.Equal(health - expected, enemy.Health);
        Assert.Equal(expected, world.WeaponDamage);
    }

    [Fact]
    public void MovementSpeedRetainsTheUnscaledBaseAndCurrentItem()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        ((Dictionary<string, WaveItemDefinition>)catalog.WaveRuntime!.Items).Add("test:boots", new("test:boots", "design", WaveItemKind.MoveSpeed, Array.Empty<string>(), 10));
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), new() { Vassals = State(MetaAbility.Movement) });
        Assert.Equal(1000, system.MovementSpeed(1000));
        Assert.Equal(1000, system.MovementSpeed(1000));
        world.WaveRuntime!.Items.Add("test:boots");
        Assert.Equal(1010, system.MovementSpeed(1000));
    }

    [Fact]
    public void EnemyExperienceAccumulatesAtDeathAndDoesNotPayAgainNextTick()
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), new() { Vassals = State(MetaAbility.Experience) });
        world.Enemies.Clear();
        for (var i = 0; i < 1000; i++) { world.Enemies.Add(new() { Id = 10000 + i, Definition = enemy.Definition, Health = 0, Position = enemy.Position }); }
        system.Tick(world.Lord);
        var expected = (long)catalog.Enemies[enemy.Definition].Experience * 1063;
        Assert.Equal(expected, world.KillExperience);
        world.Tick++;
        system.Tick(world.Lord);
        Assert.Equal(expected, world.KillExperience);
    }

    [Fact]
    public void GroupReuseKeepsHealthAndFoodHealsOnlyToItsCreationMaximum()
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.MusterWave);
        var gear = catalog.WaveRuntime!.Gear.Values.Single() with { Capacity = 100, Damage = 100 };
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear;
        var state = State(MetaAbility.Allies);
        var interactive = new InteractiveState { Vassals = state };
        var work = new WaveWorkSystem(catalog, world, interactive);
        work.Activate(gear, world.Lord, new(1, 0));
        var group = Assert.Single(world.WaveRuntime!.Groups);
        Assert.Equal(113, group.Health);
        group.Health = 90; group.Phase = "idle"; world.WaveRuntime.AvailableWorkers = 1;
        work.Activate(gear, world.Lord, new(1, 0));
        Assert.Single(world.WaveRuntime.Groups);
        Assert.Equal(90, group.Health);
        group.Health = 112; group.ReservedFood = 20;
        new WaveItemSubscriptions(catalog, world, new(catalog.WaveRuntime), vassals: state).ConsumeMissionFood(gear, group);
        Assert.Equal(113, group.Health);
        Assert.Equal(0, group.ReservedFood);
        enemy.Health = 1000; enemy.Position = world.Lord;
        work.Tick();
        Assert.Equal(887, enemy.Health);
        Assert.Equal(113, world.AllyDamage);
    }

    [Fact]
    public void GrainAccumulatesWhileBuildingsKeepTheirOriginalProgress()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.SeedFan);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        var state = State(MetaAbility.Growth);
        var growth = new WaveGrowthPrimitives(world, new(catalog.WaveRuntime), state);
        var grain = new WaveWork { Id = 100, Source = gear.Id, Kind = "grain", Position = world.Lord, Required = 1000, Health = 1 };
        for (var i = 0; i < 1000; i++) { growth.AdvanceTime(grain, gear); }
        Assert.Equal(1092, grain.Progress);
        var building = new WaveWork { Id = 101, Source = gear.Id, Kind = "building", Position = world.Lord, Required = 1000, Health = 1 };
        for (var i = 0; i < 1000; i++) { growth.AdvanceTime(building, gear); }
        Assert.Equal(1000, building.Progress);
    }

    [Fact]
    public void BlockedWorkDoesNotAdvanceTheGrainRemainder()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.ConstructionSlam);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        var state = State(MetaAbility.Growth);
        var growth = new WaveGrowthPrimitives(world, new(catalog.WaveRuntime), state);
        var grain = new WaveWork { Id = 100, Source = gear.Id, Kind = "grain", Position = new(100000, 100000), Required = 1000, Health = 1 };
        var before = state.Encode();
        for (var i = 0; i < 100; i++) { Assert.False(growth.AdvanceTime(grain, gear)); }
        Assert.Equal(0, grain.Progress);
        Assert.Equal(before, state.Encode());
    }

    [Fact]
    public void HarvestDiscardsTheOldGrainRemainderAndNextSowingGetsANewIdentity()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.SeedFan);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        var state = State(MetaAbility.Growth);
        var work = new WaveWorkSystem(catalog, world, new() { Vassals = state });
        work.Activate(gear, world.Lord, new(1, 0));
        var plot = Assert.Single(world.WaveRuntime!.Work);
        state.AdvanceGrain(plot.Id, 1);
        plot.Complete = true;
        work.Harvest(plot);
        var expected = State(MetaAbility.Growth);
        expected.AwardExperience("growth", gear.RewardExperience);
        Assert.Equal(expected.Encode(), state.Encode());
        work.Activate(gear, world.Lord, new(1, 0));
        var next = world.WaveRuntime.Work.Single(w => w.Health > 0);
        Assert.NotEqual(plot.Id, next.Id);
        Assert.Equal(1, state.AdvanceGrain(next.Id, 1));
    }

    [Fact]
    public void CompletionDuplicateDoesNotAdvanceExperienceAndCollectionDoesNotMultiplyAgain()
    {
        var (catalog, _, world, _) = WaveRuntimeTests.Arena(WaveAttackKind.SeedFan);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        var state = State(MetaAbility.Experience);
        var ledger = new WaveCompletionLedger(world, new(catalog.WaveRuntime), state);
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(ledger.Claim(gear.Id, i, 1, "growth", world.Lord, 1));
            Assert.False(ledger.Claim(gear.Id, i, 1, "growth", world.Lord, 1));
        }
        Assert.Equal(1063, world.WaveRuntime!.Rewards.Sum(r => r.Experience));
        var work = new WaveWorkSystem(catalog, world, new() { Vassals = state });
        work.Tick();
        Assert.Equal(1063, world.HarvestExperience);
        Assert.Empty(world.WaveRuntime.Rewards);
    }

    private static VassalRunState State(MetaAbility ability) => new(VassalRunTests.Options(ability));
}
