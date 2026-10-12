using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;
namespace SowSiege.Sim;
public static class ShowcaseFixtureCli
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static PlayerInput WalkingInput(int tick) => (tick / 90 % 4) switch
    {
        0 => new(PlayerInput.Scale, 0),
        1 => new(0, PlayerInput.Scale),
        2 => new(-PlayerInput.Scale, 0),
        _ => new(0, -PlayerInput.Scale)
    };
    public static int Run(string[] args)
    {

        if (args.Length != 4)
        {
            throw new ArgumentException("showcase-fixtures <frozen-C05-data> <case-json> <output>");
        }

        var baseline = ContentLoader.Load(args[1], profileName: "wave-1a");
        var baselineDataHash = ContentLoader.Hash(args[1], false);
        if (baselineDataHash != "942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC")
        {
            throw new ArgumentException("Requires original frozen C05 data; not current production data.");

        }

        using var document = JsonDocument.Parse(File.ReadAllText(args[2]));
        var cases = document.RootElement.GetProperty("cases").Deserialize<WaveShowcaseCase[]>(Json)!;
        using var visualDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[2])!, "m1-weapon-showcase.json")));
        var visuals = visualDocument.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString()!, e => e);
        Directory.CreateDirectory(args[3]);
        using var summary = new StreamWriter(Path.Combine(args[3], "summary.csv"));
        summary.WriteLine("id,stage,ticks,positiveHits,kills,windowHits,windowKills,maximumEnemies,hash");
        foreach (var review in cases)
        {
            var catalog = ReviewShowcaseCatalog.Create(baseline, review);
            var hash = ReviewShowcaseCatalog.Hash(catalog);
            var key = review.Id.Replace(':', '-') + "-" + review.Stage;
            var prefix = Path.Combine(args[3], key);
            var options = new InteractiveOptions(new(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, hash, MeleeAim: new());
            var session = new InteractiveSession(catalog, options);
            var hits = 0; var kills = 0; var windowHits = 0; var windowKills = 0; var maximumEnemies = 0; var tick = 0;
            using (var output = File.Create(prefix + ".ssreplay"))
            using (var events = new StreamWriter(prefix + ".events.csv"))
            {
                events.WriteLine("tick,eventId,kind,source,subjectId,x,y,targetX,targetY,amount");
                ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
                while (session.View.Status != RunStatus.Completed && tick < review.Arena.DurationTicks)
                {
                    var command = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance, WalkingInput(tick));
                    session.Apply(command); ReplayCodec.WriteCommand(output, command); tick++;
                    var frame = session.View.CaptureFrame(); maximumEnemies = Math.Max(maximumEnemies, frame.Enemies.Count);
                    foreach (var e in ((IWaveRunView)session.View).CaptureWaveRuntime()!.Events)
                    {
                        events.WriteLine($"{e.Tick},{e.Id},{e.Kind},{e.Source},{e.SubjectId},{e.Position.X},{e.Position.Y},{e.Target.X},{e.Target.Y},{e.Amount}");

                        if (e.Source == review.Id && e.Amount > 0 && e.Kind is "hit" or "group-hit" or "trained-group-hit")
                        {
                            hits++; if (e.Tick >= 300)
                            {
                                windowHits++;
                            }
                        }
                        if (e.Kind == "enemy-killed")
                        {
                            kills++; if (e.Tick >= 300)
                            {
                                windowKills++;
                            }
                        }
                    }
                }
                ReplayCodec.WriteEnd(output, new(session.NextSequence, tick, tick == 600 ? ReplayEndKind.Duration : ReplayEndKind.Death, session.ComputeStateHash()));
            }
            using var input = File.OpenRead(prefix + ".ssreplay");
            var verified = ReplayRunner.Verify(catalog, hash, ReplayCodec.Read(input));
            var visual = visuals[review.Id];
            var shape = visual.GetProperty("shape").GetString()!;
            var audioCue = visual.GetProperty("cue").GetString()!;
            var sidecar = new
            {
                contract = "m1-combat-case-v1",
                baselineDataHash,
                baselineCatalogHash = ReviewShowcaseCatalog.Hash(baseline),
                catalogHash = hash,
                replaySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prefix + ".ssreplay"))).ToLowerInvariant(),
                captureStartTick = 300,
                endTick = tick,
                shape,
                audioCue,
                review.Id,
                review.DesignRef,
                review.ProposedContract,
                review.Stage,
                review.Classification,
                review.Gear,
                review.Arena,
                inputContract = "square-walk-v1: full movement, right/up/left/down, 90 ticks per edge, same input for all 24 cases",
                scope = "review-only numeric hypotheses; 10s setup +10s capture; evolved is preconfigured proposal, not earned progression",
                positiveHits = hits,
                kills,
                windowHits,
                windowKills,
                maximumEnemies,
                stateHash = verified.StateHash
            };
            File.WriteAllText(prefix + ".case.json", JsonSerializer.Serialize(sidecar, Json));
            summary.WriteLine($"{review.Id},{review.Stage},{tick},{hits},{kills},{windowHits},{windowKills},{maximumEnemies},{verified.StateHash}");
        }
        return 0;
    }
}
