using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class InteractiveCli
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length != 3 || args[0] is not ("interactive-fixtures" or "interactive-replay")) { throw new ArgumentException("Usage: interactive-fixtures <data-directory> <output-directory> | interactive-replay <data-directory> <replay-file>"); }
            var catalog = ContentLoader.Load(args[1], profileName: "production"); var dataHash = ContentLoader.Hash(args[1], false);
            if (args[0] == "interactive-replay")
            {
                using var input = File.OpenRead(args[2]); var verification = ReplayRunner.Verify(catalog, dataHash, ReplayCodec.Read(input));
                Console.WriteLine(JsonSerializer.Serialize(verification)); return 0;
            }
            Directory.CreateDirectory(args[2]); var results = new List<ReplayVerification>();
            for (var seed = 30000; seed <= 30004; seed++)
            {
                var path = Path.Combine(args[2], $"{seed}.ssreplay");
                using (var output = File.Create(path)) { RecordFixture(catalog, dataHash, seed, output); }
                using var input = File.OpenRead(path); results.Add(ReplayRunner.Verify(catalog, dataHash, ReplayCodec.Read(input)));
                Console.WriteLine(JsonSerializer.Serialize(results[^1]));
            }
            File.WriteAllText(Path.Combine(args[2], "hashes.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or OverflowException)
        {
            Console.Error.WriteLine("Interactive replay failed: " + ex.Message); return 1;
        }
    }
    public static ReplayEnd RecordFixture(ContentCatalog catalog, string dataHash, int seed, Stream output)
    {
        var options = new InteractiveOptions(new(seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, dataHash);
        var session = new InteractiveSession(catalog, options); ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        var tick = 0;
        void Send(ReplayCommandKind kind, PlayerInput input = default, string? card = null, int value = 0)
        {
            var command = new ReplayCommand(session.NextSequence, tick, kind, input, card, value); session.Apply(command); ReplayCodec.WriteCommand(output, command);
        }
        Send(ReplayCommandKind.SetInvulnerable, value: 1); Send(ReplayCommandKind.GrantLevel);
        var initial = session.View.CaptureCards(); Send(ReplayCommandKind.LockCard, card: initial.Cards[0]); Send(ReplayCommandKind.RerollCards);
        var refreshed = session.View.CaptureCards(); Send(ReplayCommandKind.BanCard, card: refreshed.Cards.First(id => id != initial.Cards[0]));
        Send(ReplayCommandKind.ChooseCard, card: initial.Cards[0]);
        while (session.View.Status != RunStatus.Completed)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                var cards = session.View.CaptureCards(); Send(ReplayCommandKind.ChooseCard, card: cards.Cards[(seed + tick) % cards.Cards.Count]); continue;
            }
            if (tick % 1800 == 0)
            {
                Send(ReplayCommandKind.SetAimMode, value: (tick / 1800) % 2);
                Send(ReplayCommandKind.SetSpawnPermille, value: tick % 3600 == 0 ? PlayerInput.Scale : PlayerInput.Scale / 2);
            }
            var phase = (tick / 180) % 9;
            var input = phase switch
            {
                0 => new PlayerInput(0, 0),
                1 => new PlayerInput(1000, 0),
                2 => new PlayerInput(1000, 1000),
                3 => new PlayerInput(0, 1000),
                4 => new PlayerInput(-1000, 1000),
                5 => new PlayerInput(-1000, 0),
                6 => new PlayerInput(-1000, -1000),
                7 => new PlayerInput(0, -1000),
                _ => new PlayerInput(1000, -1000)
            };
            Send(ReplayCommandKind.Advance, input); tick++;
            if (tick % 1800 == 0) { ReplayCodec.WriteCheckpoint(output, new(session.NextSequence, tick, session.ComputeStateHash())); }
        }
        var summary = session.GetSummary(); var end = new ReplayEnd(session.NextSequence, tick, summary.Survived ? ReplayEndKind.Duration : ReplayEndKind.Death, summary.StateHash);
        ReplayCodec.WriteEnd(output, end); return end;
    }
}
