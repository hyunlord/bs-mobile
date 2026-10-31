using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;
namespace SowSiege.Sim;
public static class CombinedShowcaseFixtureCli
{
    public static void Equip(InteractiveSession session, WaveCombinedShowcaseCase review, Action<ReplayCommand> apply)
    {
        foreach (var entry in review.Cases)
        {
            if (session.View.CaptureFrame().Equipment.Any(e => e.Id == entry.Id)) { continue; }
            apply(new(session.NextSequence, 0, ReplayCommandKind.GrantLevel));
            if (!session.View.CaptureCards().Cards.Contains(entry.Id)) { throw new InvalidOperationException("Required review source was not offered: " + entry.Id); }
            apply(new(session.NextSequence, 0, ReplayCommandKind.ChooseCard, CardId: entry.Id));
        }
    }
    public static int Verify(string[] args)
    {
        if (args.Length != 4) { throw new ArgumentException("combined-showcase-replay <frozen-C05-data> <combined-json> <replay>"); }
        var baseline = ContentLoader.Load(args[1], profileName: "wave-1a");
        using var document = JsonDocument.Parse(File.ReadAllText(args[2]));
        var review = document.RootElement.GetProperty("combined").Deserialize<WaveCombinedShowcaseCase>(ShowcaseFixtureCli.Json)!;
        var catalog = ReviewShowcaseCatalog.CreateCombined(baseline, review);
        using var input = File.OpenRead(args[3]);
        var result = ReplayRunner.Verify(catalog, ReviewShowcaseCatalog.Hash(catalog), ReplayCodec.Read(input));
        Console.WriteLine(JsonSerializer.Serialize(result, ShowcaseFixtureCli.Json));
        return 0;
    }
    public static int Run(string[] args)
    {
        if (args.Length != 4) { throw new ArgumentException("combined-showcase-fixture <frozen-C05-data> <combined-json> <output>"); }
        var baseline = ContentLoader.Load(args[1], profileName: "wave-1a");
        var baselineDataHash = ContentLoader.Hash(args[1], false);
        if (baselineDataHash != "942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC")
        { throw new ArgumentException("Requires original frozen C05 data."); }
        using var document = JsonDocument.Parse(File.ReadAllText(args[2]));
        var review = document.RootElement.GetProperty("combined").Deserialize<WaveCombinedShowcaseCase>(ShowcaseFixtureCli.Json)!;
        var sources = document.RootElement.GetProperty("sources").Clone();
        var catalog = ReviewShowcaseCatalog.CreateCombined(baseline, review);
        var hash = ReviewShowcaseCatalog.Hash(catalog);
        var options = new InteractiveOptions(new(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, hash,
            Field: new(FieldOptions.ReviewVersion, review.Arena.SpawnRadius, review.Arena.RecycleRadius), MeleeAim: new());
        var session = new InteractiveSession(catalog, options);
        Directory.CreateDirectory(args[3]);
        var prefix = Path.Combine(args[3], "combined-evolved");
        var minimum = int.MaxValue; var maximum = 0; var tick = 0;
        var startCounts = new Dictionary<string, long>();
        using (var replay = File.Create(prefix + ".ssreplay"))
        using (var samples = new StreamWriter(prefix + ".density.csv"))
        using (var events = new StreamWriter(prefix + ".events.csv"))
        {
            samples.WriteLine("tick,enemies,window,lordHealth");
            events.WriteLine("tick,eventId,kind,source,subjectId,x,y,targetX,targetY,amount");
            ReplayCodec.WriteHeader(replay, ReplayCodec.Header(options));
            void Apply(ReplayCommand command) { session.Apply(command); ReplayCodec.WriteCommand(replay, command); }
            Equip(session, review, Apply);
            for (; tick < review.Arena.DurationTicks; tick++)
            {
                if (tick == review.CaptureStartTick)
                {
                    var counters = ((IWaveRunView)session.View).CaptureWaveRuntime()!.Counters;
                    foreach (var entry in review.Cases) { startCounts[entry.Id] = counters.GetValueOrDefault("activation-started:" + entry.Id); }
                }
                Apply(new(session.NextSequence, tick, ReplayCommandKind.Advance, ShowcaseFixtureCli.WalkingInput(tick)));
                var frame = session.View.CaptureFrame();
                var window = tick >= review.CaptureStartTick;
                if (window) { minimum = Math.Min(minimum, frame.Enemies.Count); maximum = Math.Max(maximum, frame.Enemies.Count); }
                samples.WriteLine($"{frame.Tick},{frame.Enemies.Count},{window},{frame.Lord.Health}");
                foreach (var e in ((IWaveRunView)session.View).CaptureWaveRuntime()!.Events)
                { events.WriteLine($"{e.Tick},{e.Id},{e.Kind},{e.Source},{e.SubjectId},{e.Position.X},{e.Position.Y},{e.Target.X},{e.Target.Y},{e.Amount}"); }
            }
            ReplayCodec.WriteEnd(replay, new(session.NextSequence, tick, ReplayEndKind.Duration, session.ComputeStateHash()));
        }
        using var input = File.OpenRead(prefix + ".ssreplay");
        var verified = ReplayRunner.Verify(catalog, hash, ReplayCodec.Read(input));
        var finalCounters = ((IWaveRunView)session.View).CaptureWaveRuntime()!.Counters;
        var activationCounts = review.Cases.ToDictionary(e => e.Id, e => finalCounters.GetValueOrDefault("activation-started:" + e.Id) - startCounts[e.Id]);
        if (minimum < 800 || activationCounts.Values.Any(value => value <= 0))
        { throw new InvalidOperationException($"Combined review dense-window gate failed: minimum {minimum}; activations {JsonSerializer.Serialize(activationCounts)}"); }
        var sidecar = new
        {
            contract = "m1-combat-combined-v1",
            baselineDataHash,
            baselineCatalogHash = ReviewShowcaseCatalog.Hash(baseline),
            catalogHash = hash,
            replaySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prefix + ".ssreplay"))).ToLowerInvariant(),
            captureStartTick = review.CaptureStartTick,
            endTick = tick,
            stateHash = verified.StateHash,
            combined = review,
            sources,
            minimumEnemies = minimum,
            maximumEnemies = maximum,
            activationCounts,
            scope = "review-only eight real Core sources; finite high health and seven weapon slots are explicit stress hypotheses; 800+ is global alive count, not viewport count; preparation GrantLevel and offered ChooseCard commands recorded; no invulnerability",
            inputContract = "square-walk-v2: right/up/left/down, 30 ticks per edge"
        };
        File.WriteAllText(prefix + ".case.json", JsonSerializer.Serialize(sidecar, ShowcaseFixtureCli.Json));
        Console.WriteLine(JsonSerializer.Serialize(new { ticks = tick, minimum, maximum, activationCounts, verified.StateHash }, ShowcaseFixtureCli.Json));
        return 0;
    }
}
