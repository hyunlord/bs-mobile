#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.App
{
    public sealed class RewardedAdsPolicy
    {
        public const int IdleDailyLimitHypothesis = 3;
        readonly IRewardedAdsStore store;
        readonly Func<int> localDay;
        RewardedAdsLedger ledger;
        RewardedAdsLedger? pendingWrite;
        bool requesting;
        public RewardedAdsPolicy(IRewardedAdsStore store, Func<int> localDay)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.localDay = localDay ?? throw new ArgumentNullException(nameof(localDay));
            int day = Day(); var bytes = store.Load();
            ledger = bytes == null ? new RewardedAdsLedger { firstDay = day, maximumDay = day } : RewardedAdsLedger.Decode(bytes);
            if (bytes == null) store.Save(ledger.Encode());
        }
        public IReadOnlyList<RewardedAdIntent> PendingRewards => ledger.receipts
            .Where(receipt => receipt.outcome == (int)RewardedAdOutcome.Completed && !receipt.acknowledged && !receipt.expired)
            .Select(receipt => new RewardedAdIntent(receipt)).ToArray();
        public int TodayIdleCompletions => ledger.receipts.Count(receipt => receipt.slot == (int)RewardedAdSlot.Idle &&
            receipt.outcome == (int)RewardedAdOutcome.Completed && receipt.day == Day());
        public bool HasPendingWrite => pendingWrite != null;

        public RewardedAdOffer Evaluate(RewardedAdContext context, bool providerAvailable)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            RewardedAdOffer Hidden(RewardedAdHiddenReason reason) => new RewardedAdOffer(reason);
            if (pendingWrite != null || requesting) return Hidden(RewardedAdHiddenReason.PersistencePending);
            int day = Day();
            if (day < ledger.maximumDay) return Hidden(RewardedAdHiddenReason.ClockRollback);
            if (context.CompletedRuns == 0) return Hidden(RewardedAdHiddenReason.FirstRun);
            if (day <= ledger.firstDay) return Hidden(RewardedAdHiddenReason.FirstDay);
            if (context.Slot != RewardedAdSlot.Idle && !RewardedAdsLedger.Identity(context.RunId) ||
                context.Slot == RewardedAdSlot.Idle && !RewardedAdsLedger.Identity(context.ClaimId)) return Hidden(RewardedAdHiddenReason.WrongContext);
            if (context.Slot == RewardedAdSlot.Result && !context.ResultMultiplierConfigured ||
                context.Slot == RewardedAdSlot.Revive && !context.ReviveRulesConfigured) return Hidden(RewardedAdHiddenReason.UndecidedConfiguration);
            if ((context.Slot == RewardedAdSlot.Result || context.Slot == RewardedAdSlot.Idle) && !context.RewardClaimAvailable ||
                context.Slot == RewardedAdSlot.Revive && !context.RevivePending ||
                context.Slot == RewardedAdSlot.Reroll && (!context.CardsPending || context.RerollsRemaining != 0)) return Hidden(RewardedAdHiddenReason.WrongContext);
            int limit = context.Slot == RewardedAdSlot.Idle ? IdleDailyLimitHypothesis : context.Slot == RewardedAdSlot.Reroll ? 2 : 1;
            if (Count(context, day) >= limit) return Hidden(RewardedAdHiddenReason.LimitReached);
            if (context.Slot == RewardedAdSlot.Idle && ledger.receipts.Any(r => r.slot == (int)context.Slot && r.claim == context.ClaimId && r.outcome == (int)RewardedAdOutcome.Completed))
                return Hidden(RewardedAdHiddenReason.LimitReached);
            if (ledger.receipts.Any(r => r.outcome == (int)RewardedAdOutcome.Completed && !r.acknowledged && !r.expired)) return Hidden(RewardedAdHiddenReason.PendingDelivery);
            if (ledger.cooling) return Hidden(RewardedAdHiddenReason.Cooldown);
            if (!context.AdRemovalOwned && !providerAvailable) return Hidden(RewardedAdHiddenReason.Unavailable);
            return new RewardedAdOffer(RewardedAdHiddenReason.None, context.AdRemovalOwned);
        }

        public RewardedAdRequestResult Request(RewardedAdContext context, string completionId, IRewardedAdProvider provider)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (!RewardedAdsLedger.Identity(completionId)) throw new ArgumentException("A stable completion ID is required.", nameof(completionId));
            if (pendingWrite != null) return RewardedAdRequestResult.PersistencePending;
            var prior = ledger.receipts.FirstOrDefault(receipt => receipt.id == completionId);
            if (prior != null)
            {
                if (prior.slot != (int)context.Slot || prior.run != context.RunId || prior.claim != context.ClaimId)
                    throw new InvalidOperationException("Completion ID was reused for a different reward.");
                return prior.outcome != (int)RewardedAdOutcome.Completed ? RewardedAdRequestResult.Failed :
                    prior.expired ? RewardedAdRequestResult.Expired : prior.acknowledged ? RewardedAdRequestResult.AlreadyDelivered : RewardedAdRequestResult.PendingReward;
            }
            var offer = Evaluate(context, provider.IsAvailable);
            if (!offer.Visible) return RewardedAdRequestResult.Hidden;
            int day = Day();
            requesting = true;
            RewardedAdOutcome outcome;
            try { outcome = offer.DirectReward ? RewardedAdOutcome.Completed : provider.Show(context.Slot, completionId); }
            catch (Exception) { outcome = RewardedAdOutcome.Failed; }
            finally { requesting = false; }
            if (!Enum.IsDefined(typeof(RewardedAdOutcome), outcome)) throw new InvalidOperationException("Unknown provider outcome.");
            var next = ledger.Copy(); next.maximumDay = Math.Max(next.maximumDay, day); next.cooling = true;
            next.receipts.Add(new RewardedAdsReceipt { id = completionId, run = context.RunId, claim = context.ClaimId,
                slot = (int)context.Slot, day = day, outcome = (int)outcome,
                ordinal = outcome == RewardedAdOutcome.Completed ? Count(context, day) + 1 : 0 });
            if (!Publish(next)) return RewardedAdRequestResult.PersistencePending;
            return outcome == RewardedAdOutcome.Completed ? RewardedAdRequestResult.PendingReward : RewardedAdRequestResult.Failed;
        }
        // The receiver must atomically persist CompletionId with the wallet effect or replay command.
        // Only acknowledge after that commit; delivery may repeat after a crash or failed acknowledgement.
        public bool Acknowledge(string completionId)
        {
            if (pendingWrite != null) return false;
            var index = ledger.receipts.FindIndex(receipt => receipt.id == completionId);
            if (index < 0 || ledger.receipts[index].outcome != (int)RewardedAdOutcome.Completed) return false;
            if (ledger.receipts[index].expired) return false;
            if (ledger.receipts[index].acknowledged) return true;
            var next = ledger.Copy(); next.receipts[index].acknowledged = true; return Publish(next);
        }
        // The host must prove this run is closed before expiring its undeliverable run command.
        // Preserve the consumed ordinal; expiry never refunds quota or delivers into another run.
        public bool ExpireClosedRunReward(string completionId, string closedRunId)
        {
            if (pendingWrite != null) return false;
            var index = ledger.receipts.FindIndex(receipt => receipt.id == completionId);
            if (index < 0) return false;
            var receipt = ledger.receipts[index];
            if (receipt.run != closedRunId || receipt.acknowledged || receipt.outcome != (int)RewardedAdOutcome.Completed ||
                receipt.slot != (int)RewardedAdSlot.Revive && receipt.slot != (int)RewardedAdSlot.Reroll) return false;
            if (receipt.expired) return true;
            var next = ledger.Copy(); next.receipts[index].expired = true; return Publish(next);
        }
        public bool NotifyMeaningfulGameplay(string runId, long eventSequence)
        {
            if (!RewardedAdsLedger.Identity(runId) || eventSequence < 0) throw new ArgumentException("A stable gameplay event identity is required.");
            if (pendingWrite != null || requesting) return false;
            var prior = ledger.activities.FirstOrDefault(activity => activity.run == runId);
            if (prior != null && eventSequence <= prior.sequence) return false;
            var next = ledger.Copy(); var activity = next.activities.FirstOrDefault(value => value.run == runId);
            if (activity == null) next.activities.Add(new RewardedAdsActivity { run = runId, sequence = eventSequence }); else activity.sequence = eventSequence;
            next.cooling = false; next.maximumDay = Math.Max(next.maximumDay, Day()); return Publish(next);
        }
        // A failed completion save blocks delivery and remains retryable in this process. A future external
        // provider must redeliver stable IDs after process death before persistence; this is not a two-store transaction.
        public bool FlushPendingWrite() => pendingWrite == null || Publish(pendingWrite);
        int Count(RewardedAdContext context, int day) => ledger.receipts.Count(receipt => receipt.slot == (int)context.Slot &&
            receipt.outcome == (int)RewardedAdOutcome.Completed && (context.Slot == RewardedAdSlot.Idle ? receipt.day == day : receipt.run == context.RunId));
        int Day() { int day = localDay(); if (day <= 0) throw new InvalidOperationException("Local calendar day key must be positive and monotonically dated."); return day; }
        bool Publish(RewardedAdsLedger next)
        {
            try { store.Save(next.Encode()); ledger = next; pendingWrite = null; return true; }
            catch (IOException) { pendingWrite = next; return false; }
            catch (UnauthorizedAccessException) { pendingWrite = next; return false; }
        }
    }
}
