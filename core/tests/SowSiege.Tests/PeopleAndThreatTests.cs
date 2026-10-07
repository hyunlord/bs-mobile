using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class PeopleAndThreatTests
{
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), true);
    private static RunOptions Options(ContentCatalog catalog, string rule = "C") => new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", rule);

    [Theory]
    [InlineData(3, 4, 100, 88)]
    [InlineData(1_000_000, 3_000, 1_000_000, 0)]
    public void ConsumptionUsesConfiguredFoodPerMemberWithoutIntegerOverflow(int foodPerPerson, int members, int initialFood, int expectedFood)
    {
        var catalog = Catalog();
        catalog = catalog with { Tuning = catalog.Tuning with { World = catalog.Tuning.World with { People = catalog.Tuning.World.People with { FoodPerPerson = foodPerPerson, FoodCapacity = Math.Max(catalog.Tuning.World.People.FoodCapacity, initialFood), MaxPeople = Math.Max(catalog.Tuning.World.People.MaxPeople, members), SquadSize = members } } } };
        var simulation = new Simulation(catalog, Options(catalog));
        var world = simulation.World;
        world.People.Clear();
        world.People.Add(new() { Id = world.AllocateId(), Position = world.Estate, Destination = world.Estate, Role = "militia", Members = members, Health = 1, DutyUntil = int.MaxValue });
        world.Food = initialFood;
        world.Tick = catalog.Tuning.World.People.ConsumePeriodTicks;
        new EstateSystem(catalog, Options(catalog), world, "C", new(catalog.Tuning.World.Map.CellSize)).Tick();
        Assert.Equal(expectedFood, world.Food);
    }

    [Fact]
    public void MilitiaIsOneSquadAndReturningMembersResumeWorkOnlyAfterArrival()
    {
        var catalog = Catalog();
        catalog = catalog with { Tuning = catalog.Tuning with { World = catalog.Tuning.World with { People = catalog.Tuning.World.People with { RecruitPeriodTicks = int.MaxValue } } } };
        var simulation = new Simulation(catalog, Options(catalog));
        var world = simulation.World;
        var estate = new EstateSystem(catalog, Options(catalog), world, "C", new(catalog.Tuning.World.Map.CellSize));
        var horn = catalog.Tools.Values.First(tool => tool.Growth.Target == "people");
        var population = world.People.Sum(person => person.Members);
        estate.ApplyGrowth(horn);
        var squad = Assert.Single(world.People, person => person.Role == "militia");
        Assert.Equal(catalog.Tuning.World.People.SquadSize, squad.Members);
        Assert.Equal(population, world.People.Sum(person => person.Members));
        world.Lord = new(world.Estate.X + 1000, world.Estate.Y);
        squad.Position = world.Lord;
        world.Tick = squad.DutyUntil - 1;
        estate.Tick();
        Assert.Equal("militia", squad.Role);
        world.Tick++;
        estate.Tick();
        Assert.Equal("returning", squad.Role);
        Assert.NotEqual(world.Estate, squad.Position);
        while (squad.Role == "returning") { world.Tick++; estate.Tick(); }
        Assert.Equal("peasant", squad.Role);
        Assert.Equal(1, squad.Members);
        Assert.Equal(population, world.People.Sum(person => person.Members));
    }

    [Fact]
    public void DeadPeopleAreRemovedAndHornCanRecoverPopulationWithinFoodCap()
    {
        var catalog = Catalog();
        var simulation = new Simulation(catalog, Options(catalog));
        var world = simulation.World;
        world.People.Clear();
        world.People.Add(new() { Id = world.AllocateId(), Position = world.Lord, Health = 1 });
        var definition = catalog.Enemies.Values.First(enemy => enemy.Target == "lord");
        world.Enemies.Add(new() { Id = world.AllocateId(), Definition = definition.Id, Position = world.Lord, Health = definition.Health });
        var spatial = new SpatialHash(catalog.Tuning.World.Map.CellSize);
        var combat = new CombatSystem(catalog, Options(catalog), world, new(42), spatial);
        combat.ResolveEnemyAttacks();
        combat.ResolveDeaths();
        Assert.Empty(world.People);
        new EstateSystem(catalog, Options(catalog), world, "C", spatial).ApplyGrowth(catalog.Tools.Values.First(tool => tool.Growth.Target == "people"));
        Assert.NotEmpty(world.People);
        Assert.True(world.People.Sum(person => person.Members) <= world.Food / catalog.Tuning.World.People.FoodPerPerson + 1);
    }

    [Fact]
    public void WoundedReturningSquadDoesNotCreateHealthWhenItSplits()
    {
        var catalog = Catalog();
        var simulation = new Simulation(catalog, Options(catalog));
        var world = simulation.World;
        world.People.Clear();
        world.Tick = 1;
        world.People.Add(new() { Id = world.AllocateId(), Position = world.Estate, Destination = world.Estate, Role = "returning", Members = 5, Health = 1 });
        new EstateSystem(catalog, Options(catalog), world, "C", new(catalog.Tuning.World.Map.CellSize)).Tick();
        Assert.Equal(1, world.People.Sum(person => person.Health));
        Assert.All(world.People, person => Assert.True(person.Health > 0));
    }

    [Theory]
    [InlineData("lord")]
    [InlineData("seed")]
    [InlineData("ripe")]
    [InlineData("building")]
    public void EnemySelectsItsDeclaredTargetWhenAvailable(string target)
    {
        var catalog = Catalog();
        var simulation = new Simulation(catalog, Options(catalog));
        var world = simulation.World;
        world.Tick = 1;
        world.Farms.Add(new() { Id = world.AllocateId(), Position = new(100, 100), Stage = 0 });
        world.Farms.Add(new() { Id = world.AllocateId(), Position = new(200, 200), Stage = catalog.Tuning.World.Farms.StageTicks.Length - 1 });
        world.Buildings[0].Built = true;
        world.Buildings[0].Health = catalog.Tuning.World.Buildings.Health;
        var definition = catalog.Enemies.Values.First(enemy => enemy.Target == target);
        var enemy = new EnemyState { Id = world.AllocateId(), Definition = definition.Id, Position = new(0, 0), Health = definition.Health };
        world.Enemies.Add(enemy);
        new CombatSystem(catalog, Options(catalog), world, new(42), new(catalog.Tuning.World.Map.CellSize)).SpawnAndMoveEnemies();
        Assert.Equal(target, enemy.LastTarget);
    }

    [Fact]
    public void ProsperityIncreasesActualSpawnCount()
    {
        var catalog = Catalog();
        var low = new Simulation(catalog, Options(catalog));
        var high = new Simulation(catalog, Options(catalog));
        low.World.Food = 0;
        high.World.Food = catalog.Tuning.World.People.FoodCapacity;
        low.Tick(); high.Tick();
        Assert.True(high.World.SpawnedEnemies > low.World.SpawnedEnemies);
    }
}
