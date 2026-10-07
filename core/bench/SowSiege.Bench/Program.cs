using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SowSiege.Core;
using SowSiege.Sim;

BenchmarkSwitcher.FromAssembly(typeof(ScaffoldBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
public class ScaffoldBenchmarks
{
    private ContentCatalog catalog = null!;
    private RunOptions options = null!;

    [GlobalSetup]
    public void Setup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "data", "tuning.json"))) { directory = directory.Parent; }
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository data directory not found.");
        catalog = ContentLoader.Load(Path.Combine(root, "data"));
        options = new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed");
    }

    [Benchmark(Description = "S0 synthetic full run (not S2 combat)")]
    public SimulationResult FullRun()
    {
        var simulation = new Simulation(catalog, options);
        while (!simulation.IsComplete) { simulation.Tick(); }
        return simulation.Result();
    }
}
