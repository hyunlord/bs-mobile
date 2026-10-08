using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class RemainsTests
{
    [Fact]
    public void DeathWithoutFarmRetainsIndependentRemains()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var estate = JsonSerializer.Deserialize<EstateDefinition>("{\"Id\":\"test:estate\",\"GrowthMultiplier\":1,\"RemainsLoop\":{\"Capacity\":2,\"LifetimeTicks\":10,\"AbsorptionRadius\":100}}")!;
        catalog = catalog with { Estates = new Dictionary<string, EstateDefinition> { [estate.Id] = estate } };
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, estate.Id, "mixed");
        var simulation = new Simulation(catalog, options);
        simulation.World.Farms.Clear();
        simulation.World.Enemies.Add(new() { Id = 999, Definition = catalog.Enemies.Keys.First(), Position = simulation.World.Lord, Health = 0 });
        var nextId = simulation.World.NextId;
        new CombatSystem(catalog, options, simulation.World, new(9000), new(catalog.Tuning.World.Map.CellSize)).ResolveDeaths();
        var result = JsonSerializer.SerializeToElement(simulation.Result());
        Assert.True(result.TryGetProperty("Remains", out var remains));
        Assert.Equal(1, remains.GetProperty("Active").GetInt32());
        Assert.Equal(nextId, simulation.World.NextId);
    }
    private static (ContentCatalog Catalog, RunOptions Options, Simulation Simulation) Fixture(int capacity = 2, int lifetime = 10, int radius = 100)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        var estates = catalog.Estates.ToDictionary(pair => pair.Key, pair => pair.Value);
        estates[catalog.Tuning.DefaultEstate] = estates[catalog.Tuning.DefaultEstate] with { RemainsLoop = new(capacity, lifetime, radius) };
        catalog = catalog with { Estates = estates };
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed");
        var simulation = new Simulation(catalog, options);
        simulation.World.Farms.Clear(); simulation.World.People.Clear(); simulation.World.Enemies.Clear();
        return (catalog, options, simulation);
    }

    [Fact]
    public void LaterFarmAbsorbsOnceWithInclusiveRadiusAndStableTie()
    {
        var (_, _, sim) = Fixture(); var world = sim.World;
        RemainsSystem.Create(world, new() { Id = 99, Position = new(0, 0) });
        var fartherId = new FarmState { Id = 20, Position = new(100, 0) };
        var lowerId = new FarmState { Id = 10, Position = new(-100, 0) };
        world.Farms.Add(fartherId); world.Farms.Add(lowerId);
        RemainsSystem.Absorb(world, 3); Assert.Equal(0, lowerId.Fertility);
        world.Tick = 1; RemainsSystem.Absorb(world, 3); RemainsSystem.Absorb(world, 3);
        Assert.Equal(3, lowerId.Fertility); Assert.Equal(0, fartherId.Fertility);
        Assert.Equal(1, world.Remains!.Absorbed); Assert.Empty(world.Remains.Live);
    }

    [Fact]
    public void CapacityExpiryAndDistanceConserveAdmittedRemains()
    {
        var (_, _, sim) = Fixture(capacity: 1, lifetime: 2); var world = sim.World;
        RemainsSystem.Create(world, new() { Position = new(0, 0) });
        RemainsSystem.Create(world, new() { Position = new(0, 0) });
        world.Farms.Add(new() { Id = 10, Position = new(101, 0) });
        world.Tick = 2; RemainsSystem.Absorb(world, 3); Assert.Single(world.Remains!.Live);
        world.Tick = 3; world.Farms[0].Position = new(0, 0); RemainsSystem.Absorb(world, 3);
        var result = RemainsSystem.Result(world)!;
        Assert.Equal(1, result.Created); Assert.Equal(1, result.Dropped); Assert.Equal(1, result.Expired);
        Assert.Equal(result.Created, result.Absorbed + result.Expired + result.Active);
        Assert.Equal(0, world.Farms[0].Fertility);
    }

    [Fact]
    public void ActualFertilityAcceleratesCropAndHarvestCreditsOnlyOnce()
    {
        var (catalog, options, sim) = Fixture(); var world = sim.World;
        var farms = catalog.Tuning.World.Farms with { StageTicks = [4, 4, 4, 1], FertilityGrowthBonus = 2, FertilityPerKill = 6 };
        var seasons = catalog.Tuning.World.Seasons.Select(season => season with { GrowthMultiplier = 1 }).ToArray();
        catalog = catalog with { Tuning = catalog.Tuning with { World = catalog.Tuning.World with { Farms = farms, Seasons = seasons } } };
        var farm = new FarmState { Id = 10, Position = world.Lord }; world.Farms.Add(farm);
        RemainsSystem.Create(world, new() { Position = world.Lord }); world.Tick = 1;
        var estate = new EstateSystem(catalog, options, world, "C", new(100));
        var controlWorld = new WorldState { Lord = world.Lord, Tick = world.Tick };
        var controlFarm = new FarmState { Id = 10, Position = world.Lord }; controlWorld.Farms.Add(controlFarm);
        var controlEstate = new EstateSystem(catalog, options, controlWorld, "C", new(100));
        estate.Tick(); controlEstate.Tick(); Assert.Equal(3, farm.Progress); Assert.Equal(1, controlFarm.Progress); Assert.Equal(1, world.Remains!.FertilityConsumed);
        estate.Tick(); controlEstate.Tick(); Assert.Equal(1, farm.Stage); Assert.Equal(0, controlFarm.Stage);
        var fertileTicks = 2; var controlTicks = 2;
        while (world.Harvests == 0) { world.Tick++; estate.Tick(); fertileTicks++; }
        while (controlWorld.Harvests == 0) { controlWorld.Tick++; controlEstate.Tick(); controlTicks++; }
        Assert.True(fertileTicks < controlTicks);
        Assert.Equal(1, world.Remains.FertilizedHarvests); Assert.True(world.HarvestExperience > 0);
        estate.Harvest(farm); Assert.Equal(1, world.Remains.FertilizedHarvests);
        Assert.Equal(world.Remains.FertilityConsumed * 2, world.Remains.GrowthBonusApplied);
    }

    [Fact]
    public void RipeStorageAndDestroyedCropDoNotInventHarvestAttribution()
    {
        var (_, _, sim) = Fixture(); var world = sim.World;
        var farm = new FarmState { Id = 10, Position = world.Lord, Stage = 3 }; world.Farms.Add(farm);
        RemainsSystem.Create(world, new() { Position = world.Lord }); world.Tick = 1; RemainsSystem.Absorb(world, 3);
        RemainsSystem.Harvest(world, farm); Assert.Equal(3, farm.Fertility); Assert.Equal(0, world.Remains!.FertilizedHarvests);
        farm.Fertility--; RemainsSystem.Consumed(world, farm, 2);
        RemainsSystem.Destroyed(world, farm); farm.Fertility = 0;
        RemainsSystem.Harvest(world, farm); Assert.Equal(0, world.Remains.FertilizedHarvests);
        Assert.Empty(world.Remains.FarmCredits); Assert.Empty(world.Remains.FertilizedCycles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveDefaultAndDummyProduceDeterministicActualLoop(bool dummy)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), dummy);
        var hero = dummy ? catalog.Heroes.Values.Single(value => value.Id != catalog.Tuning.DefaultHero) : catalog.Heroes[catalog.Tuning.DefaultHero];
        var estate = dummy ? catalog.Estates.Values.Single(value => value.Id != catalog.Tuning.DefaultEstate) : catalog.Estates[catalog.Tuning.DefaultEstate];
        Assert.NotNull(estate.RemainsLoop);
        var hashes = new List<string>();
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var options = new RunOptions(9000, hero.Id, estate.Id, "mixed"); var sim = new Simulation(catalog, options); var world = sim.World;
            world.Equipment.Clear(); world.People.Clear(); world.Enemies.Clear(); world.Farms.Clear(); world.Food = 0;
            var farm = new FarmState { Id = world.AllocateId(), Position = world.Lord, Source = hero.StartingTool }; world.Farms.Add(farm);
            var nextId = world.NextId; var random = new TrackedRandom(9000);
            world.Enemies.Add(new() { Id = 999, Definition = catalog.Enemies.Keys.First(), Position = world.Lord });
            new CombatSystem(catalog, options, world, random, new(100)).ResolveDeaths();
            Assert.Equal(nextId, world.NextId); Assert.Equal(0, random.Draws);
            var system = new EstateSystem(catalog, options, world, "C", new(100));
            while (world.Harvests == 0 && world.Tick < catalog.Tuning.DurationTicks) { world.Tick++; system.Tick(); }
            var result = sim.Result(); var proof = result.Remains!;
            Assert.True(proof.FertilityTransferred > 0); Assert.True(proof.GrowthBonusApplied > 0); Assert.Equal(1, proof.FertilizedHarvests);
            Assert.True(result.HarvestExperience > 0); Assert.Equal(catalog.Tuning.World.Farms.FoodPerHarvest, result.Food);
            Assert.Equal(proof.Created, proof.Absorbed + proof.Expired + proof.Active);
            Assert.Equal(result.Hash, sim.Result().Hash); hashes.Add(result.Hash);
        }
        Assert.Single(hashes.Distinct());
    }

    [Fact]
    public void ActualCropAttackClearsTransferredCreditsAndCycleMarker()
    {
        var (catalog, options, sim) = Fixture(); var world = sim.World;
        var farm = new FarmState { Id = 10, Position = world.Lord, Stage = 3, Fertility = 5 }; world.Farms.Add(farm);
        world.Remains!.FarmCredits[farm.Id] = 5; world.Remains.FertilizedCycles.Add(farm.Id);
        var enemy = catalog.Enemies.Values.First(value => value.Target == "ripe");
        world.Enemies.Add(new() { Id = 20, Definition = enemy.Id, Position = farm.Position, Health = enemy.Health });
        new CombatSystem(catalog, options, world, new(9000), new(100)).ResolveEnemyAttacks();
        Assert.Equal(0, farm.Stage); Assert.Equal(0, farm.Fertility);
        Assert.Empty(world.Remains.FarmCredits); Assert.Empty(world.Remains.FertilizedCycles);
        new EstateSystem(catalog, options, world, "C", new(100)).Harvest(farm);
        Assert.Equal(0, world.Remains.FertilizedHarvests);
    }

    [Fact]
    public void ActualTickSamplesConserveAndResultDoesNotMutateState()
    {
        var (catalog, options, _) = Fixture(); catalog = catalog with { Tuning = catalog.Tuning with { DurationTicks = 7 } };
        var sim = new Simulation(catalog, options);
        sim.World.Enemies.Add(new() { Id = 999, Definition = catalog.Enemies.Keys.First(), Position = sim.World.Lord, Health = 0 });
        while (!sim.IsComplete) { sim.Tick(); }
        var result = sim.Result(); var remains = result.Remains!;
        Assert.Equal(new[] { 0, 7 }, remains.Samples.Select(sample => sample.Tick));
        Assert.All(remains.Samples, sample => Assert.Equal(sample.Created, sample.Absorbed + sample.Expired + sample.Active));
        Assert.Equal(remains.Created, remains.Absorbed + remains.Expired + remains.Active);
        Assert.True(remains.Created > 0); Assert.Equal(result.Hash, sim.Result().Hash);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("zero")]
    [InlineData("too-large")]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("fraction")]
    public void ActiveEstateRejectsInvalidRemainsConfig(string mutation)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "data");
        var root = Path.Combine(Path.GetTempPath(), "r3-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var estateId = ContentLoader.Load(source).Tuning.DefaultEstate;
            var path = Directory.GetFiles(Path.Combine(root, "estates"), "*.json").Single(file => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!["id"]!.GetValue<string>() == estateId);
            var record = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            switch (mutation)
            {
                case "null": record["remainsLoop"] = null; break;
                case "zero": record["remainsLoop"]!["capacity"] = 0; break;
                case "too-large": record["remainsLoop"]!["lifetimeTicks"] = 1000001; break;
                case "missing": record["remainsLoop"]!.AsObject().Remove("absorptionRadius"); break;
                case "unknown": record["remainsLoop"]!["invented"] = 1; break;
                case "fraction": record["remainsLoop"]!["capacity"] = 1.5; break;
            }
            File.WriteAllText(path, record.ToJsonString());
            Assert.ThrowsAny<Exception>(() => ContentLoader.Load(root));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }

}
