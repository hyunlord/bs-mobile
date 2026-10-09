using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public static class MetaValidation
    {
        public const long MaximumMetricValue = 1000000000000;

        public static string[] ValidateState(MetaCatalog catalog, MetaState state)
        {
            var errors = new List<string>();
            void Check(bool valid, string message) { if (!valid) { errors.Add(message); } }
            bool Same(IEnumerable<string> actual, IEnumerable<string> expected) => new HashSet<string>(actual).SetEquals(expected);
            Check(state.SchemaVersion == MetaSaveCodec.CurrentVersion, "schema-version");
            Check(state.HighestClearedChapter >= 0 && state.HighestClearedChapter <= catalog.Chapters.Max(x => x.Index), "highest-chapter");
            Check(Same(state.Wallet.Keys, catalog.Materials.Select(x => x.Id)), "wallet-keys");
            foreach (var material in catalog.Materials)
            {
                Check(state.Wallet.TryGetValue(material.Id, out int amount) && amount >= 0 && amount <= material.WalletCap, "wallet:" + material.Id);
            }

            Check(Same(state.ManorLevels.Keys, catalog.ManorBuildings.Select(x => x.Id)), "manor-keys");
            foreach (var building in catalog.ManorBuildings)
            {
                Check(state.ManorLevels.TryGetValue(building.Id, out int level) && level >= 0 && level <= building.MaxLevel, "manor:" + building.Id);
            }

            Check(catalog.ManorBuildings.Any(x => x.Id == state.ManorPriority), "manor-priority");
            int Effect(ManorEffect effect) => catalog.ManorBuildings.Where(x => x.Effect == effect).Sum(x => state.ManorLevels.TryGetValue(x.Id, out int level) ? Math.Max(0, Math.Min(x.MaxLevel, level)) : 0);
            long levelCap = (long)catalog.Economy.BaseVassalLevelCap + (long)Effect(ManorEffect.VassalLevelCap) * catalog.Economy.LevelCapPerForgeLevel;
            int slots = Math.Min(catalog.Economy.MaximumVassalSlots, catalog.Economy.BaseVassalSlots + Effect(ManorEffect.VassalSlots) / catalog.Economy.BarracksLevelsPerSlot);
            Check(Same(state.Vassals.Keys, catalog.Vassals.Select(x => x.Id)), "vassal-keys");
            foreach (var vassal in catalog.Vassals)
            {
                Check(state.Vassals.TryGetValue(vassal.Id, out var value) && value.Level >= 1 && value.Level <= Math.Min(levelCap, vassal.MaxLevel) && value.Rank >= 0 && value.Rank <= vassal.MaxRank && value.Fragments >= 0 && value.Fragments <= vassal.FragmentCap, "vassal:" + vassal.Id);
            }
            Check(state.ActiveVassalIds.Length <= slots && state.ActiveVassalIds.Distinct().Count() == state.ActiveVassalIds.Length && state.ActiveVassalIds.All(x => state.Vassals.TryGetValue(x, out var value) && value.Unlocked), "active-vassals");
            var challengeIds = new HashSet<string>(catalog.Challenges.Select(x => x.Id));
            Check(state.CompletedChallenges.Distinct().Count() == state.CompletedChallenges.Length && state.CompletedChallenges.All(challengeIds.Contains), "challenge-ids");
            var unlocked = new HashSet<string>(catalog.InitialContentIds);
            foreach (var challenge in catalog.Challenges.Where(x => state.CompletedChallenges.Contains(x.Id)))
            {
                unlocked.UnionWith(challenge.UnlockContentIds);
            }

            Check(state.UnlockedContentIds.Distinct().Count() == state.UnlockedContentIds.Length && unlocked.SetEquals(state.UnlockedContentIds), "content-unlocks");
            foreach (var vassal in catalog.Vassals)
            {
                bool allowed = vassal.InitiallyUnlocked || catalog.Challenges.Any(x => state.CompletedChallenges.Contains(x.Id) && x.UnlockVassalIds.Contains(vassal.Id));
                Check(state.Vassals.TryGetValue(vassal.Id, out var value) && value.Unlocked == allowed, "vassal-unlock:" + vassal.Id);
            }
            var metricIds = new HashSet<string>(Enum.GetNames(typeof(MetaMetric)));
            Check(state.Metrics.All(x => metricIds.Contains(x.Key) && x.Value >= 0 && x.Value <= MaximumMetricValue), "metrics");
            Check(state.NextRunSequence >= 1 && state.CompletedRuns >= 0 && state.CompletedRuns < state.NextRunSequence, "run-sequence");
            Check(state.LastMonotonicSeconds >= -1 && state.LastWallSeconds >= -1 && state.IdleRemainderSeconds >= 0 && state.IdleRemainderSeconds < catalog.Economy.IdleStepSeconds, "clock");
            if (state.PendingRun != null)
            {
                Check(state.PendingRun.Sequence >= 1 && state.PendingRun.Sequence == state.NextRunSequence - 1, "pending-sequence");
                Check(catalog.Chapters.Any(x => x.Id == state.PendingRun.ChapterId && x.Index <= state.HighestClearedChapter + 1), "pending-chapter");
            }
            return errors.ToArray();
        }
    }
}
