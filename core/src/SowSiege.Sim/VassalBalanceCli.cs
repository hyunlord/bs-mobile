using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

// Balance observations only: this command never measures host/frame performance or changes input fixtures.
public static class VassalBalanceCli
{
    public sealed record BalanceCase(string Id, VassalRunOptions? Options);
    public const string Policy = "octagon-180ticks-ordinal-card-v1";
    private static readonly JsonSerializerOptions Json = HostJson.CreateOptions(camelCase: true, indented: true);

    public static BalanceCase[] Cases(MetaCatalog meta, VassalRunRules rules)
    {
        var state = MetaEngine.NewGame(meta);
        foreach (var building in meta.ManorBuildings)
        {
            state.ManorLevels[building.Id] = building.MaxLevel;
        }
        state = state with
        {
            CompletedChallenges = meta.Challenges.Select(c => c.Id).ToArray(),
            UnlockedContentIds = meta.InitialContentIds.Concat(meta.Challenges.SelectMany(c => c.UnlockContentIds)).Distinct(StringComparer.Ordinal).ToArray()
        };
        foreach (var v in meta.Vassals)
        {
            state.Vassals[v.Id] = new(v.MaxLevel, v.MaxRank, 0,
                v.InitiallyUnlocked || meta.Challenges.Any(c => c.UnlockVassalIds.Contains(v.Id)));
        }
        BalanceCase Maximum(string id, params MetaAbility[] abilities)
        {
            var selected = state with { ActiveVassalIds = abilities.Select(a => meta.Vassals.Single(v => v.Ability == a).Id).ToArray() };
            return new(id, VassalRun.Create(meta, selected, rules));
        }
        var middle = state with
        {
            ActiveVassalIds = new[] { MetaAbility.Attack, MetaAbility.Health, MetaAbility.Movement }
                .Select(a => meta.Vassals.Single(v => v.Ability == a).Id).ToArray(),
            Vassals = state.Vassals.ToDictionary(pair => pair.Key,
                pair => pair.Value with { Level = 10, Rank = 2 }, StringComparer.Ordinal)
        };
        return new[]
        {
            new BalanceCase("none", null),
            Maximum("attack-health-movement", MetaAbility.Attack, MetaAbility.Health, MetaAbility.Movement),
            Maximum("growth-experience-attack", MetaAbility.Growth, MetaAbility.Experience, MetaAbility.Attack),
            Maximum("allies-health-attack", MetaAbility.Allies, MetaAbility.Health, MetaAbility.Attack),
            new BalanceCase("middle-attack-health-movement", VassalRun.Create(meta, middle, rules))
        };
    }

    public static PlayerInput Movement(int tick) => (tick / 180 % 8) switch
    {
        0 => new(1000, 0),
        1 => new(1000, 1000),
        2 => new(0, 1000),
        3 => new(-1000, 1000),
        4 => new(-1000, 0),
        5 => new(-1000, -1000),
        6 => new(0, -1000),
        _ => new(1000, -1000)
    };

    public static string ChooseCard(IEnumerable<string> offered) => offered.Order(StringComparer.Ordinal).First();

    public static bool C05Only(string[] args)
    {
        if (args.Length is 3 or 5 && args.Skip(1).All(a => !a.StartsWith("--", StringComparison.Ordinal)))
        {
            return false;
        }
        if (args.Length == 6 && args[5] == "--c05-only" && args.Skip(1).Take(4).All(a => !a.StartsWith("--", StringComparison.Ordinal)))
        {
            return true;
        }
        throw new ArgumentException("vassal-balance <data-root> <output-directory> [C05-replay C05-frozen-data-root [--c05-only]]");
    }

    public static int Run(string[] args)
    {
        var c05Only = C05Only(args);
        var coreAssembly = CoreAssemblyMetadata.VerifyHostBinding();
        var data = args[1]; var output = args[2];
        var catalog = ContentLoader.Load(data, profileName: "wave-1a");
        var hash = ContentLoader.Hash(data, false);
        if (catalog.WaveRuntime is null)
        {
            throw new InvalidOperationException("Normal balance requires wave runtime.");
        }
        var cases = Cases(MetaContentLoader.Read(data), VassalContentLoader.Load(data));
        ContentCatalog? frozen = null;
        ReplayDocument? original = null;
        if (args.Length >= 5)
        {
            frozen = ContentLoader.Load(args[4], profileName: "wave-1a");
            using var input = File.OpenRead(args[3]);
            original = ReplayCodec.Read(input);
            ReplayRunner.Verify(frozen, ContentLoader.Hash(args[4], false), original);
        }
        var normalSeeds = c05Only ? Array.Empty<int>() : Enumerable.Range(30000, 32).ToArray();
        Directory.CreateDirectory(output);
        using var csv = new StreamWriter(Path.Combine(output, "balance.csv"));
        csv.WriteLine("scene,case,seed,status,rejectedSequence,error,tick,tickRate,combatSeconds,endReason,survived,level,health,maxHealth,killXp,harvestXp,taxXp,weaponDamage,toolActivationDamage,toolGrowthDamage,allyDamage,workCompleted,groups,kills,stateHash,commandSha256,counters");
        var provenance = new
        {
            contract = "vassal-balance-v1",
            profile = "wave-1a",
            dataHash = hash,
            coreAssembly,
            catalogHash = ReviewShowcaseCatalog.Hash(catalog),
            durationTicks = catalog.Tuning.DurationTicks,
            seeds = normalSeeds,
            normalWave = c05Only ? "NOT_RUN (--c05-only)" : "REQUESTED",
            policy = Policy,
            admission = "Normal runs use only Advance and ChooseCard; full original duration or natural death. Same tick-indexed movement and ordinal offered-card policy, not identical card-command streams when XP differs. No invulnerability, grants, speedup or rewards. Max accounts are catalog-valid synthetic completed accounts, not claimed earned playthroughs.",
            middleState = "Lv10 rank2 attack/health/movement vassals on the same synthetic completed account and unlock capacity as maximum cases; not a midpoint account-economy progression claim. Same 32 seeds, duration and input policy; no per-run grants.",
            fp01 = "HISTORICAL_REGRESSION_ONLY; no vassal effect comparison",
            performance = "NOT_RUN; combatSeconds is simulation ticks / tickRate, not wall time",
            c05 = original is not null ? new { replaySha256 = FileHash(args[3]), dataHash = ContentLoader.Hash(args[4], false), policy = "Original commands unchanged; original verified first; derivative failure preserves first rejected command and observed failure state. Stress prefix, not normal survival evidence." } : null,
            cases = cases.Select(c => new { c.Id, snapshot = c.Options?.Snapshot }).ToArray()
        };
        File.WriteAllText(Path.Combine(output, "provenance.json"), JsonSerializer.Serialize(provenance, Json));
        var failures = 0;
        foreach (var seed in normalSeeds)
        {
            foreach (var entry in cases)
            {
                var targets = catalog.WaveRuntime.MaterialTargets!.Keys.Order(StringComparer.Ordinal).ToArray();
                var options = new InteractiveOptions(new(seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true,
                    TargetMaterial: targets[seed % targets.Length]), AimMode.Movement, hash, Vassals: entry.Options);
                if (!Observe(csv, "normal-wave", entry.Id, catalog, options, null))
                {
                    failures++;
                }
            }
        }
        if (original is not null && frozen is not null)
        {
            foreach (var entry in cases)
            {
                if (!Observe(csv, "C05-fixed-command-derivative", entry.Id, frozen, original.Header.Options with { Vassals = entry.Options }, original))
                {
                    failures++;
                }
            }
        }
        Console.WriteLine($"Balance rows finished; rejected runs={failures}; performance NOT_RUN; FP01 historical only.");
        return failures == 0 ? 0 : 1;
    }

    private static bool Observe(TextWriter csv, string scene, string id, ContentCatalog catalog, InteractiveOptions options, ReplayDocument? replay)
    {
        var session = new InteractiveSession(catalog, options);
        var status = "COMPLETED"; var error = ""; long? rejected = null;
        using var algorithm = SHA256.Create();
        using var digest = new CryptoStream(Stream.Null, algorithm, CryptoStreamMode.Write);
        ReplayCodec.WriteHeader(digest, ReplayCodec.Header(options));
        void Apply(ReplayCommand command)
        {
            rejected = command.Sequence;
            session.Apply(command);
            ReplayCodec.WriteCommand(digest, command);
            rejected = null;
        }
        try
        {
            if (replay is not null)
            {
                foreach (var command in replay.Commands)
                {
                    Apply(command);
                }
                if (session.View.CaptureFrame().Tick != replay.End.Tick)
                {
                    throw new InvalidOperationException("Derivative did not reach original end tick.");
                }
            }
            else
            {
                var tick = 0;
                while (session.View.Status != RunStatus.Completed)
                {
                    if (session.View.Status == RunStatus.AwaitingCard)
                    {
                        Apply(new(session.NextSequence, tick, ReplayCommandKind.ChooseCard, CardId: ChooseCard(session.View.CaptureCards().Cards)));
                    }
                    else
                    {
                        if (tick >= catalog.Tuning.DurationTicks)
                        {
                            throw new InvalidOperationException("Original duration reached without completion.");
                        }
                        Apply(new(session.NextSequence, tick, ReplayCommandKind.Advance, Movement(tick)));
                        tick++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            status = "REJECTED"; error = ex.GetType().Name + ": " + ex.Message;
        }
        digest.FlushFinalBlock();
        var s = session.GetSummary(); var frame = session.View.CaptureFrame();
        var wave = ((IWaveRunView)session.View).CaptureWaveRuntime()!;
        object?[] row =
        {
            scene, id, s.Seed, status, rejected, error, s.Tick, frame.TickRate, (decimal)s.Tick / frame.TickRate,
            s.EndReason, s.Survived, s.Level, frame.Lord.Health, frame.Lord.MaxHealth, s.KillExperience, s.HarvestExperience, s.TaxExperience,
            s.WeaponDamage, s.ToolActivationDamage, s.ToolGrowthDamage, s.AllyDamage, wave.Work.Count(w => w.Complete), wave.Groups.Count,
            wave.Counters.Where(c => c.Key.StartsWith("enemy-killed:", StringComparison.Ordinal)).Sum(c => c.Value), s.StateHash, Convert.ToHexString(algorithm.Hash!),
            JsonSerializer.Serialize(wave.Counters.OrderBy(c => c.Key, StringComparer.Ordinal).ToDictionary(c => c.Key, c => c.Value))
        };
        csv.WriteLine(string.Join(",", row.Select(Csv))); csv.Flush();
        return status == "COMPLETED";
    }

    private static string Csv(object? value) => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";
    private static string FileHash(string path) { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)); }
}
