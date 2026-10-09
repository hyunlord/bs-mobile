using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record MetaParityObservation(int Seed, int Chapter, int Repeat, int Tick, long Commands,
    string CatalogHash, string GameplayHash, string ReplayContextHash, string ReplayVerifiedHash,
    string SettlementHash, string IdleHash, string[] MigratedSaveHashes);

public static class MetaParityCli
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "meta-parity-probe" && args[0] != "meta-replay-fixtures")
        {
            return false;
        }
        if (args.Length != 3)
        {
            throw new ArgumentException("meta-parity-probe DATA_ROOT OUTPUT_JSON | meta-replay-fixtures DATA_ROOT OUTPUT_DIR");
        }
        string data = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]);
        string dataHash = ContentLoader.Hash(data, false);
        var content = ContentLoader.Load(data, profileName: "first-playable");
        var meta = MetaContentLoader.Load(data);
        bool fixtures = args[0] == "meta-replay-fixtures";
        if (fixtures)
        {
            Directory.CreateDirectory(output);
        }

        var rows = new List<MetaParityObservation>();
        int[] chapters = [1, 3, 5, 7, 10];
        for (int index = 0; index < chapters.Length; index++)
        {
            for (int repeat = 0; repeat < (fixtures ? 1 : 3); repeat++)
            {
                rows.Add(Observe(meta, content, dataHash, 52000 + index, chapters[index], repeat, fixtures ? output : null));
            }
        }
        if (dataHash != ContentLoader.Hash(data, false))
        {
            throw new IOException("Data changed during meta parity.");
        }
        if (fixtures)
        {
            Console.WriteLine($"Generated {rows.Count} .NET meta replay/context fixture pairs: {output}; 300 ticks each, not full-run evidence.");
            return true;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, new
        {
            schemaVersion = 1,
            dataHash,
            coreAssembly = CoreAssemblyMetadata.VerifyHostBinding(),
            observations = rows,
            scope = "300 actual interactive ticks; fixture chapter unlocks; partial abandonment settlement; not full-run balance or Unity runtime evidence"
        }, HostJson.CreateOptions(camelCase: true, indented: true));
        return true;
    }

    public static MetaParityObservation Observe(MetaCatalog meta, ContentCatalog content, string dataHash, int seed, int chapterIndex, int repeat, string? fixtureDirectory = null)
    {
        var chapter = meta.Chapters.Single(c => c.Index == chapterIndex);
        // Predeclared fixture unlocks reach different chapters without pretending that 300 ticks clear them.
        var initial = MetaEngine.NewGame(meta) with { HighestClearedChapter = chapterIndex - 1 };
        var plan = MetaEngine.BeginRun(meta, initial, chapter.Id, seed);
        var projected = MetaRunAdapter.ProjectCatalog(content, meta, plan.State, plan);
        byte[] context = MetaReplayContext.Encode(plan, dataHash, projected);
        var options = new InteractiveOptions(new RunOptions(seed, content.Tuning.DefaultHero, content.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.NearestEnemy, dataHash);
        var session = new InteractiveSession(projected, options);
        var commands = new List<ReplayCommand>();
        while (session.View.CaptureFrame().Tick < 300 && session.View.CaptureFrame().Status != RunStatus.Completed)
        {
            int tick = session.View.CaptureFrame().Tick;
            var command = session.View.CaptureFrame().Status == RunStatus.AwaitingCard
                ? new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard, CardId: session.View.CaptureCards().Cards.Order(StringComparer.Ordinal).First())
                : new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance, tick % 120 < 60 ? new(1000, 0) : new(0, 1000));
            session.Apply(command);
            commands.Add(command);
        }
        var summary = session.GetSummary();
        var end = summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit;
        var replay = new ReplayDocument(ReplayCodec.Header(options), commands, [], new(session.NextSequence, summary.Tick, end, summary.StateHash));
        var verified = MetaReplayContext.Verify(context, content, meta, dataHash, replay);
        if (fixtureDirectory != null)
        {
            string path = Path.Combine(fixtureDirectory, seed + ".ssreplay");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                ReplayCodec.WriteHeader(stream, replay.Header);
                foreach (var command in replay.Commands)
                {
                    ReplayCodec.WriteCommand(stream, command);
                }

                ReplayCodec.WriteEnd(stream, replay.End);
            }
            File.WriteAllBytes(path + ".meta", context);
        }
        var facts = MetaRunAdapter.FromInteractive(summary, session.View.CaptureFrame(), session.View.CaptureFirstPlayable()!, true);
        var settlement = MetaEngine.SettleRun(meta, plan.State, plan.Run, facts);
        var clock = MetaEngine.AdvanceIdle(meta, settlement.State, 1000, 100000);
        var idle = MetaEngine.AdvanceIdle(meta, clock.State, 1600, 100600);
        if (MetaValidation.ValidateState(meta, idle.State).Length != 0)
        {
            throw new InvalidDataException("Parity produced invalid meta state.");
        }
        var migrated = Enumerable.Range(1, 3).Select(version =>
        {
            var decoded = MetaSaveCodec.Decode(MetaSaveCodec.EncodeVersion(idle.State, version));
            if (!decoded.Valid || decoded.State == null)
            {
                throw new InvalidDataException("Parity save migration failed.");
            }
            return Hash(MetaSaveCodec.Encode(decoded.State));
        }).ToArray();
        return new(seed, chapterIndex, repeat, summary.Tick, session.NextSequence, Hash(JsonSerializer.SerializeToUtf8Bytes(projected, HostJson.CreateOptions())), summary.StateHash,
            Hash(context), verified.StateHash, Hash(MetaSaveCodec.Encode(settlement.State)), Hash(MetaSaveCodec.Encode(idle.State)), migrated);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
