using System;
using System.Collections.Generic;
using System.IO;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class RewardedAdsTests
    {
        int day;
        MemoryStore store;
        RewardedAdsPolicy policy;
        RewardedAdContext Context(RewardedAdSlot slot = RewardedAdSlot.Reroll, string run = "run-1", bool removal = false)
            => new RewardedAdContext(slot, run, "claim-1", 1, removal, true, 0, true, true, true, true);
        [SetUp] public void Setup() { day = 100; store = new MemoryStore(); policy = new RewardedAdsPolicy(store, () => day); day++; }

        [Test] public void FirstAccountDayAndFirstRunHideEvenWithRemoval()
        {
            day = 100;
            Assert.That(policy.Evaluate(Context(removal: true), false).Visible, Is.False);
            day++;
            var firstRun = new RewardedAdContext(RewardedAdSlot.Reroll, "first", "", 0, true, true, 0, true, true, true, true);
            Assert.That(policy.Evaluate(firstRun, true).HiddenReason, Is.EqualTo(RewardedAdHiddenReason.FirstRun));
            Assert.That(policy.Evaluate(Context(), true).Visible, Is.True);
        }

        [Test] public void UnknownConfigurationAndNonzeroRerollsHide()
        {
            var result = new RewardedAdContext(RewardedAdSlot.Result, "r", "", 1, false, false, 0, true, true, false, false);
            Assert.That(policy.Evaluate(result, true).HiddenReason, Is.EqualTo(RewardedAdHiddenReason.UndecidedConfiguration));
            var revive = new RewardedAdContext(RewardedAdSlot.Revive, "r", "", 1, false, false, 0, true, true, false, false);
            Assert.That(policy.Evaluate(revive, true).Visible, Is.False);
            var reroll = new RewardedAdContext(RewardedAdSlot.Reroll, "r", "", 1, false, true, 1, true, true, true, true);
            Assert.That(policy.Evaluate(reroll, true).Visible, Is.False);
        }

        [TestCase(RewardedAdSlot.Result, 1)]
        [TestCase(RewardedAdSlot.Revive, 1)]
        [TestCase(RewardedAdSlot.Reroll, 2)]
        public void PerRunQuotaPersistsAndCannotBeResetByScreenVisits(RewardedAdSlot slot, int maximum)
        {
            var provider = new CountingProvider(RewardedAdOutcome.Completed);
            for (int index = 1; index <= maximum; index++)
            {
                Assert.That(policy.Request(Context(slot), "done-" + index, provider), Is.EqualTo(RewardedAdRequestResult.PendingReward));
                Assert.That(policy.PendingRewards[0].Ordinal, Is.EqualTo(index));
                policy.Acknowledge("done-" + index);
                policy = new RewardedAdsPolicy(store, () => day);
                Assert.That(policy.Evaluate(Context(slot), true).Visible, Is.False);
                policy.NotifyMeaningfulGameplay("run-1", index);
            }
            Assert.That(policy.Evaluate(Context(slot), true).HiddenReason, Is.EqualTo(RewardedAdHiddenReason.LimitReached));
            Assert.That(policy.Evaluate(Context(slot, "run-2"), true).Visible, Is.True);
        }

        [Test] public void IdleThreeDailyLimitPersistsAcrossRunAndDayRollback()
        {
            var provider = new CountingProvider(RewardedAdOutcome.Completed);
            for (int index = 1; index <= 3; index++)
            {
                var context = new RewardedAdContext(RewardedAdSlot.Idle, "run-" + index, "idle-" + index, 1, false, false, 0, true, true, true, true);
                policy.Request(context, "idle-done-" + index, provider); policy.Acknowledge("idle-done-" + index);
                policy.NotifyMeaningfulGameplay("run-1", index);
            }
            Assert.That(policy.Evaluate(Context(RewardedAdSlot.Idle), true).HiddenReason, Is.EqualTo(RewardedAdHiddenReason.LimitReached));
            day++; Assert.That(policy.Evaluate(Context(RewardedAdSlot.Idle), true).Visible, Is.True);
            policy.Request(Context(RewardedAdSlot.Idle), "tomorrow", provider); policy.Acknowledge("tomorrow");
            policy.NotifyMeaningfulGameplay("run-1", 4); day--;
            Assert.That(policy.Evaluate(Context(RewardedAdSlot.Idle), true).Visible, Is.False);
        }

        [Test] public void FailureHidesWithoutQuotaAndMeaningfulGameplayDoesNotAcceptDuplicateEvents()
        {
            policy.NotifyMeaningfulGameplay("run-1", 10);
            var provider = new CountingProvider(RewardedAdOutcome.Failed);
            Assert.That(policy.Request(Context(), "failed", provider), Is.EqualTo(RewardedAdRequestResult.Failed));
            Assert.That(policy.PendingRewards, Is.Empty);
            policy = new RewardedAdsPolicy(store, () => day);
            policy.NotifyMeaningfulGameplay("run-1", 10);
            Assert.That(policy.Evaluate(Context(), true).Visible, Is.False);
            policy.NotifyMeaningfulGameplay("run-1", 11);
            provider.Outcome = RewardedAdOutcome.Completed;
            policy.Request(Context(), "success", provider);
            Assert.That(policy.PendingRewards[0].Ordinal, Is.EqualTo(1));
        }

        [Test] public void ProviderExceptionIsHiddenAndConsumesNoRewardQuota()
        {
            var provider = new CountingProvider(RewardedAdOutcome.Completed) { Throw = true };
            Assert.That(policy.Request(Context(), "exception", provider), Is.EqualTo(RewardedAdRequestResult.Failed));
            Assert.That(policy.PendingRewards, Is.Empty);
            Assert.That(policy.Evaluate(Context(), true).Visible, Is.False);
            policy.NotifyMeaningfulGameplay("run-1", 1); provider.Throw = false;
            policy.Request(Context(), "retry", provider);
            Assert.That(policy.PendingRewards[0].Ordinal, Is.EqualTo(1));
        }

        [Test] public void RemovalBypassesUnavailableProviderButNotQuotasAndIdsRemainIdempotent()
        {
            var provider = new CountingProvider(RewardedAdOutcome.Failed) { IsAvailable = false };
            Assert.That(policy.Evaluate(Context(), false).Visible, Is.False);
            Assert.That(policy.Evaluate(Context(removal: true), false).DirectReward, Is.True);
            policy.Request(Context(removal: true), "direct", provider);
            Assert.That(provider.Calls, Is.Zero);
            policy.Request(Context(removal: true), "direct", provider);
            Assert.That(policy.PendingRewards.Count, Is.EqualTo(1));
            policy.Acknowledge("direct"); policy = new RewardedAdsPolicy(store, () => day);
            Assert.That(policy.Request(Context(removal: true), "direct", provider), Is.EqualTo(RewardedAdRequestResult.AlreadyDelivered));
            Assert.Throws<InvalidOperationException>(() => policy.Request(Context(RewardedAdSlot.Revive, removal: true), "direct", provider));
        }

        [Test] public void CompletionWriteFailureRetainsOutboxForFlushAndDoesNotRepeatProvider()
        {
            var provider = new CountingProvider(RewardedAdOutcome.Completed);
            store.FailNext = true;
            Assert.That(policy.Request(Context(), "receipt", provider), Is.EqualTo(RewardedAdRequestResult.PersistencePending));
            Assert.That(policy.PendingRewards, Is.Empty);
            Assert.That(policy.Evaluate(Context(), true).Visible, Is.False);
            policy.FlushPendingWrite();
            Assert.That(provider.Calls, Is.EqualTo(1));
            policy = new RewardedAdsPolicy(store, () => day);
            Assert.That(policy.PendingRewards[0].CompletionId, Is.EqualTo("receipt"));
            Assert.That(policy.Request(Context(), "receipt", provider), Is.EqualTo(RewardedAdRequestResult.PendingReward));
            Assert.That(provider.Calls, Is.EqualTo(1));
        }

        [Test] public void ReceiverSuccessThenAcknowledgementFailureRetriesSameIdNotAnotherReward()
        {
            policy.Request(Context(removal: true), "delivery", new RewardedAdsUnavailableProvider());
            var received = new HashSet<string>();
            received.Add(policy.PendingRewards[0].CompletionId);
            store.FailNext = true;
            Assert.That(policy.Acknowledge("delivery"), Is.False);
            policy.FlushPendingWrite(); policy = new RewardedAdsPolicy(store, () => day);
            Assert.That(policy.PendingRewards, Is.Empty);
            Assert.That(received.Count, Is.EqualTo(1));
            Assert.That(policy.Request(Context(removal: true), "delivery", new RewardedAdsUnavailableProvider()), Is.EqualTo(RewardedAdRequestResult.AlreadyDelivered));
        }

        [Test] public void ClosedRunExpiryPreservesConsumedQuotaAndCannotAcknowledgeWrongRun()
        {
            policy.Request(Context(removal: true), "old", new RewardedAdsUnavailableProvider());
            Assert.That(policy.ExpireClosedRunReward("old", "different"), Is.False);
            Assert.That(policy.ExpireClosedRunReward("old", "run-1"), Is.True);
            Assert.That(policy.PendingRewards, Is.Empty);
            Assert.That(policy.Acknowledge("old"), Is.False);
            policy = new RewardedAdsPolicy(store, () => day);
            Assert.That(policy.Request(Context(removal: true), "old", new RewardedAdsUnavailableProvider()), Is.EqualTo(RewardedAdRequestResult.Expired));
            policy.NotifyMeaningfulGameplay("run-1", 1);
            policy.Request(Context(removal: true), "next", new RewardedAdsUnavailableProvider());
            Assert.That(policy.PendingRewards[0].Ordinal, Is.EqualTo(2));
        }

        [Test] public void IdleDisplayedDailyCountIncludesAcknowledgedRewardsOnlyOnTheirDay()
        {
            Assert.That(policy.TodayIdleCompletions, Is.Zero);
            policy.Request(Context(RewardedAdSlot.Idle, removal: true), "idle", new RewardedAdsUnavailableProvider());
            Assert.That(policy.TodayIdleCompletions, Is.EqualTo(1));
            policy.Acknowledge("idle"); policy = new RewardedAdsPolicy(store, () => day);
            Assert.That(policy.TodayIdleCompletions, Is.EqualTo(1)); day++;
            Assert.That(policy.TodayIdleCompletions, Is.Zero);
        }

        [Test] public void AtomicLedgerReloadPreservesPendingIntentAndRejectsCorruption()
        {
            var path = Path.Combine(Path.GetTempPath(), "reward-policy-" + Guid.NewGuid().ToString("N"));
            try
            {
                var disk = new RewardedAdsAtomicStore(path); day = 200;
                var actual = new RewardedAdsPolicy(disk, () => day); day++;
                actual.Request(Context(removal: true), "saved", new RewardedAdsUnavailableProvider());
                actual = new RewardedAdsPolicy(disk, () => day);
                Assert.That(actual.PendingRewards[0].RunId, Is.EqualTo("run-1"));
                Assert.That(actual.PendingRewards[0].Slot, Is.EqualTo(RewardedAdSlot.Reroll));
                Assert.That(actual.PendingRewards[0].Ordinal, Is.EqualTo(1));
                actual.Acknowledge("saved");
                actual = new RewardedAdsPolicy(disk, () => day); Assert.That(actual.PendingRewards, Is.Empty);
                File.WriteAllText(Path.Combine(path, "rewarded-ads.bin"), "broken");
                File.WriteAllText(Path.Combine(path, "rewarded-ads.bin.bak"), "broken");
                Assert.Throws<InvalidDataException>(() => new RewardedAdsPolicy(disk, () => day));
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        sealed class MemoryStore : IRewardedAdsStore
        {
            byte[] bytes;
            public bool FailNext;
            public byte[] Load() => bytes == null ? null : (byte[])bytes.Clone();
            public void Save(byte[] value)
            {
                if (FailNext) { FailNext = false; throw new IOException("Injected write failure."); }
                bytes = (byte[])value.Clone();
            }
        }
        sealed class CountingProvider : IRewardedAdProvider
        {
            public bool IsAvailable { get; set; } = true;
            public RewardedAdOutcome Outcome;
            public int Calls;
            public bool Throw;
            public CountingProvider(RewardedAdOutcome outcome) { Outcome = outcome; }
            public RewardedAdOutcome Show(RewardedAdSlot slot, string completionId) { Calls++; if (Throw) throw new InvalidOperationException("Injected provider failure."); return Outcome; }
        }
    }
}
