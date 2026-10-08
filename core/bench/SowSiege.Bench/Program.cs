using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SowSiege.Core;
using SowSiege.Sim;

BenchmarkSwitcher.FromAssembly(typeof(GameplayBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
public class GameplayBenchmarks
{
    private ContentCatalog catalog = null!;
    private Simulation simulation = null!;

    [GlobalSetup]
    public void Setup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "data", "tuning.json"))) { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository data directory not found.");
        catalog = ContentLoader.Load(Path.Combine(root, "data"));
        var load = catalog.Tuning.World.Load;
        if (load.Enemies != 1000 || load.Farms != 300 || load.Buildings != 20 || load.People != 60)
        {
            throw new InvalidOperationException("S2 load gate requires exactly 1000 enemies, 300 farms, 20 buildings and 60 people.");
        }
    }

    [IterationSetup]
    public void PrepareMeasuredTick()
    {
        simulation = SimulationFactory.Create(catalog, new RunOptions(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", Scenario: "load"));
        for (var tick = 0; tick < 120; tick++)
        {
            AssertLoad();
            if (simulation.IsComplete) { throw new InvalidOperationException("Load fixture ended during warmup."); }
            simulation.Tick();
            AssertLoad();
        }
    }

    [Benchmark(Description = "S2 actual load tick; entity refill and count assertions included")]
    public WorldSnapshot LoadedTick()
    {
        AssertLoad();
        if (simulation.IsComplete) { throw new InvalidOperationException("Load fixture ended before measurement."); }
        simulation.Tick();
        AssertLoad();
        return simulation.Snapshot;
    }

    private void AssertLoad()
    {
        var actual = simulation.Snapshot;
        var expected = catalog.Tuning.World.Load;
        if (actual.ActiveEnemies != expected.Enemies || actual.Farms != expected.Farms
            || actual.Buildings != expected.Buildings || actual.People != expected.People)
        {
            throw new InvalidOperationException($"Active load changed at tick {actual.Tick}: enemies={actual.ActiveEnemies}, farms={actual.Farms}, buildings={actual.Buildings}, people={actual.People}.");
        }
    }
}
