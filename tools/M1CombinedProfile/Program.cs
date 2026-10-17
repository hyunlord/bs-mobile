using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;

if (args.Length is < 4 or > 5)
{
    Console.Error.WriteLine("M1CombinedProfile <frozen-C05-data> <combined-json> <replay> <output-stem> [runs=3]");
    return 2;
}
var runs = args.Length == 5 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 3;
if (runs < 1) { throw new ArgumentOutOfRangeException(nameof(runs)); }
var stem = Path.GetFullPath(args[3]);
var outputs = Enumerable.Range(0, runs).Select(run => $"{stem}-{run}.csv")
    .Append(stem + "-summary.json").ToArray();
foreach (var output in outputs)
{
    if (File.Exists(output)) { throw new IOException("Refusing to overwrite evidence: " + output); }
}
var baseline = ContentLoader.Load(args[0], profileName: "wave-1a");
using var json = JsonDocument.Parse(File.ReadAllText(args[1]));
var review = json.RootElement.GetProperty("combined").Deserialize<WaveCombinedShowcaseCase>(ShowcaseFixtureCli.Json)
    ?? throw new InvalidDataException("Missing combined definition.");
var catalog = ReviewShowcaseCatalog.CreateCombined(baseline, review);
using var file = File.OpenRead(args[2]);
var replay = ReplayCodec.Read(file);
var catalogHash = ReviewShowcaseCatalog.Hash(catalog);
if (replay.Header.Options.DataHash != catalogHash) { throw new InvalidDataException("Replay catalog hash differs."); }
var sampleCount = replay.Commands.Count(command => command.Kind == ReplayCommandKind.Advance && command.Tick >= review.CaptureStartTick);
if (sampleCount == 0) { throw new InvalidDataException("No measurement-window advances."); }
Directory.CreateDirectory(Path.GetDirectoryName(stem)!);
var summaries = new List<object>(runs);
var stages = Enum.GetValues<RuntimeStage>();
for (var run = 0; run < runs; run++)
{
    var session = new InteractiveSession(catalog, replay.Header.Options);
    var diagnostics = new StageDiagnostics(stages);
    session.Diagnostics = diagnostics;
    // Reserve observer storage before advancing; no file I/O or observer row allocation inside a measured tick.
    var rows = Enumerable.Range(0, sampleCount).Select(_ => new double[3 + stages.Length * 2]).ToArray();
    var rowIndex = 0;
    foreach (var command in replay.Commands)
    {
        diagnostics.Reset();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        session.Apply(command);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (command.Kind != ReplayCommandKind.Advance || command.Tick < review.CaptureStartTick) { continue; }
        var row = rows[rowIndex++];
        row[0] = command.Tick; row[1] = elapsed; row[2] = allocated;
        for (var i = 0; i < stages.Length; i++)
        {
            row[3 + i] = diagnostics.Milliseconds[i];
            row[3 + stages.Length + i] = diagnostics.Bytes[i];
        }
    }
    var hash = session.ComputeStateHash();
    if (hash != replay.End.StateHash) { throw new InvalidDataException("Terminal hash mismatch: " + hash); }
    using (var output = new StreamWriter(new FileStream(outputs[run], FileMode.CreateNew, FileAccess.Write)))
    {
        output.WriteLine("tick,totalMs,totalAllocatedBytes," + string.Join(',', stages.Select(stage => stage + "Ms"))
            + "," + string.Join(',', stages.Select(stage => stage + "Bytes")));
        foreach (var row in rows) { output.WriteLine(string.Join(',', row.Select(value => value.ToString("R", CultureInfo.InvariantCulture)))); }
    }
    var summary = new
    {
        run,
        hash,
        samples = rows.Length,
        total = Statistics(rows.Select(row => row[1])),
        meanAllocatedBytes = rows.Average(row => row[2]),
        stages = stages.Select((stage, index) => new
        {
            stage = stage.ToString(),
            time = Statistics(rows.Select(row => row[3 + index])),
            meanAllocatedBytes = rows.Average(row => row[3 + stages.Length + index])
        }).ToArray()
    };
    summaries.Add(summary);
    Console.WriteLine(JsonSerializer.Serialize(summary));
}
var core = typeof(InteractiveSession).Assembly;
using (var output = new FileStream(outputs[^1], FileMode.CreateNew, FileAccess.Write))
{
    JsonSerializer.Serialize(output, new
    {
        scope = "Diagnostic .NET host only; nested stages overlap; no Unity, renderer, native frame or device acceptance claim.",
        quantile = "nearest-rank: sorted[ceil(p * N) - 1]",
        fineStagesEnabled = RuntimeDiagnosticsCapabilities.FineStagesEnabled,
        catalogHash,
        replaySha256 = Hash(args[2]),
        reviewSha256 = Hash(args[1]),
        baselineDataHash = ContentLoader.Hash(args[0], false),
        core = new { location = core.Location, sha256 = Hash(core.Location), mvid = core.ManifestModule.ModuleVersionId },
        environment = new { RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture, Environment.ProcessorCount },
        captureStartTick = review.CaptureStartTick,
        replay.End.Tick,
        summaries
    }, new JsonSerializerOptions { WriteIndented = true });
}
return 0;

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static object Statistics(IEnumerable<double> values)
{
    var sorted = values.Order().ToArray();
    double Quantile(double p) => sorted[Math.Max(0, (int)Math.Ceiling(p * sorted.Length) - 1)];
    return new { mean = sorted.Average(), p50 = Quantile(0.50), p95 = Quantile(0.95), p99 = Quantile(0.99), max = sorted[^1] };
}

internal sealed class StageDiagnostics : IRuntimeDiagnostics
{
    private readonly Dictionary<RuntimeStage, int> indexes;
    private readonly long[] starts, allocations;
    internal double[] Milliseconds { get; }
    internal long[] Bytes { get; }
    internal StageDiagnostics(RuntimeStage[] stages)
    {
        indexes = stages.Select((stage, index) => (stage, index)).ToDictionary(pair => pair.stage, pair => pair.index);
        Milliseconds = new double[stages.Length]; Bytes = new long[stages.Length];
        starts = new long[stages.Length]; allocations = new long[stages.Length];
    }
    internal void Reset() { Array.Clear(Milliseconds); Array.Clear(Bytes); }
    public void Begin(RuntimeStage stage)
    {
        var index = indexes[stage];
        starts[index] = Stopwatch.GetTimestamp(); allocations[index] = GC.GetAllocatedBytesForCurrentThread();
    }
    public void End(RuntimeStage stage)
    {
        var index = indexes[stage];
        Milliseconds[index] += Stopwatch.GetElapsedTime(starts[index]).TotalMilliseconds;
        Bytes[index] += GC.GetAllocatedBytesForCurrentThread() - allocations[index];
    }
}
