using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record EconomyArchetype(string Id, int RunsPerDay);
public sealed record EconomyRunObservation(MetaRunFacts Facts, string RunHash);
public sealed record EconomyRow(string Archetype, int Day, int Run, int Seed, string Event, int Chapter,
    bool Cleared, int HighestCleared, int Ticks, bool BossDefeated, int Level, int Farms, int Buildings, int People,
    long Kills, long Harvests, int UnlockedContent, int Challenges, int VassalLevel, int VassalRank,
    int ForgeLevel, int ResearchLevel, int GranaryLevel, int BarracksLevel, string Bottleneck,
    Dictionary<string, int> Wallet, Dictionary<string, int> Credited, Dictionary<string, int> Spent,
    Dictionary<string, int> Overflow, string RunHash, string StateHash);

public static class MetaEconomySimulation
{
    public static readonly EconomyArchetype[] Archetypes = { new("light", 2), new("normal", 6), new("heavy", 12) };

    public static IReadOnlyList<EconomyRow> Run(MetaCatalog meta, ContentCatalog content, int days,
        IEnumerable<EconomyArchetype>? archetypes = null,
        Func<ContentCatalog, int, EconomyRunObservation>? observe = null, Action<EconomyRow>? emitted = null, int seedOffset = 0)
    {
        if (days is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(nameof(days));
        }

        observe ??= Observe;
        var rows = new List<EconomyRow>();
        int archetypeIndex = 0;
        foreach (var type in archetypes ?? Archetypes)
        {
            if (type.RunsPerDay is < 1 or > 24)
            {
                throw new ArgumentOutOfRangeException(nameof(archetypes));
            }

            var state = MetaEngine.NewGame(meta);
            state = MetaEngine.AdvanceIdle(meta, state, 0, 1700000000).State;
            for (int day = 1; day <= days; day++)
            {
                var beforeIdle = state;
                var idle = MetaEngine.AdvanceIdle(meta, state, (day - 1) * 86400L, 1700000000 + (day - 1) * 86400L);
                state = Prepare(meta, idle.State);
                var idleGross = meta.Materials.ToDictionary(m => m.Id, m => meta.Economy.IdlePerStep.GetValueOrDefault(m.Id) * (idle.CreditedSeconds / meta.Economy.IdleStepSeconds));
                Emit(beforeIdle, state, day, 0, 0, "idle", 0, false, null, "", idleGross);
                for (int run = 1; run <= type.RunsPerDay; run++)
                {
                    var before = state;
                    var chapter = meta.Chapters.Single(c => c.Index == Math.Min(meta.Chapters.Length, state.HighestClearedChapter + 1));
                    int seed = 710000 + seedOffset + archetypeIndex * 100000 + day * 100 + run;
                    var plan = MetaEngine.BeginRun(meta, state, chapter.Id, seed);
                    var projected = MetaRunAdapter.ProjectCatalog(content, meta, plan.State, plan);
                    var observation = observe(projected, seed);
                    var result = MetaEngine.SettleRun(meta, plan.State, plan.Run, observation.Facts);
                    state = Prepare(meta, result.State);
                    var gross = result.Awarded.ToDictionary(x => x.Key, x => x.Value + result.Overflow[x.Key]);
                    Emit(before, state, day, run, seed, "run", chapter.Index, result.Cleared, observation.Facts, observation.RunHash, gross);
                }
            }
            archetypeIndex++;

            void Emit(MetaState before, MetaState after, int day, int run, int seed, string kind, int chapter,
                bool cleared, MetaRunFacts? facts, string runHash, Dictionary<string, int> gross)
            {
                var errors = MetaValidation.ValidateState(meta, after);
                if (errors.Length != 0)
                {
                    throw new InvalidDataException("Economy state invalid: " + string.Join(",", errors));
                }

                var spent = Spending(meta, before, after);
                var credited = meta.Materials.ToDictionary(m => m.Id, m => after.Wallet[m.Id] - before.Wallet[m.Id] + spent[m.Id]);
                foreach (var challenge in meta.Challenges.Where(c => after.CompletedChallenges.Contains(c.Id) && !before.CompletedChallenges.Contains(c.Id)))
                {
                    foreach (var reward in challenge.Rewards)
                    {
                        gross[reward.Key] = gross.GetValueOrDefault(reward.Key) + reward.Value;
                    }
                }

                var overflow = meta.Materials.ToDictionary(m => m.Id, m => gross.GetValueOrDefault(m.Id) - credited[m.Id]);
                if (credited.Values.Any(x => x < 0) || overflow.Values.Any(x => x < 0))
                {
                    throw new InvalidDataException("Economy ledger conservation failed.");
                }

                int Building(ManorEffect effect) => meta.ManorBuildings.Where(b => b.Effect == effect).Sum(b => after.ManorLevels[b.Id]);
                var row = new EconomyRow(type.Id, day, run, seed, kind, chapter, cleared, after.HighestClearedChapter,
                    facts?.Tick ?? 0, facts?.BossDefeated ?? false, facts?.Level ?? 0, facts?.Farms ?? 0,
                    facts?.Buildings ?? 0, facts?.People ?? 0, facts?.Kills ?? 0, facts?.Harvests ?? 0,
                    after.UnlockedContentIds.Length, after.CompletedChallenges.Length, after.Vassals.Values.Max(v => v.Level),
                    after.Vassals.Values.Max(v => v.Rank), Building(ManorEffect.VassalLevelCap), Building(ManorEffect.Research),
                    Building(ManorEffect.IdleCapacity), Building(ManorEffect.VassalSlots), Bottleneck(meta, after, facts, cleared),
                    new(after.Wallet), credited, spent, overflow, runHash, Convert.ToHexString(SHA256.HashData(MetaSaveCodec.Encode(after))));
                rows.Add(row); emitted?.Invoke(row);
            }
        }
        return rows;
    }

    public static EconomyRunObservation Observe(ContentCatalog content, int seed)
    {
        var run = SimulationFactory.Create(content, new(seed, content.Tuning.DefaultHero, content.Tuning.DefaultEstate,
            "mixed", "C", Movement: "circuit"));
        while (!run.IsComplete)
        {
            run.Tick();
        }

        var result = run.Result(); var world = run.Snapshot; var coverage = run.FirstPlayableCoverage;
        bool boss = coverage.GetValueOrDefault("enemy:boss:kill") > 0;
        long kills = coverage.Where(x => x.Key.StartsWith("enemy:", StringComparison.Ordinal) && x.Key.EndsWith(":kill", StringComparison.Ordinal)).Sum(x => x.Value);
        return new(new(result.Ticks, content.Tuning.DurationTicks, result.Survived, boss, false, result.Level,
            world.Farms, world.Buildings, world.PopulationMembers, kills, result.Harvests), result.Hash);
    }

    private static MetaState Prepare(MetaCatalog meta, MetaState state)
    {
        int slots = Math.Min(meta.Economy.MaximumVassalSlots, meta.Economy.BaseVassalSlots + meta.ManorBuildings.Where(b => b.Effect == ManorEffect.VassalSlots).Sum(b => state.ManorLevels[b.Id]) / meta.Economy.BarracksLevelsPerSlot);
        foreach (var v in meta.Vassals.Where(v => state.Vassals[v.Id].Unlocked).OrderBy(v => v.Ability == MetaAbility.Attack ? 0 : v.Ability == MetaAbility.Health ? 1 : 2).ThenBy(v => v.Id, StringComparer.Ordinal))
        {
            if (!state.ActiveVassalIds.Contains(v.Id) && state.ActiveVassalIds.Length < slots)
            {
                state = MetaEngine.ToggleVassal(meta, state, v.Id);
            }

            if (!state.ActiveVassalIds.Contains(v.Id))
            {
                continue;
            }

            while (state.Vassals[v.Id].Rank < v.MaxRank && state.Vassals[v.Id].Fragments >= v.RankCosts[state.Vassals[v.Id].Rank])
            {
                state = MetaEngine.RankUpVassal(meta, state, v.Id);
            }

            int cap = Math.Min(v.MaxLevel, meta.Economy.BaseVassalLevelCap + meta.ManorBuildings.Where(b => b.Effect == ManorEffect.VassalLevelCap).Sum(b => state.ManorLevels[b.Id]) * meta.Economy.LevelCapPerForgeLevel);
            while (state.Vassals[v.Id].Level < cap)
            {
                int level = state.Vassals[v.Id].Level;
                if (!v.LevelCostBase.Keys.Union(v.LevelCostStep.Keys).All(k => state.Wallet[k] >= v.LevelCostBase.GetValueOrDefault(k) + v.LevelCostStep.GetValueOrDefault(k) * (level - 1)))
                {
                    break;
                }

                state = MetaEngine.UpgradeVassal(meta, state, v.Id);
            }
        }
        return state;
    }

    private static Dictionary<string, int> Spending(MetaCatalog meta, MetaState before, MetaState after)
    {
        var spent = meta.Materials.ToDictionary(m => m.Id, _ => 0);
        foreach (var b in meta.ManorBuildings)
        {
            for (int level = before.ManorLevels[b.Id]; level < after.ManorLevels[b.Id]; level++)
            {
                foreach (string id in spent.Keys.ToArray())
                {
                    spent[id] += b.BaseCost.GetValueOrDefault(id) + b.CostPerLevel.GetValueOrDefault(id) * level;
                }
            }
        }

        foreach (var v in meta.Vassals)
        {
            for (int level = before.Vassals[v.Id].Level; level < after.Vassals[v.Id].Level; level++)
            {
                foreach (string id in spent.Keys.ToArray())
                {
                    spent[id] += v.LevelCostBase.GetValueOrDefault(id) + v.LevelCostStep.GetValueOrDefault(id) * (level - 1);
                }
            }
        }

        return spent;
    }

    private static string Bottleneck(MetaCatalog meta, MetaState state, MetaRunFacts? facts, bool cleared)
    {
        var reasons = new List<string>();
        if (facts is not null && !cleared)
        {
            reasons.Add(facts.Survived ? "boss-not-defeated" : "survival");
        }

        var b = meta.ManorBuildings.Single(b => b.Id == state.ManorPriority);
        if (state.ManorLevels[b.Id] < b.MaxLevel)
        {
            foreach (var m in meta.Materials)
            {
                if (state.Wallet[m.Id] < b.BaseCost.GetValueOrDefault(m.Id) + b.CostPerLevel.GetValueOrDefault(m.Id) * state.ManorLevels[b.Id])
                {
                    reasons.Add("manor-short:" + m.Id);
                }
            }
        }

        foreach (var v in meta.Vassals.Where(v => state.ActiveVassalIds.Contains(v.Id)))
        {
            if (state.Vassals[v.Id].Level < v.MaxLevel)
            {
                foreach (var m in meta.Materials)
                {
                    if (state.Wallet[m.Id] < v.LevelCostBase.GetValueOrDefault(m.Id) + v.LevelCostStep.GetValueOrDefault(m.Id) * (state.Vassals[v.Id].Level - 1))
                    {
                        reasons.Add("vassal-short:" + m.Id);
                    }
                }
            }
        }

        return string.Join(";", reasons.Distinct());
    }

    public static string Csv(IReadOnlyList<EconomyRow> rows, MetaCatalog meta, string dataHash)
    {
        var text = new StringBuilder("archetype,day,run,seed,event,chapter,cleared,highest_cleared,ticks,boss_defeated,level,farms,buildings,people,kills,harvests,unlocked_content,challenges,vassal_level,vassal_rank,forge,research,granary,barracks,bottleneck");
        foreach (var m in meta.Materials)
        {
            foreach (string field in new[] { "wallet", "credited", "spent", "overflow" })
            {
                text.Append(',').Append(m.Id).Append('_').Append(field);
            }
        }

        text.Append(",run_hash,state_hash,data_hash\n");
        foreach (var r in rows)
        {
            var fields = new List<object> { r.Archetype, r.Day, r.Run, r.Seed, r.Event, r.Chapter, r.Cleared ? 1 : 0, r.HighestCleared, r.Ticks, r.BossDefeated ? 1 : 0, r.Level, r.Farms, r.Buildings, r.People, r.Kills, r.Harvests, r.UnlockedContent, r.Challenges, r.VassalLevel, r.VassalRank, r.ForgeLevel, r.ResearchLevel, r.GranaryLevel, r.BarracksLevel, r.Bottleneck };
            foreach (var m in meta.Materials) { fields.Add(r.Wallet[m.Id]); fields.Add(r.Credited[m.Id]); fields.Add(r.Spent[m.Id]); fields.Add(r.Overflow[m.Id]); }
            fields.Add(r.RunHash); fields.Add(r.StateHash); fields.Add(dataHash);
            text.AppendLine(string.Join(",", fields.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture))));
        }
        return text.ToString();
    }
}
