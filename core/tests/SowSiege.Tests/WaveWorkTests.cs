using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;

public sealed class WaveWorkTests
{
    private static (WaveWorkSystem System, WorldState World, WaveRuntimeDefinition Definition) Arena(int timber = 1, int workers = 1, int dryAfter = 0, int rainTicks = 0)
    {
        var gear = new[] {
            new WaveGearDefinition("test:seed", "design", WaveAttackKind.SeedFan, 1, 20, 2, 10, 1, 10, 0, 3, 20, 7, 4),
            new WaveGearDefinition("test:water", "design", WaveAttackKind.WaterFan, 1, 20, 2, 10, 1, 10, 0, 3, 20, 5, 2),
            new WaveGearDefinition("test:hammer", "design", WaveAttackKind.ConstructionSlam, 1, 20, 2, 10, 1, 10, 0, 3, 20, 9, 2),
            new WaveGearDefinition("test:horn", "design", WaveAttackKind.MusterWave, 2, 40, 2, 10, 1, 10, 0, 3, 20, 11, 4)
        }.ToDictionary(g => g.Id);
        var definition = new WaveRuntimeDefinition("test", "test:chapter", "design", "test:boss", 1000, 1, workers, timber, 2, rainTicks, 2, 10, 5, 20, 10, gear,
            new Dictionary<string, WaveItemDefinition>(), new Dictionary<string, WaveEvolutionDefinition>(), new Dictionary<string, WaveEnemyDefinition>(), DryAfterTicks: dryAfter);
        var catalog = InteractiveTests.Catalog() with { WaveRuntime = definition };
        var world = new WorldState { Lord = new(100, 100), WaveRuntime = new() { Water = 2, Timber = timber, AvailableWorkers = workers } };
        return (new(catalog, world, null), world, definition);
    }
    private static void Advance(WaveWorkSystem system, WorldState world, int count)
    { for (var i = 0; i < count; i++) { world.Tick++; system.Tick(); } }

    [Fact]
    public void SeedNeedsMaturityAndContactAndClaimsOriginalCycleOnce()
    {
        var (system, world, definition) = Arena();
        system.Activate(definition.Gear["test:seed"], world.Lord, new(1, 0));
        var plot = Assert.Single(world.WaveRuntime!.Work); system.Harvest(plot);
        Assert.Empty(world.WaveRuntime.Rewards);
        Advance(system, world, 3); Assert.True(plot.Complete); Assert.Equal(0, world.Experience);
        world.Lord = plot.Position; system.Harvest(plot); system.Harvest(plot); system.Tick();
        Assert.Equal(7, world.Experience); Assert.Equal(1, world.Harvests);
        plot.Health = 1; system.Harvest(plot); system.Tick(); Assert.Equal(7, world.Experience);
    }

    [Fact]
    public void ConstructionProgressesWhileMovingInRadiusAndFiniteStockCannotRegenerateByRepair()
    {
        var (system, world, definition) = Arena(); var hammer = definition.Gear["test:hammer"];
        system.Activate(hammer, world.Lord, new(1, 0)); var building = Assert.Single(world.WaveRuntime!.Work);
        world.Lord = new(500, 500); Advance(system, world, 5); Assert.Equal(0, building.Progress);
        world.Lord = new(105, 100); Advance(system, world, 1); world.Lord = new(110, 101); Advance(system, world, 4);
        Assert.True(building.Complete); Assert.Equal(0, world.WaveRuntime.Timber);
        Assert.Single(world.WaveRuntime.Completed, k => k.StartsWith("shipment:"));
        building.Health = 0; system.Activate(hammer, building.Position, new(0, 0)); Advance(system, world, 12);
        Assert.True(world.WaveRuntime.RepairCompleted); Assert.Equal(0, world.WaveRuntime.Timber);
        Assert.Single(world.WaveRuntime.Completed, k => k.StartsWith("shipment:"));
    }

    [Fact]
    public void InterruptedShipmentRetainsReservationAndIdentityUntilRepairFinishes()
    {
        var (system, world, definition) = Arena(); var hammer = definition.Gear["test:hammer"];
        system.Activate(hammer, world.Lord, new(1, 0)); Advance(system, world, 3);
        var building = Assert.Single(world.WaveRuntime!.Work); var cycle = building.Cycle;
        Assert.True(building.ShipmentActive); Assert.Equal(0, world.WaveRuntime.Timber);
        building.Health = 0; Advance(system, world, 10); Assert.Empty(world.WaveRuntime.Rewards);
        system.Activate(hammer, building.Position, new(0, 0)); Advance(system, world, 5);
        var reward = Assert.Single(world.WaveRuntime.Rewards); Assert.EndsWith(":" + cycle, reward.CompletionKey);
        Assert.Equal(0, world.WaveRuntime.Timber);
    }

    [Fact]
    public void WaterAttackAndEmptyPoolDoNotProduceExperienceAndRepairDoesNotRefill()
    {
        var (system, world, definition) = Arena(); var ladle = definition.Gear["test:water"];
        system.Activate(ladle, world.Lord, new(1, 0)); system.Activate(ladle, world.Lord, new(1, 0));
        var pool = Assert.Single(world.WaveRuntime!.Work); Assert.Equal(0, world.WaveRuntime.Water);
        pool.Health = 0; system.Activate(ladle, world.Lord, new(1, 0)); Advance(system, world, 20);
        Assert.Equal(0, world.WaveRuntime.Water); Assert.Empty(world.WaveRuntime.Rewards); Assert.Equal(0, world.Experience);
    }

    [Fact]
    public void IrrigationSpendsActualStockAndDoesNotRepeatForSamePlot()
    {
        var (system, world, definition) = Arena();
        system.Activate(definition.Gear["test:seed"], world.Lord, new(1, 0));
        world.WaveRuntime!.Work.Single(w => w.Kind == "grain").Dry = true;
        system.Activate(definition.Gear["test:water"], world.Lord, new(1, 0)); Advance(system, world, 1);
        Assert.Equal(0, world.WaveRuntime!.Water); Assert.Single(world.WaveRuntime.Rewards);
        world.WaveRuntime.Water = 2; Advance(system, world, 10);
        Assert.Single(world.WaveRuntime.Rewards); Assert.Equal(2, world.WaveRuntime.Water);
    }

    [Fact]
    public void HornRequiresRealEnemyAndAvailableWorkersAndOneGroupCannotMultiply()
    {
        var (system, world, definition) = Arena(); var horn = definition.Gear["test:horn"];
        system.Activate(horn, world.Lord, new(1, 0)); Assert.Empty(world.WaveRuntime!.Groups);
        world.Enemies.Add(new() { Id = 100, Health = 1, Position = new(110, 100) });
        system.Activate(horn, world.Lord, new(1, 0)); system.Activate(horn, world.Lord, new(1, 0));
        Assert.Single(world.WaveRuntime.Groups); Assert.Equal(0, world.WaveRuntime.AvailableWorkers);
        Advance(system, world, 1); system.OnKill(world.Enemies[0]); Advance(system, world, 2);
        Assert.Equal(11, world.Experience); Assert.Equal("idle", world.WaveRuntime.Groups[0].Phase);
        Advance(system, world, 10); Assert.Equal(11, world.Experience);
    }

    [Fact]
    public void EnemyLeavingBeforeEngagementNeverPaysReturnExperience()
    {
        var (system, world, definition) = Arena();
        world.Enemies.Add(new() { Id = 100, Health = 100, Position = new(110, 100) });
        system.Activate(definition.Gear["test:horn"], world.Lord, new(1, 0)); world.Enemies.Clear();
        Advance(system, world, 10); Assert.Empty(world.WaveRuntime!.Rewards); Assert.Equal(0, world.Experience);
    }

    [Fact]
    public void ShelteredPlotPausesWhenParentIsRuinedAndResumesSameCycleAfterRepair()
    {
        var (system, world, definition) = Arena(timber: 0);
        var evolution = new WaveEvolutionDefinition("test:shelter", "design", WaveEvolutionKind.ShelteredPlot, new[] { "test:seed", "test:hammer" });
        ((Dictionary<string, WaveEvolutionDefinition>)definition.Evolutions).Add(evolution.Id, evolution);
        var gear = definition.Gear["test:hammer"] with { Id = evolution.Id };
        system.Activate(gear, world.Lord, new(1, 0));
        var plot = Assert.Single(world.WaveRuntime!.Work, w => w.Kind == "grain");
        var building = Assert.Single(world.WaveRuntime.Work, w => w.Kind == "building");
        var cycle = plot.Cycle; Advance(system, world, 1); Assert.Equal(0, plot.Progress);
        Advance(system, world, 2); Assert.True(building.Complete); Assert.True(plot.Progress > 0);
        building.Health = 0; var progress = plot.Progress; Advance(system, world, 5);
        Assert.Equal(progress, plot.Progress); Assert.False(plot.Protected);
        system.Activate(gear, building.Position, new(0, 0)); Advance(system, world, 5);
        Assert.Equal(cycle, plot.Cycle); Assert.True(plot.Complete); Assert.True(plot.Protected);
    }

    [Fact]
    public void MatureBuildingStillDefendsWhenLordLeavesWorkRadius()
    {
        var (system, world, definition) = Arena(timber: 0);
        system.Activate(definition.Gear["test:hammer"], world.Lord, new(1, 0)); Advance(system, world, 3);
        var building = Assert.Single(world.WaveRuntime!.Work);
        var enemy = new EnemyState { Id = 100, Position = building.Position, Health = 5 }; world.Enemies.Add(enemy);
        world.Lord = new(500, 500); Advance(system, world, 1); Assert.Equal(4, enemy.Health);
    }

    [Fact]
    public void EarlierTrainingDoesNotRewardLaterMissionWithoutItsOwnParticipatingKill()
    {
        var (system, world, definition) = Arena();
        var enemy = new EnemyState { Id = 100, Health = 1, Position = new(110, 100) }; world.Enemies.Add(enemy);
        var horn = definition.Gear["test:horn"]; system.Activate(horn, world.Lord, new(1, 0));
        Advance(system, world, 1); system.OnKill(enemy); Advance(system, world, 2); Assert.Equal(11, world.Experience);
        enemy = new EnemyState { Id = 101, Health = 100, Position = new(110, 100) }; world.Enemies.Add(enemy);
        system.Activate(horn, world.Lord, new(1, 0)); Advance(system, world, 2); world.Enemies.Clear();
        Advance(system, world, 5); Assert.Equal(11, world.Experience);
    }

    [Fact]
    public void CarryWaterMovesExistingStockOnlyToFirstYoungPlot()
    {
        var (system, world, definition) = Arena();
        ((Dictionary<string, WaveItemDefinition>)definition.Items).Add("test:bead", new("test:bead", "design", WaveItemKind.CarryWater, new[] { "test:water" }, 0));
        world.WaveRuntime!.Items.Add("test:bead");
        system.Activate(definition.Gear["test:water"], world.Lord, new(1, 0));
        system.Activate(definition.Gear["test:seed"], new(200, 100), new(1, 0));
        world.WaveRuntime.Work.Single(w => w.Kind == "grain").Dry = true;
        world.Lord = world.WaveRuntime.Work.Single(w => w.Kind == "water").Position; Advance(system, world, 1);
        Assert.Equal(1, world.WaveRuntime.CarriedWater); Assert.Equal(0, world.WaveRuntime.Water);
        var plot = world.WaveRuntime.Work.Single(w => w.Kind == "grain"); world.Lord = plot.Position; Advance(system, world, 1);
        Assert.True(plot.Irrigated); Assert.Equal(0, world.WaveRuntime.CarriedWater); Assert.Equal(5, world.Experience);
        Advance(system, world, 5); Assert.Equal(12, world.Experience);
    }

    [Fact]
    public void HarvestGuardRepositionsExistingGroupWithoutInventingPeople()
    {
        var (system, world, definition) = Arena();
        ((Dictionary<string, WaveItemDefinition>)definition.Items).Add("test:guard", new("test:guard", "design", WaveItemKind.HarvestGuard, new[] { "test:seed", "test:horn" }, 0));
        world.WaveRuntime!.Items.Add("test:guard");
        system.Activate(definition.Gear["test:seed"], world.Lord, new(1, 0)); Advance(system, world, 3);
        var plot = Assert.Single(world.WaveRuntime.Work); system.Harvest(plot); Assert.Empty(world.WaveRuntime.Groups);
        world.Enemies.Add(new() { Id = 100, Health = 100, Position = new(110, 100) }); system.Activate(definition.Gear["test:horn"], world.Lord, new(1, 0));
        var group = Assert.Single(world.WaveRuntime.Groups); var workers = world.WaveRuntime.AvailableWorkers;
        plot = new() { Id = 101, Source = "test:seed", Kind = "grain", Health = 1, Complete = true, Cycle = 101, Position = new(130, 100) };
        world.WaveRuntime.Work.Add(plot); system.Harvest(plot);
        Assert.Single(world.WaveRuntime.Groups); Assert.Equal(workers, world.WaveRuntime.AvailableWorkers);
        Assert.Equal("guarding", group.Phase); Assert.Equal(plot.Position, group.Destination);
    }

    [Fact]
    public void PackedFoodIsReservedAndUnspentFoodReturnsAfterEmptyTrip()
    {
        var (system, world, definition) = Arena(); world.Food = 1;
        ((Dictionary<string, WaveItemDefinition>)definition.Items).Add("test:meal", new("test:meal", "design", WaveItemKind.FieldMeal, new[] { "test:horn" }, 0));
        world.WaveRuntime!.Items.Add("test:meal");
        world.Enemies.Add(new() { Id = 100, Health = 100, Position = new(110, 100) });
        system.Activate(definition.Gear["test:horn"], world.Lord, new(1, 0));
        Assert.Equal(0, world.Food); Assert.Equal(1, Assert.Single(world.WaveRuntime.Groups).ReservedFood);
        world.Enemies.Clear(); Advance(system, world, 3);
        Assert.Equal(1, world.Food); Assert.Equal(1, world.WaveRuntime.AvailableWorkers); Assert.Equal(0, world.Experience);
    }

    [Fact]
    public void HarvestCannotDispatchIdleReturnedWorkerOrMultiplyAvailableWorkers()
    {
        var (system, world, definition) = Arena();
        ((Dictionary<string, WaveItemDefinition>)definition.Items).Add("test:guard", new("test:guard", "design", WaveItemKind.HarvestGuard, new[] { "test:horn" }, 0));
        world.WaveRuntime!.Items.Add("test:guard");
        world.WaveRuntime.Groups.Add(new() { Id = 200, Source = "test:horn", Health = 1, Phase = "idle", Position = world.Lord });
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var plot = new WaveWork { Id = 100 + cycle, Source = "test:seed", Kind = "grain", Health = 1, Complete = true, Cycle = cycle, Position = world.Lord };
            world.WaveRuntime.Work.Add(plot); system.Harvest(plot); Advance(system, world, 3);
        }
        Assert.Equal(1, world.WaveRuntime.AvailableWorkers); Assert.Equal("idle", Assert.Single(world.WaveRuntime.Groups).Phase);
    }

    [Fact]
    public void MoistPlotDoesNotConsumeIrrigationStockOrPayWaterExperience()
    {
        var (system, world, definition) = Arena();
        system.Activate(definition.Gear["test:seed"], world.Lord, new(1, 0));
        system.Activate(definition.Gear["test:water"], world.Lord, new(1, 0)); Advance(system, world, 1);
        Assert.Equal(1, world.WaveRuntime!.Water); Assert.Empty(world.WaveRuntime.Rewards);
    }

    [Fact]
    public void DryPlotActuallyPausesUntilDeliveryAndRepeatedDrynessCannotPaySameCycleAgain()
    {
        var (system, world, definition) = Arena();
        system.Activate(definition.Gear["test:seed"], world.Lord, new(1, 0));
        var plot = Assert.Single(world.WaveRuntime!.Work); plot.Dry = true;
        Advance(system, world, 5); Assert.Equal(0, plot.Progress); Assert.False(plot.Complete);
        system.Activate(definition.Gear["test:water"], world.Lord, new(1, 0)); Advance(system, world, 1);
        Assert.False(plot.Dry); Assert.Single(world.WaveRuntime.Rewards);
        plot.Dry = true; world.WaveRuntime.Water = 1; Advance(system, world, 1);
        Assert.False(plot.Dry); Assert.Equal(0, world.WaveRuntime.Water); Assert.Single(world.WaveRuntime.Rewards);
    }

    [Fact]
    public void ExplicitDryDeadlinePausesAndNaturalRainResumesWithoutIrrigationReward()
    {
        var (system, world, definition) = Arena(dryAfter: 2, rainTicks: 5);
        system.Activate(definition.Gear["test:seed"], world.Lord, new(1, 0));
        var plot = Assert.Single(world.WaveRuntime!.Work);
        Advance(system, world, 4); Assert.True(plot.Dry); Assert.Equal(1, plot.Progress);
        Advance(system, world, 1); Assert.False(plot.Dry); Assert.Equal(2, plot.Progress);
        Assert.Empty(world.WaveRuntime.Rewards); Assert.Equal(0, world.Experience);
    }

    [Fact]
    public void RepairCompletionRestoresDamagedPositiveHealthWithoutNewStockOrCycle()
    {
        var (system, world, definition) = Arena(timber: 0);
        system.Activate(definition.Gear["test:hammer"], world.Lord, new(1, 0)); Advance(system, world, 3);
        var building = Assert.Single(world.WaveRuntime!.Work); var cycle = building.Cycle;
        building.Health = 1; building.Complete = false; building.Progress = 0; building.ReadyTick = -1;
        Advance(system, world, 3);
        Assert.True(building.Complete); Assert.Equal(definition.Gear["test:hammer"].Capacity, building.Health);
        Assert.Equal(cycle, building.Cycle); Assert.Equal(0, world.WaveRuntime.Timber); Assert.Empty(world.WaveRuntime.Rewards);
    }

    [Fact]
    public void TrainedCompanyAlternatesFrontRankAndPhysicallyCoversWithoutDamageBonus()
    {
        var (system, world, definition) = Arena();
        var enemy = new EnemyState { Id = 100, Health = 20, Position = new(120, 100) }; world.Enemies.Add(enemy);
        system.Activate(definition.Gear["test:horn"], world.Lord, new(1, 0));
        var group = Assert.Single(world.WaveRuntime!.Groups); group.Training = 1;
        Advance(system, world, 1); Assert.Equal(18, enemy.Health); Assert.Equal(1, group.FrontRank); Assert.Equal("cover", group.Formation);
        group.Position = enemy.Position;
        Advance(system, world, 1); Assert.True(group.Position.X < enemy.Position.X); Assert.Equal(18, enemy.Health);
        Advance(system, world, 1); Assert.Equal(16, enemy.Health); Assert.Equal(0, group.FrontRank);
        Assert.Single(world.WaveRuntime.Groups);
        Assert.Contains(world.WaveRuntime.Events, e => e.Kind == "group-rank-swapped");
    }

    [Fact]
    public void PlantingSweepCreatesDistinctPlotsInsideRealFacingArcAndRespectsCapacity()
    {
        var (system, world, definition) = Arena(); var origin = new Position(2000, 2000); var direction = new Position(1, 0);
        system.PlantSweep("test:seed", origin, direction, 1000, 3);
        var plots = world.WaveRuntime!.Work.ToArray(); Assert.True(plots.Length > 1);
        Assert.All(plots, p => Assert.True(WaveRuntimeSystem.InArc(origin, direction, p.Position, 1000)));
        var originalCycles = plots.Select(p => p.Cycle).ToArray(); system.PlantSweep("test:seed", origin, direction, 1000, 3);
        Assert.Equal(originalCycles, world.WaveRuntime.Work.Select(p => p.Cycle));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(30, false)]
    public void BuildingBraceKnocksOnlyActualInRangeHitAwayAndClampsToMap(int offset, bool hit)
    {
        var (system, world, definition) = Arena(timber: 0);
        var hammer = definition.Gear["test:hammer"] with { Knockback = 1000 };
        ((Dictionary<string, WaveGearDefinition>)definition.Gear)[hammer.Id] = hammer;
        system.Activate(hammer, world.Lord, new(0, 0)); Advance(system, world, 3);
        var building = Assert.Single(world.WaveRuntime!.Work); building.Position = new(20, 100); world.Lord = new(20, 100);
        var enemy = new EnemyState { Id = 100, Health = 5, Position = new(20 - offset, 100) }; world.Enemies.Add(enemy);
        var before = enemy.Position; Advance(system, world, 1);
        Assert.Equal(hit ? 4 : 5, enemy.Health); Assert.Equal(hit ? new Position(0, 100) : before, enemy.Position);
        Assert.Equal(hit ? 1 : 0, world.WeaponDamage);
        Assert.Equal(hit, world.WaveRuntime.Events.Any(e => e.Kind == "building-brace-hit"));
    }
}
