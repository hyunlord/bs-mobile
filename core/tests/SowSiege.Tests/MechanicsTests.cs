using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class MechanicsTests
{
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase0-r2", "data"), true);
    private static RunOptions Options(ContentCatalog catalog, string rule = "C") => new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", rule);
    private static ToolDefinition Tool(ContentCatalog catalog, string target) => catalog.Tools.Values.First(tool => tool.Growth.Target == target && tool.Id.StartsWith("core:", StringComparison.Ordinal));

    [Theory]
    [InlineData("land")]
    [InlineData("building")]
    [InlineData("people")]
    public void EveryToolDamagesRealEnemyAndChangesActualWorld(string target)
    {
        var catalog = Catalog();
        var simulation = SimulationFactory.Create(catalog, Options(catalog));
        var world = simulation.World;
        var tool = Tool(catalog, target);
        var enemy = new EnemyState { Id = world.AllocateId(), Definition = catalog.Enemies.Keys.First(), Position = new(world.Lord.X + 1, world.Lord.Y), Health = 1000 };
        world.Enemies.Add(enemy);
        var spatial = new SpatialHash(catalog.Tuning.World.Map.CellSize);
        spatial.Rebuild(world.Enemies);
        new CombatSystem(catalog, Options(catalog), world, new(42), spatial).Activate(new() { Id = tool.Id }, tool.Activation, world.Tools[tool.Id]);
        new EstateSystem(catalog, Options(catalog), world, "C", spatial).ApplyGrowth(tool);
        Assert.True(enemy.Health < 1000);
        Assert.True(world.Tools[tool.Id].ActivationDamage > 0);
        switch (target)
        {
            case "land": Assert.NotEmpty(world.Farms); break;
            case "building": Assert.Contains(world.Buildings, building => building.Built && building.Health > 0); break;
            case "people": Assert.Contains(world.People, person => person.Role == "militia"); break;
        }
    }

    [Fact]
    public void FarmVisitsFourStagesThenHarvestAddsExperienceAndBoundedFood()
    {
        var catalog = Catalog();
        var worldTuning = catalog.Tuning.World;
        catalog = catalog with { Tuning = catalog.Tuning with { World = worldTuning with { Farms = worldTuning.Farms with { StageTicks = [1, 1, 1, 2] } } } };
        var simulation = SimulationFactory.Create(catalog, Options(catalog));
        var world = simulation.World;
        var estate = new EstateSystem(catalog, Options(catalog), world, "C", new(catalog.Tuning.World.Map.CellSize));
        estate.ApplyGrowth(Tool(catalog, "land"));
        var farm = Assert.Single(world.Farms);
        var stages = new HashSet<int> { farm.Stage };
        for (var index = 0; index < 4; index++) { estate.Tick(); stages.Add(farm.Stage); world.Tick++; }
        Assert.Equal(4, stages.Count);
        Assert.True(world.Harvests > 0);
        Assert.True(world.HarvestExperience > 0);
        Assert.InRange(world.Food, 0, catalog.Tuning.World.People.FoodCapacity);
    }

    [Fact]
    public void DestroyedBuildingRemainsRuinAndHammerRebuildsIt()
    {
        var catalog = Catalog();
        var simulation = SimulationFactory.Create(catalog, Options(catalog));
        var world = simulation.World;
        var tool = Tool(catalog, "building");
        var estate = new EstateSystem(catalog, Options(catalog), world, "C", new(catalog.Tuning.World.Map.CellSize));
        estate.ApplyGrowth(tool);
        var building = world.Buildings.First(building => building.Built);
        world.People.Clear();
        building.Health = 1;
        world.Lord = building.Position;
        var definition = catalog.Enemies.Values.First(enemy => enemy.Target == "building");
        world.Enemies.Add(new() { Id = world.AllocateId(), Definition = definition.Id, Health = definition.Health, Position = building.Position });
        new CombatSystem(catalog, Options(catalog), world, new(42), new(catalog.Tuning.World.Map.CellSize)).ResolveEnemyAttacks();
        Assert.True(building.Built);
        Assert.Equal(0, building.Health);
        estate.ApplyGrowth(tool);
        Assert.True(building.Health > 0);
        Assert.Equal(1, world.Rebuilds);
    }

    [Fact]
    public void WorkerHornChangesActualGrowthRatherThanOnlyLedger()
    {
        var catalog = Catalog();
        var first = SimulationFactory.Create(catalog, Options(catalog, "B"));
        var second = SimulationFactory.Create(catalog, Options(catalog, "B"));
        var firstEstate = new EstateSystem(catalog, Options(catalog), first.World, "B", new(catalog.Tuning.World.Map.CellSize));
        var secondEstate = new EstateSystem(catalog, Options(catalog), second.World, "B", new(catalog.Tuning.World.Map.CellSize));
        firstEstate.ApplyGrowth(Tool(catalog, "land")); secondEstate.ApplyGrowth(Tool(catalog, "land"));
        firstEstate.ApplyGrowth(Tool(catalog, "people"));
        firstEstate.Tick(); secondEstate.Tick();
        Assert.True(first.World.Farms[0].Progress > second.World.Farms[0].Progress);
        Assert.Contains(first.World.People, person => person.Role == "peasant" && person.DutyUntil > first.World.Tick);
    }

    [Fact]
    public void FirstKillingEnemyOwnsDeathCause()
    {
        var catalog = Catalog();
        var simulation = SimulationFactory.Create(catalog, Options(catalog));
        var world = simulation.World;
        world.People.Clear();
        world.LordHealth = 1;
        var enemies = catalog.Enemies.Values.Take(2).ToArray();
        foreach (var definition in enemies)
        {
            world.Enemies.Add(new() { Id = world.AllocateId(), Definition = definition.Id, Position = world.Lord, Health = definition.Health });
        }
        new CombatSystem(catalog, Options(catalog), world, new(42), new(catalog.Tuning.World.Map.CellSize)).ResolveEnemyAttacks();
        Assert.Equal(enemies[0].Id, world.DeathCause);
    }

    [Fact]
    public void LevelHasNoGameplayCapAndOffersThreeDistinctCardsWithRarity()
    {
        var catalog = Catalog();
        var simulation = SimulationFactory.Create(catalog, Options(catalog));
        simulation.World.Level = 1000;
        simulation.World.Experience = catalog.Tuning.World.Progression.BaseExperience + 1000L * catalog.Tuning.World.Progression.ExperiencePerLevel;
        new ProgressionSystem(catalog, Options(catalog), simulation.World, new(42)).Tick();
        Assert.True(simulation.World.Level > 1000);
        Assert.All(simulation.World.Cards, card =>
        {
            Assert.Equal(3, card.Offered.Distinct().Count());
            Assert.Contains(card.Chosen, card.Offered);
            Assert.Contains(catalog.Tuning.World.Progression.Rarities, rarity => rarity.Name == card.Rarity);
        });
    }

    [Fact]
    public void BaselineAllyDamageDoesNotInflateWeaponOrToolLedgers()
    {
        var catalog = Catalog();
        var simulation = SimulationFactory.Create(catalog, Options(catalog));
        var world = simulation.World;
        world.Enemies.Add(new() { Id = world.AllocateId(), Definition = catalog.Enemies.Keys.First(), Position = world.Estate, Health = 1000 });
        var spatial = new SpatialHash(catalog.Tuning.World.Map.CellSize);
        spatial.Rebuild(world.Enemies);
        new EstateSystem(catalog, Options(catalog), world, "C", spatial).Tick();
        Assert.True(world.AllyDamage > 0);
        Assert.Equal(0, world.WeaponDamage);
        Assert.All(world.Tools.Values, ledger => Assert.Equal(0, ledger.GrowthDamage));
    }

    [Fact]
    public void ReorderedContentDictionariesDoNotChangeStateHash()
    {
        var catalog = Catalog();
        var reversed = catalog with { Tools = catalog.Tools.Reverse().ToDictionary(), Enemies = catalog.Enemies.Reverse().ToDictionary() };
        var first = SimulationFactory.Create(catalog, Options(catalog));
        var second = SimulationFactory.Create(reversed, Options(catalog));
        for (var tick = 0; tick < 120; tick++) { first.Tick(); second.Tick(); }
        Assert.Equal(first.Result().Hash, second.Result().Hash);
    }
}
