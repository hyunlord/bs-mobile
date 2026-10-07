using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldBehaviorTests
{
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), true);

    [Fact]
    public void LordRemainsInsideBoundedMapAndVisitsAllSeasons()
    {
        var catalog = Catalog();
        var simulation = new Simulation(catalog, new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed"));
        var seasons = new HashSet<int>();
        while (!simulation.IsComplete)
        {
            simulation.Tick();
            var state = simulation.Snapshot;
            Assert.InRange(state.LordX, 0, catalog.Tuning.World.Map.Width);
            Assert.InRange(state.LordY, 0, catalog.Tuning.World.Map.Height);
            seasons.Add(state.Season);
        }
        Assert.Equal(catalog.Tuning.World.Seasons.Length, seasons.Count);
    }

    [Fact]
    public void LoadFixtureContainsRealRequestedEntitiesBeforeAndAfterTick()
    {
        var catalog = Catalog();
        var simulation = new Simulation(catalog, new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", "C", "load"));
        simulation.PrepareLoadTick();
        var before = simulation.Snapshot;
        for (var tick = 0; tick < 600; tick++)
        {
            simulation.Tick();
            Assert.Equal(catalog.Tuning.World.Load.People, simulation.Snapshot.People);
        }
        var after = simulation.Snapshot;
        Assert.Equal(catalog.Tuning.World.Load.Enemies, before.ActiveEnemies);
        Assert.Equal(catalog.Tuning.World.Load.Enemies, after.ActiveEnemies);
        Assert.Equal(catalog.Tuning.World.Load.Farms, after.Farms);
        Assert.Equal(catalog.Tuning.World.Load.Buildings, after.Buildings);
        Assert.Equal(catalog.Tuning.World.Load.People, after.People);
    }
}
