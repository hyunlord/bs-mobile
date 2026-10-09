using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public static class MetaEngine
    {
        public static MetaState NewGame(MetaCatalog catalog)
        {
            return new MetaState(MetaSaveCodec.CurrentVersion, 0, catalog.ManorBuildings.First().Id,
                catalog.Materials.ToDictionary(x => x.Id, _ => 0), catalog.ManorBuildings.ToDictionary(x => x.Id, _ => 0),
                catalog.Vassals.ToDictionary(x => x.Id, x => new MetaVassalState(1, 0, 0, x.InitiallyUnlocked)),
                new Dictionary<string, long>(), Array.Empty<string>(), catalog.InitialContentIds.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                catalog.Vassals.Where(x => x.InitiallyUnlocked).Take(catalog.Economy.BaseVassalSlots).Select(x => x.Id).ToArray(),
                1, null, -1, -1, 0, 0);
        }

        public static MetaRunPlan BeginRun(MetaCatalog catalog, MetaState state, string chapterId, int seed)
        {
            var chapter = catalog.Chapters.Single(x => x.Id == chapterId);
            if (state.PendingRun != null)
            {
                throw new InvalidOperationException("A run is already pending.");
            }

            if (chapter.Index > state.HighestClearedChapter + 1)
            {
                throw new InvalidOperationException("Chapter locked.");
            }

            if (state.NextRunSequence == long.MaxValue)
            {
                throw new InvalidOperationException("Run sequence exhausted.");
            }

            var pending = new MetaPendingRun(state.NextRunSequence, chapterId, seed);
            return new MetaRunPlan(Clone(state) with { PendingRun = pending, NextRunSequence = state.NextRunSequence + 1 }, pending);
        }

        public static MetaSettlement SettleRun(MetaCatalog catalog, MetaState state, MetaPendingRun pending, MetaRunFacts facts)
        {
            if (state.PendingRun == null || state.PendingRun != pending)
            {
                throw new InvalidOperationException("Run is not pending.");
            }

            if (facts.Tick < 0 || facts.DurationTicks <= 0 || facts.Level < 0 || facts.Farms < 0 || facts.Buildings < 0 || facts.People < 0 || facts.Kills < 0 || facts.Harvests < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(facts));
            }

            var chapter = catalog.Chapters.Single(x => x.Id == pending.ChapterId);
            bool cleared = !facts.Abandoned && facts.Survived && facts.BossDefeated && facts.Tick >= facts.DurationTicks;
            int multiplier = facts.Abandoned ? catalog.Economy.AbandonPermille : !cleared ? catalog.Economy.DeathPermille : chapter.Index <= state.HighestClearedChapter ? catalog.Economy.RepeatPermille : PlayerInput.Scale;
            var next = Clone(state) with { PendingRun = null, CompletedRuns = Saturate(state.CompletedRuns, 1), HighestClearedChapter = cleared ? Math.Max(state.HighestClearedChapter, chapter.Index) : state.HighestClearedChapter };
            var requested = new Dictionary<string, int>();
            foreach (var rule in catalog.Economy.RewardRules)
            {
                decimal raw = (decimal)facts.Farms * rule.PerFarm + (decimal)facts.Buildings * rule.PerBuilding + (decimal)facts.People * rule.PerPerson + (decimal)facts.Harvests * rule.PerHarvest + (decimal)facts.Level * rule.PerLevel + (facts.BossDefeated ? rule.PerBoss : 0);
                int cap = chapter.RewardCaps[rule.MaterialId];
                requested[rule.MaterialId] = facts.Tick < catalog.Economy.MinimumRewardTicks ? 0 : (int)(Math.Min(cap, raw) * multiplier / PlayerInput.Scale);
            }
            var awarded = Credit(catalog, next, requested, out var overflow);
            if (facts.Tick >= catalog.Economy.MinimumRewardTicks)
            {
                AddMetric(next, MetaMetric.Runs, 1); AddMetric(next, MetaMetric.Clears, cleared ? 1 : 0);
                next.Metrics[MetaMetric.HighestChapter.ToString()] = next.HighestClearedChapter;
                AddMetric(next, MetaMetric.Kills, facts.Kills); AddMetric(next, MetaMetric.Harvests, facts.Harvests);
                AddMetric(next, MetaMetric.Buildings, facts.Buildings); AddMetric(next, MetaMetric.People, facts.People);
                AddMetric(next, MetaMetric.Bosses, !facts.Abandoned && facts.BossDefeated ? 1 : 0);
                AddMetric(next, MetaMetric.SurvivalTicks, Math.Min(facts.Tick, facts.DurationTicks));
            }
            foreach (string id in next.ActiveVassalIds)
            {
                var definition = catalog.Vassals.Single(x => x.Id == id); var vassal = next.Vassals[id];
                int fragments = facts.Tick < catalog.Economy.MinimumRewardTicks || facts.Abandoned ? 0 : cleared ? catalog.Economy.FragmentsPerClear : catalog.Economy.FragmentsPerDeath;
                next.Vassals[id] = vassal with { Fragments = (int)Math.Min(definition.FragmentCap, (long)vassal.Fragments + fragments) };
            }
            next = CompleteProgression(catalog, next, out var grown, out var completed);
            return new MetaSettlement(next, cleared, awarded, overflow, grown, completed);
        }

        public static MetaIdleResult AdvanceIdle(MetaCatalog catalog, MetaState state, long observedMonotonicSeconds, long observedWallSeconds)
        {
            if (observedMonotonicSeconds < 0 || observedWallSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(observedMonotonicSeconds));
            }

            var next = Clone(state) with { LastMonotonicSeconds = observedMonotonicSeconds, LastWallSeconds = observedWallSeconds };
            long mono = state.LastMonotonicSeconds < 0 ? 0 : observedMonotonicSeconds - state.LastMonotonicSeconds;
            long wall = state.LastWallSeconds < 0 ? 0 : observedWallSeconds - state.LastWallSeconds;
            bool initial = state.LastMonotonicSeconds < 0 || state.LastWallSeconds < 0;
            bool valid = !initial && mono >= 0 && wall >= 0 && Math.Abs((decimal)mono - wall) <= catalog.Economy.ClockToleranceSeconds;
            int cap = (int)Math.Min(catalog.Economy.MaximumIdleCapSeconds, (long)catalog.Economy.BaseIdleCapSeconds + (long)EffectLevel(catalog, state, ManorEffect.IdleCapacity) * catalog.Economy.IdleCapSecondsPerLevel);
            int seconds = valid ? (int)Math.Min(cap, mono) : 0;
            long available = valid ? Math.Min(cap, seconds + state.IdleRemainderSeconds) : 0;
            long steps = available / catalog.Economy.IdleStepSeconds;
            next = next with { IdleRemainderSeconds = available % catalog.Economy.IdleStepSeconds };
            var requested = catalog.Economy.IdlePerStep.ToDictionary(x => x.Key, x => (int)Math.Min(int.MaxValue, steps * x.Value));
            var awarded = Credit(catalog, next, requested, out _);
            next = CompleteProgression(catalog, next, out var grown, out _);
            return new MetaIdleResult(next, seconds, initial ? "initialized" : valid ? "accepted" : "clock-mismatch", awarded, grown);
        }

        public static MetaState SetManorPriority(MetaCatalog catalog, MetaState state, string buildingId)
        {
            if (!catalog.ManorBuildings.Any(x => x.Id == buildingId))
            {
                throw new ArgumentException("Unknown manor building.", nameof(buildingId));
            }

            var next = Clone(state) with { ManorPriority = buildingId };
            return CompleteProgression(catalog, next, out _, out _);
        }

        public static MetaState UpgradeVassal(MetaCatalog catalog, MetaState state, string id)
        {
            var definition = catalog.Vassals.Single(x => x.Id == id); var current = state.Vassals[id];
            int cap = (int)Math.Min(definition.MaxLevel, (long)catalog.Economy.BaseVassalLevelCap + (long)EffectLevel(catalog, state, ManorEffect.VassalLevelCap) * catalog.Economy.LevelCapPerForgeLevel);
            if (!current.Unlocked || current.Level >= cap)
            {
                throw new InvalidOperationException("Vassal level locked or capped.");
            }

            var next = Clone(state); Pay(next, Cost(definition.LevelCostBase, definition.LevelCostStep, current.Level - 1));
            next.Vassals[id] = current with { Level = current.Level + 1 };
            next.Metrics[MetaMetric.VassalLevel.ToString()] = next.Vassals.Values.Max(x => (long)x.Level);
            return CompleteProgression(catalog, next, out _, out _);
        }

        public static MetaState RankUpVassal(MetaCatalog catalog, MetaState state, string id)
        {
            var definition = catalog.Vassals.Single(x => x.Id == id); var current = state.Vassals[id];
            if (!current.Unlocked || current.Rank >= definition.MaxRank || current.Fragments < definition.RankCosts[current.Rank])
            {
                throw new InvalidOperationException("Vassal rank locked or unaffordable.");
            }

            var next = Clone(state); next.Vassals[id] = current with { Rank = current.Rank + 1, Fragments = current.Fragments - definition.RankCosts[current.Rank] }; return next;
        }

        public static MetaState ToggleVassal(MetaCatalog catalog, MetaState state, string id)
        {
            if (!state.Vassals.TryGetValue(id, out var vassal) || !vassal.Unlocked)
            {
                throw new InvalidOperationException("Vassal locked.");
            }

            var active = state.ActiveVassalIds.ToList();
            if (!active.Remove(id))
            {
                int cap = Math.Min(catalog.Economy.MaximumVassalSlots, catalog.Economy.BaseVassalSlots + EffectLevel(catalog, state, ManorEffect.VassalSlots) / catalog.Economy.BarracksLevelsPerSlot);
                if (active.Count >= cap)
                {
                    throw new InvalidOperationException("Vassal slots full.");
                }

                active.Add(id);
            }
            return Clone(state) with { ActiveVassalIds = active.OrderBy(x => x, StringComparer.Ordinal).ToArray() };
        }

        public static MetaModifiers Modifiers(MetaCatalog catalog, MetaState state)
        {
            var values = new int[Enum.GetValues(typeof(MetaAbility)).Length];
            foreach (string id in state.ActiveVassalIds)
            {
                var definition = catalog.Vassals.Single(x => x.Id == id); var vassal = state.Vassals[id];
                if (!vassal.Unlocked)
                {
                    throw new InvalidOperationException("Active vassal locked.");
                }

                values[(int)definition.Ability] = checked(values[(int)definition.Ability] + definition.BasePermille + (vassal.Level - 1) * definition.PerLevelPermille + vassal.Rank * definition.PerRankPermille);
            }
            return new MetaModifiers(PlayerInput.Scale + values[(int)MetaAbility.Attack], PlayerInput.Scale + values[(int)MetaAbility.Health], PlayerInput.Scale + values[(int)MetaAbility.Movement], PlayerInput.Scale + values[(int)MetaAbility.Growth], PlayerInput.Scale + values[(int)MetaAbility.Allies], PlayerInput.Scale + values[(int)MetaAbility.Experience]);
        }

        private static MetaState Clone(MetaState state) => state with { Wallet = new Dictionary<string, int>(state.Wallet), ManorLevels = new Dictionary<string, int>(state.ManorLevels), Vassals = new Dictionary<string, MetaVassalState>(state.Vassals), Metrics = new Dictionary<string, long>(state.Metrics), CompletedChallenges = (string[])state.CompletedChallenges.Clone(), UnlockedContentIds = (string[])state.UnlockedContentIds.Clone(), ActiveVassalIds = (string[])state.ActiveVassalIds.Clone() };
        private static long Saturate(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
        private static void AddMetric(MetaState state, MetaMetric metric, long value) { string key = metric.ToString(); state.Metrics.TryGetValue(key, out long previous); state.Metrics[key] = Math.Min(MetaValidation.MaximumMetricValue, Saturate(previous, value)); }
        private static int EffectLevel(MetaCatalog catalog, MetaState state, ManorEffect effect) => catalog.ManorBuildings.Where(x => x.Effect == effect).Sum(x => state.ManorLevels[x.Id]);
        private static Dictionary<string, int> Cost(Dictionary<string, int> basic, Dictionary<string, int> step, int level) => basic.Keys.Union(step.Keys).ToDictionary(x => x, x => checked((basic.TryGetValue(x, out int b) ? b : 0) + (step.TryGetValue(x, out int s) ? s : 0) * level));
        private static bool Affordable(MetaState state, Dictionary<string, int> cost) => cost.All(x => state.Wallet.TryGetValue(x.Key, out int amount) && amount >= x.Value);
        private static void Pay(MetaState state, Dictionary<string, int> cost)
        {
            if (!Affordable(state, cost))
            {
                throw new InvalidOperationException("Insufficient materials.");
            }

            foreach (var item in cost)
            {
                state.Wallet[item.Key] -= item.Value;
            }
        }
        private static Dictionary<string, int> Credit(MetaCatalog catalog, MetaState state, Dictionary<string, int> requested, out Dictionary<string, int> overflow)
        {
            var awarded = new Dictionary<string, int>(); overflow = new Dictionary<string, int>();
            foreach (var material in catalog.Materials)
            {
                int value = requested.TryGetValue(material.Id, out int count) ? count : 0;
                int credit = Math.Min(value, material.WalletCap - state.Wallet[material.Id]);
                state.Wallet[material.Id] += credit; awarded[material.Id] = credit; overflow[material.Id] = value - credit;
            }
            return awarded;
        }
        private static List<string> Grow(MetaCatalog catalog, MetaState state)
        {
            var grown = new List<string>();
            var ordered = catalog.ManorBuildings.OrderBy(x => x.Id == state.ManorPriority ? 0 : 1).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
            bool changed;
            do
            {
                changed = false;
                foreach (var building in ordered)
                {
                    int level = state.ManorLevels[building.Id]; if (level >= building.MaxLevel)
                    {
                        continue;
                    }

                    var cost = Cost(building.BaseCost, building.CostPerLevel, level); if (!Affordable(state, cost))
                    {
                        continue;
                    }

                    Pay(state, cost); state.ManorLevels[building.Id] = level + 1; grown.Add(building.Id); changed = true;
                }
            } while (changed);
            return grown;
        }
        private static MetaState CompleteProgression(MetaCatalog catalog, MetaState state, out string[] buildingsGrown, out string[] challengesCompleted)
        {
            var grown = new List<string>();
            var completed = new List<string>();
            string[] newlyCompleted;
            do
            {
                grown.AddRange(Grow(catalog, state));
                state = CompleteChallenges(catalog, state, out newlyCompleted);
                completed.AddRange(newlyCompleted);
                // Each repeat consumes previously unclaimed challenges; building levels are also bounded.
            } while (newlyCompleted.Length > 0);
            buildingsGrown = grown.ToArray();
            challengesCompleted = completed.ToArray();
            return state;
        }
        private static MetaState CompleteChallenges(MetaCatalog catalog, MetaState state, out string[] newlyCompleted)
        {
            var done = new HashSet<string>(state.CompletedChallenges); var content = new HashSet<string>(state.UnlockedContentIds); var added = new List<string>();
            int research = EffectLevel(catalog, state, ManorEffect.Research);
            foreach (var challenge in catalog.Challenges.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                state.Metrics.TryGetValue(challenge.Metric.ToString(), out long metric);
                if (done.Contains(challenge.Id) || metric < challenge.Target || research < challenge.ResearchLevel)
                {
                    continue;
                }

                done.Add(challenge.Id); added.Add(challenge.Id); content.UnionWith(challenge.UnlockContentIds);
                foreach (string id in challenge.UnlockVassalIds)
                {
                    state.Vassals[id] = state.Vassals[id] with { Unlocked = true };
                }

                Credit(catalog, state, challenge.Rewards, out _);
            }
            newlyCompleted = added.ToArray();
            return state with { CompletedChallenges = done.OrderBy(x => x, StringComparer.Ordinal).ToArray(), UnlockedContentIds = content.OrderBy(x => x, StringComparer.Ordinal).ToArray() };
        }
    }
}
