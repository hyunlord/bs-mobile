using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 0; index < args.Length; index++)
{
    var key = args[index];
    if (key == "--include-test") { options.Add(key, "true"); }
    else if (key.StartsWith("--", StringComparison.Ordinal) && index + 1 < args.Length) { options.Add(key, args[++index]); }
    else { throw new ArgumentException($"Invalid option: {key}"); }
}
var catalog = ContentLoader.Load(options.GetValueOrDefault("--data", "data"), options.ContainsKey("--include-test"));
var seed = int.Parse(options.GetValueOrDefault("--seed", "42"), CultureInfo.InvariantCulture);
var iterations = int.Parse(options.GetValueOrDefault("--iterations", "3"), CultureInfo.InvariantCulture);
if (iterations <= 0) { throw new ArgumentException("iterations must be positive"); }
var run = new RunOptions(seed, options.GetValueOrDefault("--hero", catalog.Tuning.DefaultHero),
    options.GetValueOrDefault("--estate", catalog.Tuning.DefaultEstate), options.GetValueOrDefault("--policy", "mixed"));
var samples = new List<double>();
var results = new List<SimulationResult>();
// Warm-up is excluded from timing; measured samples are individual tick wall times.
var warmup = new Simulation(catalog, run);
while (!warmup.IsComplete) { warmup.Tick(); }
for (var repeat = 0; repeat < iterations; repeat++)
{
    var simulation = new Simulation(catalog, run);
    while (!simulation.IsComplete)
    {
        var start = Stopwatch.GetTimestamp();
        simulation.Tick();
        samples.Add((Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency);
    }
    results.Add(simulation.Result());
}
if (results.Select(result => result.Hash).Distinct().Count() != 1)
{
    throw new InvalidOperationException("Determinism failure: identical input produced different hashes.");
}
samples.Sort();
var metrics = new
{
    schemaVersion = 1,
    model = "scaffold",
    config = new { run.HeroId, run.EstateId, catalog.Tuning.TickRate, catalog.Tuning.DurationTicks, catalog.Tuning.DamageRollMax },
    stage = "S0",
    scope = "synthetic-scaffold",
    gameplayBalanceClaim = false,
    commit = Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "local-uncommitted",
    timestamp = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    runtime = Environment.Version.ToString(),
    seed,
    policy = run.Policy,
    iterations,
    ticks = catalog.Tuning.DurationTicks,
    tickP95Ms = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1],
    tickSampleCount = samples.Count,
    stopwatchFrequency = Stopwatch.Frequency,
    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
    damage = results[0].Damage,
    growth = results[0].Growth,
    hash = results[0].Hash,
    balanceDispersion = (double?)null
};
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var resultJson = JsonSerializer.Serialize(results, jsonOptions);
if (options.TryGetValue("--output", out var output)) { Write(output, resultJson); }
if (options.TryGetValue("--metrics", out var metricsOutput)) { Write(metricsOutput, JsonSerializer.Serialize(metrics, jsonOptions)); }
Console.WriteLine(resultJson);

static void Write(string path, string content)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    File.WriteAllText(path, content + Environment.NewLine);
}
