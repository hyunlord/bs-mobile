using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class ForgeWalkFixtureCli
{
    public const int EndTick = 900;
    public const int Seed = 30001;

    public static ReplayEnd Record(ContentCatalog catalog, string dataHash, Stream output)
    {
        if (catalog.WaveRuntime is null) { throw new ArgumentException("Forge walk requires the ordinary wave-1a catalog."); }
        var options = new InteractiveOptions(new(Seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true),
            AimMode.Movement, dataHash, Feel: new(FasterLord: true), Field: new());
        var session = new InteractiveSession(catalog, options);
        ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        while (session.View.CaptureFrame().Tick < EndTick)
        {
            var frame = session.View.CaptureFrame();
            if (frame.Status == RunStatus.Completed) { throw new InvalidOperationException("Ordinary pilot ended before thirty seconds; do not grant survival."); }
            var command = frame.Status == RunStatus.AwaitingCard
                ? new ReplayCommand(session.NextSequence, frame.Tick, ReplayCommandKind.ChooseCard, CardId: session.View.CaptureCards().Cards[0])
                : new ReplayCommand(session.NextSequence, frame.Tick, ReplayCommandKind.Advance, new(1000, 0));
            session.Apply(command);
            ReplayCodec.WriteCommand(output, command);
        }
        var end = new ReplayEnd(session.NextSequence, EndTick, ReplayEndKind.Quit, session.ComputeStateHash());
        ReplayCodec.WriteEnd(output, end);
        return end;
    }

    public static int Run(string[] args)
    {
        if (args.Length != 3) { throw new ArgumentException("forge-walk-fixture <data-directory> <fresh-output-directory>"); }
        var directory = Path.GetFullPath(args[2]);
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        { throw new IOException("Preserve prior evidence: Forge walk output must be empty."); }
        Directory.CreateDirectory(directory);
        var catalog = ContentLoader.Load(args[1], profileName: "wave-1a");
        var dataHash = ContentLoader.Hash(args[1], false);
        var replayPath = Path.Combine(directory, "forge-walk.ssreplay");
        using (var output = File.Create(replayPath)) { Record(catalog, dataHash, output); }
        using var input = File.OpenRead(replayPath);
        var verified = ReplayRunner.Verify(catalog, dataHash, ReplayCodec.Read(input));
        var sidecar = new
        {
            contract = "forge-walk-review-v1",
            profile = "wave-1a",
            seed = Seed,
            captureStartTick = 0,
            endTick = EndTick,
            direction = "E",
            cameraScale = 1.4f,
            dataHash,
            replaySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(replayPath))).ToLowerInvariant(),
            stateHash = verified.StateHash,
            inputContract = "ordinary east input (1000,0), first offered card only when earned; no grants, no spawn or combat changes",
            presentationContract = "one-direction walking sprite pilot; attacks and damage remain real but hero attack/hit/death poses are unavailable"
        };
        var json = JsonSerializer.Serialize(sidecar, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.ChangeExtension(replayPath, ".json"), json + Environment.NewLine);
        Console.WriteLine(json);
        return 0;
    }
}
