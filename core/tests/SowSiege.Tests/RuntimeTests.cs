using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class RuntimeTests
{
    internal static ContentCatalog Catalog()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase0-r2", "data"), true);
        return catalog with { Runtime = new(new(5, 4, 4, 300, 10, 1, 0, 100, 8, 1, [new("test:chest", "chest", 1, 0)]), new Dictionary<string, EquipmentRuntimeDefinition>(), new Dictionary<string, CharterDefinition>(), new Dictionary<string, ItemDefinition>(), new Dictionary<string, EvolutionDefinition>()) };
    }
    internal static Simulation Create(ContentCatalog catalog) => new(catalog, new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed"));

    [Fact]
    public void RuntimeRecordsActualInitialWorldBeforeFirstTick()
    {
        var simulation = Create(Catalog());
        var sample = Assert.Single(simulation.Result().Timeline);
        Assert.Equal(0, sample.Tick);
        Assert.Equal(simulation.Snapshot.People, sample.People);
        Assert.Equal(0, sample.WeaponDamage);
    }

    [Fact]
    public void RepeatedLootHasUnlimitedStacksAndIsNeverACard()
    {
        var catalog = Catalog();
        catalog = catalog with { Runtime = catalog.Runtime! with { Items = new Dictionary<string, ItemDefinition> { ["test:item"] = new("test:item", [], []) } } };
        var simulation = Create(catalog);
        for (var i = 0; i < 20; i++) { simulation.Tick(); }
        var runtime = Assert.IsType<RuntimeResult>(simulation.Result().Runtime);
        Assert.Equal(20, runtime.Build.ItemStacks["test:item"]);
        Assert.Equal(20, runtime.Loot.Count);
        Assert.DoesNotContain(simulation.Result().Cards.SelectMany(card => card.Offered), id => id == "test:item");
    }

    [Fact]
    public void MixedGrowthCreatesBuildingAndRemovesWorkerIntoGuard()
    {
        var catalog = Catalog();
        var tool = catalog.Tools.Values.First(tool => tool.Growth.Target == "building");
        catalog = catalog with { Runtime = catalog.Runtime! with { Equipment = new Dictionary<string, EquipmentRuntimeDefinition> { [tool.Id] = new(tool.Id, [new("building", "construct", 1, 0), new("people", "garrison", 1, 300)], []) } } };
        var simulation = Create(catalog);
        simulation.World.Lord = simulation.World.Buildings[0].Position;
        simulation.World.Equipment.Clear();
        simulation.World.Equipment.Add(new() { Id = tool.Id });
        simulation.Tick();
        Assert.Contains(simulation.World.People, person => person.Role == "guard");
        var growth = simulation.Result().Runtime!.ToolGrowthByTarget[tool.Id];
        Assert.True(growth["building"] > 0);
        Assert.True(growth["people"] > 0);
    }
    [Fact]
    public void UnacquiredEffectsRemainExplicitZeroRows()
    {
        var catalog = Catalog();
        catalog = catalog with { Runtime = catalog.Runtime! with { Items = new Dictionary<string, ItemDefinition> { ["test:item"] = new("test:item", [], [new("test:effect", "modifier", "stat-add", "attack-damage", 1, 0, 0, 0, [])]) } } };
        var simulation = Create(catalog);
        var effect = Assert.Single(simulation.Result().Runtime!.Effects);
        Assert.Equal(0, effect.ActivationCount); Assert.Equal(0, effect.AppliedTotal); Assert.Null(effect.FirstActivationTick); Assert.Null(effect.LastActivationTick);
    }

}
