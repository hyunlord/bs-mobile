using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class InteractiveCli
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length is not (3 or 4) || args[0] is not ("interactive-fixtures" or "interactive-replay" or "field-fixture" or "melee-aim-fixture" or "estate-fixture")) { throw new ArgumentException("Usage: interactive-fixtures|field-fixture|melee-aim-fixture|estate-fixture <data-directory> <output-directory> [profile] | interactive-replay <data-directory> <replay-file> [profile]"); }
            var catalog = ContentLoader.Load(args[1], profileName: args.Length == 4 ? args[3] : "production"); var dataHash = ContentLoader.Hash(args[1], false);
            if (args[0] == "interactive-replay")
            {
                using var input = File.OpenRead(args[2]); var verification = MetaReplayFile.Verify(args[2], args[1], catalog, dataHash, ReplayCodec.Read(input));
                Console.WriteLine(JsonSerializer.Serialize(verification)); return 0;
            }
            if (args[0] == "estate-fixture")
            {
                Directory.CreateDirectory(args[2]); var replayPath = Path.Combine(args[2], $"{EstateFixtureSeed}.ssreplay");
                EstateFixtureObservation observation;
                using (var output = File.Create(replayPath)) { observation = RecordEstateFixture(catalog, dataHash, EstateFixtureSeed, output); }
                using var input = File.OpenRead(replayPath); var verification = ReplayRunner.Verify(catalog, dataHash, ReplayCodec.Read(input));
                File.WriteAllText(Path.Combine(args[2], "hashes.json"), JsonSerializer.Serialize(new[] { verification }));
                File.WriteAllText(Path.Combine(args[2], "estate-observation.json"), JsonSerializer.Serialize(observation));
                Console.WriteLine(JsonSerializer.Serialize(verification)); return 0;
            }
            if (args[0] is "field-fixture" or "melee-aim-fixture")
            {
                Directory.CreateDirectory(args[2]); var replayPath = Path.Combine(args[2], "30001.ssreplay");
                RunFrame final;
                using (var output = File.Create(replayPath)) { final = RecordFieldFixture(catalog, dataHash, 30001, output, args[0] == "melee-aim-fixture" ? new() : null); }
                using var input = File.OpenRead(replayPath); var verification = ReplayRunner.Verify(catalog, dataHash, ReplayCodec.Read(input));
                File.WriteAllText(Path.Combine(args[2], "hashes.json"), JsonSerializer.Serialize(new[] { verification }));
                File.WriteAllText(Path.Combine(args[2], "field-observation.json"), JsonSerializer.Serialize(new { formatVersion = args[0] == "melee-aim-fixture" ? 6 : 4, tick = final.Tick, crossedNegativeX = final.Lord.Position.X < 0, lord = final.Lord.Position, field = final.Field }));
                Console.WriteLine(JsonSerializer.Serialize(verification)); return 0;
            }
            Directory.CreateDirectory(args[2]); var results = new List<ReplayVerification>();
            var waveCounters = new Dictionary<int, IReadOnlyDictionary<string, long>>();
            for (var seed = 30000; seed <= 30004; seed++)
            {
                var path = Path.Combine(args[2], $"{seed}.ssreplay");
                using (var output = File.Create(path))
                {
                    if (catalog.WaveRuntime is null) { RecordFixture(catalog, dataHash, seed, output); }
                    else { RecordWaveFixture(catalog, dataHash, seed, output, observed: frame => waveCounters.Add(seed, frame.Counters)); }
                }
                using var input = File.OpenRead(path); results.Add(ReplayRunner.Verify(catalog, dataHash, ReplayCodec.Read(input)));
                Console.WriteLine(JsonSerializer.Serialize(results[^1]));
            }
            File.WriteAllText(Path.Combine(args[2], "hashes.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            if (catalog.WaveRuntime is not null) { File.WriteAllText(Path.Combine(args[2], "wave-counters.json"), JsonSerializer.Serialize(waveCounters)); }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or OverflowException)
        {
            Console.Error.WriteLine("Interactive replay failed: " + ex.Message); return 1;
        }
    }
    public static RunFrame RecordFieldFixture(ContentCatalog catalog, string dataHash, int seed, Stream output, MeleeAimOptions? meleeAim = null)
    {
        if (meleeAim is not null && catalog.WaveRuntime?.Gear.Values.Any(gear => gear.Aim == "nearest-enemy") != true)
        { throw new ArgumentException("Melee aim fixture requires explicitly configured review gear."); }
        var options = new InteractiveOptions(new(seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, dataHash, Field: new(), MeleeAim: meleeAim);
        var session = new InteractiveSession(catalog, options); ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        var tick = 0;
        while (session.View.Status != RunStatus.Completed && tick < 600)
        {
            var cards = session.View.Status == RunStatus.AwaitingCard ? session.View.CaptureCards() : null;
            var command = cards is null ? new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance, new(-1000, 0))
                : new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard, CardId: cards.Cards[(seed + tick) % cards.Cards.Count]);
            session.Apply(command); ReplayCodec.WriteCommand(output, command);
            if (cards is null) { tick++; }
            if (cards is null && tick % 300 == 0) { ReplayCodec.WriteCheckpoint(output, new(session.NextSequence, tick, session.ComputeStateHash())); }
        }
        var summary = session.GetSummary();
        ReplayCodec.WriteEnd(output, new(session.NextSequence, tick, summary.Survived ? ReplayEndKind.Quit : ReplayEndKind.Death, summary.StateHash));
        return session.View.CaptureFrame();
    }
    public static ReplayEnd RecordFixture(ContentCatalog catalog, string dataHash, int seed, Stream output)
    {
        if (catalog.WaveRuntime is not null) { return RecordWaveFixture(catalog, dataHash, seed, output); }
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
    public const int EstateFixtureSeed = 30001;
    public sealed record EstateFixtureObservation(int FormatVersion, string LoadoutId, int Tick, int EndKind, int AllAwakeTick, long Shots, long Heals, long MagnetPulls, long HarvestBonuses, long PullsOutsideGranaries);

    // Format 7 (opt-in estate) fixture: the hero walks to the nearest sleeping building like the bot's "estate-wake" movement and
    // stands in it until it wakes, then tours the woken buildings (watchtower, well, granary) so all three effects fire.
    // Normal input only: no invulnerability or debug grants. It quits once every effect has been seen and 300 more ticks have run.
    public static EstateFixtureObservation RecordEstateFixture(ContentCatalog catalog, string dataHash, int seed, Stream output, string loadout = "L1", int maximumTicks = 12000)
    {
        if (catalog.WaveRuntime?.EstateLight is null) { throw new ArgumentException("Estate fixture requires a profile with an estate light."); }
        var options = new InteractiveOptions(new(seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, dataHash,
            Field: new FieldOptions(), Estate: new(WaveEstateLight.Version, loadout, ReviewShowcaseCatalog.Hash(catalog)));
        var session = new InteractiveSession(catalog, options);
        ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        var light = catalog.WaveRuntime.EstateLight;
        string[] tour = { "core:estate_watchtower", "core:estate_well_shrine", "core:estate_granary" };
        var tick = 0; var allAwake = -1; var quitAt = -1; var stop = 0; var stopSince = 0;
        long shots = 0, heals = 0, pulls = 0, bonuses = 0, strayPulls = 0;
        void Send(ReplayCommandKind kind, PlayerInput input = default, string? card = null)
        {
            var command = new ReplayCommand(session.NextSequence, tick, kind, input, card);
            session.Apply(command); ReplayCodec.WriteCommand(output, command);
        }
        static PlayerInput Toward(WorldPoint from, WorldPoint to, int stand)
        {
            long dx = (long)to.X - from.X, dy = (long)to.Y - from.Y;
            if (dx * dx + dy * dy <= (long)stand * stand) { return default; }
            var length = Math.Sqrt((double)(dx * dx + dy * dy));
            return new((short)Math.Round(dx * PlayerInput.Scale / length), (short)Math.Round(dy * PlayerInput.Scale / length));
        }
        static bool Inside(WorldPoint a, WorldPoint b, int radius) { long dx = (long)a.X - b.X, dy = (long)a.Y - b.Y; return dx * dx + dy * dy <= (long)radius * radius; }
        while (session.View.Status != RunStatus.Completed && tick < maximumTicks && (quitAt < 0 || tick < quitAt))
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                var cards = session.View.CaptureCards();
                Send(ReplayCommandKind.ChooseCard, card: cards.Cards[(seed + tick) % cards.Cards.Count]); continue;
            }
            var frame = session.View.CaptureFrame(); var wave = ((IWaveRunView)session.View).CaptureWaveRuntime()!; var estate = wave.Estate!;
            var lord = frame.Lord.Position;
            WaveEstateBuildingView? next = null; var best = long.MaxValue;
            foreach (var b in estate.Buildings)
            {
                if (b.Phase == WaveEstatePhase.Active) { continue; }
                long dx = (long)b.Position.X - lord.X, dy = (long)b.Position.Y - lord.Y;
                if (dx * dx + dy * dy < best) { best = dx * dx + dy * dy; next = b; }
            }
            PlayerInput input;
            if (next is not null) { input = Toward(lord, next.Position, next.WakeRadius / 3); }
            else
            {
                if (allAwake < 0) { allAwake = tick; stopSince = tick; }
                // Each stop lasts 450 ticks; the well stop ends early once health is full.
                var at = estate.Buildings.First(b => b.DefinitionId == tour[stop % tour.Length]);
                var done = tick - stopSince >= 450 || at.DefinitionId == "core:estate_well_shrine" && tick - stopSince >= 90 && frame.Lord.Health >= frame.Lord.MaxHealth;
                if (done) { stop++; stopSince = tick; at = estate.Buildings.First(b => b.DefinitionId == tour[stop % tour.Length]); }
                // At the granary the hero circles inside its light, so work orbs are left behind and then pulled in.
                var goal = at.Position;
                if (at.DefinitionId == "core:estate_granary")
                {
                    var angle = (tick - stopSince) * Math.PI * 2 / 300;
                    goal = new((int)Math.Round(at.Position.X + 1200 * Math.Cos(angle)), (int)Math.Round(at.Position.Y + 1200 * Math.Sin(angle)));
                }
                input = Toward(lord, goal, 120);
            }
            var before = wave.Rewards.ToDictionary(r => r.Id, r => r.Position);
            Send(ReplayCommandKind.Advance, input); tick++;
            var after = ((IWaveRunView)session.View).CaptureWaveRuntime()!; var lordAfter = session.View.CaptureFrame().Lord.Position;
            foreach (var e in after.Events)
            {
                if (e.Kind == "estate-shot") { shots++; } else if (e.Kind == "estate-heal") { heals++; } else if (e.Kind == "estate-harvest-bonus") { bonuses++; }
            }
            var granaries = after.Estate!.Buildings.Where(b => b.Phase == WaveEstatePhase.Active && b.Effect == WaveEstateEffectKind.Magnet && Inside(b.Position, lordAfter, b.LitRadius)).ToArray();
            foreach (var r in after.Rewards)
            {
                if (!before.TryGetValue(r.Id, out var was) || was.X == r.Position.X && was.Y == r.Position.Y) { continue; }
                if (granaries.Any(g => Inside(g.Position, was, g.LitRadius))) { pulls++; } else { strayPulls++; }
            }
            if (quitAt < 0 && shots > 0 && heals > 0 && pulls > 0) { quitAt = tick + 300; }
            if (tick % 1800 == 0) { ReplayCodec.WriteCheckpoint(output, new(session.NextSequence, tick, session.ComputeStateHash())); }
        }
        var summary = session.GetSummary();
        var end = new ReplayEnd(session.NextSequence, tick, summary.EndReason == "duration" ? ReplayEndKind.Duration : summary.EndReason == "death" ? ReplayEndKind.Death : ReplayEndKind.Quit, summary.StateHash);
        ReplayCodec.WriteEnd(output, end);
        return new(7, loadout, tick, (int)end.Kind, allAwake, shots, heals, pulls, bonuses, strayPulls);
    }

    public static ReplayEnd RecordWaveFixture(ContentCatalog catalog, string dataHash, int seed, Stream output, int maximumTicks = 3000, Action<WaveRuntimeFrame>? observed = null)
    {
        if (maximumTicks is < 1 or > 3000) { throw new ArgumentOutOfRangeException(nameof(maximumTicks)); }
        var targets = catalog.WaveRuntime!.MaterialTargets!.Keys.Order(StringComparer.Ordinal).ToArray();
        var options = new InteractiveOptions(new(seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true, TargetMaterial: targets[seed % targets.Length]), AimMode.Movement, dataHash);
        var session = new InteractiveSession(catalog, options);
        ReplayCodec.WriteHeader(output, ReplayCodec.Header(options));
        var tick = 0;
        void Send(ReplayCommandKind kind, PlayerInput input = default, string? card = null)
        {
            var command = new ReplayCommand(session.NextSequence, tick, kind, input, card);
            session.Apply(command); ReplayCodec.WriteCommand(output, command);
        }
        while (session.View.Status != RunStatus.Completed && tick < maximumTicks)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                var cards = session.View.CaptureCards();
                Send(ReplayCommandKind.ChooseCard, card: cards.Cards[(seed + tick) % cards.Cards.Count]); continue;
            }
            var phase = tick / 180 % 8;
            var input = phase switch
            {
                0 => new PlayerInput(1000, 0),
                1 => new PlayerInput(1000, 1000),
                2 => new PlayerInput(0, 1000),
                3 => new PlayerInput(-1000, 1000),
                4 => new PlayerInput(-1000, 0),
                5 => new PlayerInput(-1000, -1000),
                6 => new PlayerInput(0, -1000),
                _ => new PlayerInput(1000, -1000)
            };
            Send(ReplayCommandKind.Advance, input); tick++;
            if (tick % 1800 == 0) { ReplayCodec.WriteCheckpoint(output, new(session.NextSequence, tick, session.ComputeStateHash())); }
        }
        observed?.Invoke(((IWaveRunView)session.View).CaptureWaveRuntime()!);
        var summary = session.GetSummary();
        var end = new ReplayEnd(session.NextSequence, tick, summary.EndReason == "duration" ? ReplayEndKind.Duration : summary.EndReason == "death" ? ReplayEndKind.Death : ReplayEndKind.Quit, summary.StateHash);
        ReplayCodec.WriteEnd(output, end); return end;
    }

}
