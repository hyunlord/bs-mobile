using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class ExperimentTests
{
    internal static ContentCatalog Catalog()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "s4-stage-one");
        return catalog with { Experiment = new(new([new(-3000, -3000), new(3000, -3000), new(3000, 3000), new(-3000, 3000)], 90, 1200, 1200), new(30, 15, 3), ["weapon", "land", "building", "people"]) };
    }
    internal static Simulation Create(ContentCatalog catalog, string policy = "mixed", string movement = "circuit") => new(catalog, new(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, policy, Movement: movement));
    [Fact]
    public void CircuitActualTraceIsIdenticalAcrossPoliciesDespiteCardAndRandomDifferences()
    {
        var catalog = Catalog();
        var simulations = catalog.Tuning.Policies.Keys.Select(policy => Create(catalog, policy)).ToArray();
        foreach (var simulation in simulations) { for (var tick = 0; tick < 900; tick++) { simulation.Tick(); } }
        var traces = simulations.Select(simulation => Assert.IsType<ExperimentResult>(simulation.Result().Experiment).MovementTrace).ToArray();
        Assert.All(traces, trace => Assert.Equal(traces[0], trace)); Assert.Equal(1802, traces[0].Length);
        Assert.NotEqual(traces[0][0], traces[0][2]);
    }
    [Fact]
    public void ExperimentHasActualInitialAndTerminalSamplesOnly()
    {
        var catalog = Catalog(); catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 7 } };
        var simulation = Create(catalog); while (!simulation.IsComplete) { simulation.Tick(); }
        var result = Assert.IsType<ExperimentResult>(simulation.Result().Experiment);
        Assert.Equal(new[] { 0, 7 }, result.MovementSamples.Select(sample => sample.Tick)); Assert.Equal(16, result.MovementTrace.Length); Assert.Null(result.DeathTick);
    }
    [Fact]
    public void CircuitAdvancesOnlyAfterArrivalAndDoesNotConsumeRandom()
    {
        var simulation = Create(Catalog()); var world = simulation.World;
        world.Lord = new(3000, 3000); world.Experiment!.CircuitIndex = 0;
        var draws = simulation.Result().RandomDraws; simulation.Experiment!.MoveLord();
        Assert.Equal(1, world.Experiment.CircuitIndex); Assert.Equal(new Position(3024, 3000), world.Lord); Assert.Equal(draws, simulation.Result().RandomDraws);
    }
    [Fact]
    public void HarvestUsesNearestRipeThenStableIdAndFallsBackToCircuit()
    {
        var simulation = Create(Catalog(), movement: "harvest"); var world = simulation.World;
        world.Farms.Add(new() { Id = 200, Position = new(6100, 6000), Stage = 3 });
        world.Farms.Add(new() { Id = 100, Position = new(5900, 6000), Stage = 3 });
        simulation.Experiment!.MoveLord(); Assert.Equal(new Position(5900, 6000), world.Destination);
        world.Tick = 90; world.Farms.Clear(); simulation.Experiment.MoveLord(); Assert.Equal(new Position(3000, 3000), world.Destination);
    }
    [Fact]
    public void EvadeUsesAllLivingTargetsCoincidentPositiveXAndMapClamp()
    {
        var simulation = Create(Catalog(), movement: "evade"); var world = simulation.World;
        world.Enemies.Add(new() { Id = 1, Health = 0, Position = new(6000, 6000) });
        world.Enemies.Add(new() { Id = 3, Health = 100, Definition = "core:seed_mite", Position = new(6000, 6000) });
        simulation.Experiment!.MoveLord(); Assert.Equal(new Position(7200, 6000), world.Destination);
        world.Tick = 90; world.Lord = new(11999, 6000); world.Enemies[1].Position = world.Lord;
        simulation.Experiment.MoveLord(); Assert.Equal(12000, world.Destination.X);
    }
    [Fact]
    public void NonlinearCurveMatchesPolynomialAndSaturatesWithoutOverflow()
    {
        var simulation = Create(Catalog());
        Assert.Equal(30, simulation.Experiment!.RequiredExperience(1)); Assert.Equal(72, simulation.Experiment.RequiredExperience(3));
        Assert.Equal(long.MaxValue, simulation.Experiment.RequiredExperience(int.MaxValue));
    }
    [Fact]
    public void MixedSelectsLeastOwnedFourFamilyAndIgnoresUpgradeRank()
    {
        var catalog = Catalog(); var simulation = Create(catalog); var world = simulation.World;
        world.Runtime!.Charters["core:meal_oath"] = 100;
        world.PendingCards = ["core:iron_blade", "core:carpenter_hammer", "core:muster_horn"];
        var progression = new ProgressionSystem(catalog, new(42, "core:founder", "core:meadow", "mixed"), world, new(42), simulation.Runtime, simulation.Experiment);
        // Force a real level deal with only these three cards eligible.
        foreach (var id in catalog.Weapons.Keys.Concat(catalog.Tools.Keys).Concat(catalog.Runtime!.Charters.Keys).Except(world.PendingCards)) { world.BannedCards.Add(id); }
        world.PendingCards = []; world.Experience = 30; progression.Tick();
        Assert.Equal("core:carpenter_hammer", world.Cards.Single().Chosen);
    }
    [Theory]
    [InlineData("harvest", false)]
    [InlineData("destroyed", false)]
    [InlineData("duration-censored", true)]
    [InlineData("death-censored", true)]
    public void RipeEpisodesRecordExactObservedAgeAndSeparateCompetingOutcomes(string outcome, bool censored)
    {
        var simulation = Create(Catalog()); var world = simulation.World;
        var farm = new FarmState { Id = 100, Stage = 3 }; world.Farms.Add(farm);
        world.Tick = 9; simulation.Experiment!.Ripe(farm); world.Tick = 19;
        if (censored) { world.Tick = 20; if (outcome == "death-censored") { world.LordHealth = 0; } simulation.Experiment.End(); }
        else { simulation.Experiment.CloseRipe(farm, outcome); }
        var result = simulation.Result(); var entry = Assert.Single(result.Experiment!.FarmWaitEvents);
        Assert.Equal(10, entry.RipeTick); Assert.Equal(20, entry.EndTick); Assert.Equal(10, entry.ObservedWaitTicks); Assert.Equal(censored, entry.Censored); Assert.Equal(outcome, entry.EndKind);
        Assert.Equal(result.Hash, simulation.Result().Hash);
    }
    [Fact]
    public void ProfileOverridesAreIsolatedAndTraceStopsAtRealDeath()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data"); var old = ContentLoader.Load(data, false, "s4-stage-one"); var current = ContentLoader.Load(data, false, "s4b-01");
        Assert.Null(old.Experiment); Assert.Equal(3000, old.Tuning.World.Map.LordHealth); Assert.Equal(900, current.Tuning.World.Map.LordHealth);
        Assert.Equal(10, old.Enemies["core:raider"].Speed); Assert.Equal(30, current.Enemies["core:raider"].Speed);
        var simulation = Create(current); simulation.World.LordHealth = 1; simulation.World.People.Clear(); simulation.World.Equipment.Clear();
        simulation.World.Enemies.Add(new() { Id = simulation.World.AllocateId(), Definition = "core:raider", Health = 100, Position = simulation.World.Lord });
        simulation.Tick(); Assert.True(simulation.IsComplete); var result = simulation.Result(); Assert.Equal(1, result.Experiment!.DeathTick); Assert.Equal(4, result.Experiment.MovementTrace.Length);
    }

    [Fact]
    public void LegacyS4CommittedCrossPlatformHashIsUnchanged()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "s4-stage-one");
        catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 900 } };
        var result = SimulationTests.Finish(catalog, new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "weapon", "A"));
        Assert.Equal("4BA4C83F44050B7B606A2929164BB86D15E270FAEED6BD80A80AC27E49548938", result.Hash);
    }
    [Fact]
    public void RealRipeTransitionHarvestAndCensorAreMeasuredByFarmLoop()
    {
        var catalog = Catalog(); var worldTuning = catalog.Tuning.World;
        catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 3, World = worldTuning with { Farms = worldTuning.Farms with { StageTicks = [1, 1, 1, 1] } } } };
        var simulation = Create(catalog); var world = simulation.World;
        world.Farms.Add(new() { Id = world.AllocateId(), Stage = 2, Source = "core:seed_bag", Position = world.Lord });
        simulation.Tick();
        var harvested = Assert.Single(simulation.Result().Experiment!.FarmWaitEvents);
        Assert.Equal("harvest", harvested.EndKind); Assert.Equal(1, harvested.RipeTick); Assert.Equal(1, harvested.EndTick);
        var far = new FarmState { Id = world.AllocateId(), Stage = 2, Source = "core:seed_bag", Position = new(0, 0) }; world.Farms.Add(far);
        world.People.Clear(); while (!simulation.IsComplete) { simulation.Tick(); }
        Assert.Contains(simulation.Result().Experiment!.FarmWaitEvents, entry => entry.FarmId == far.Id && entry.EndKind == "duration-censored" && entry.ObservedWaitTicks == 1);
    }

}
