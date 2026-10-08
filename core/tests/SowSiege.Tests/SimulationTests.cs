using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class SimulationTests
{
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase0-r2", "data"), true);

    [Fact]
    public void SameSeedProducesSameHashThreeTimes()
    {
        var catalog = Catalog();
        var hashes = Enumerable.Range(0, 3).Select(_ => Finish(catalog, new(42, "core:founder", "core:meadow", "mixed")).Hash).ToArray();
        Assert.Single(hashes.Distinct());
    }

    [Fact]
    public void DifferentSeedsChangeObservableState()
    {
        var catalog = Catalog();
        var first = Finish(catalog, new(42, "core:founder", "core:meadow", "mixed"));
        var second = Finish(catalog, new(43, "core:founder", "core:meadow", "mixed"));
        Assert.NotEqual(first.Damage, second.Damage);
    }

    [Theory]
    [InlineData("core:founder", "core:meadow")]
    [InlineData("test:scout", "test:moor")]
    public void BothHeroEstateCombinationsCompleteWithActivationAndGrowth(string hero, string estate)
    {
        var catalog = Catalog();
        var result = Finish(catalog, new(42, hero, estate, "mixed"));
        Assert.Equal(catalog.Tuning.DurationTicks, result.Ticks);
        Assert.True(result.Damage > 0);
        Assert.True(result.Growth > 0);
    }

    [Fact]
    public void EveryStartingToolAndSynergyReferenceExists()
    {
        var catalog = Catalog();
        Assert.All(catalog.Heroes.Values, hero => Assert.True(catalog.Tools.ContainsKey(hero.StartingTool)));
        Assert.All(catalog.Tools.Values, tool => Assert.All(tool.AntiSynergy, id => Assert.True(catalog.Tools.ContainsKey(id))));
    }

    [Fact]
    public void TuningChangesActualDamage()
    {
        var catalog = Catalog();
        var baseline = Finish(catalog, new(42, "core:founder", "core:meadow", "mixed"));
        var modified = catalog with { Tuning = catalog.Tuning with { DamageRollMax = catalog.Tuning.DamageRollMax + 10 } };
        Assert.NotEqual(baseline.Damage, Finish(modified, new(42, "core:founder", "core:meadow", "mixed")).Damage);
    }

    internal static SimulationResult Finish(ContentCatalog catalog, RunOptions options)
    {
        var simulation = new Simulation(catalog, options);
        while (!simulation.IsComplete) { simulation.Tick(); }
        return simulation.Result();
    }
}
