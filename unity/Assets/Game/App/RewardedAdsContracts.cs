#nullable enable
using System;

namespace Game.App
{
    public enum RewardedAdSlot { Result, Revive, Idle, Reroll }
    public enum RewardedAdOutcome { Completed, Failed, Unavailable, Cancelled }
    public enum RewardedAdRequestResult { Hidden, Failed, PendingReward, AlreadyDelivered, PersistencePending, Expired }
    public enum RewardedAdHiddenReason { None, FirstRun, FirstDay, Unavailable, LimitReached, Cooldown, WrongContext, UndecidedConfiguration, PendingDelivery, PersistencePending, ClockRollback }

    public sealed class RewardedAdContext
    {
        public RewardedAdSlot Slot { get; }
        public string RunId { get; }
        public string ClaimId { get; }
        public int CompletedRuns { get; }
        public bool AdRemovalOwned { get; }
        public bool CardsPending { get; }
        public int RerollsRemaining { get; }
        public bool RevivePending { get; }
        public bool RewardClaimAvailable { get; }
        public bool ResultMultiplierConfigured { get; }
        public bool ReviveRulesConfigured { get; }
        public RewardedAdContext(RewardedAdSlot slot, string runId, string claimId, int completedRuns,
            bool adRemovalOwned, bool cardsPending, int rerollsRemaining, bool revivePending,
            bool rewardClaimAvailable, bool resultMultiplierConfigured = false, bool reviveRulesConfigured = false)
        {
            if (!Enum.IsDefined(typeof(RewardedAdSlot), slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            if (completedRuns < 0 || rerollsRemaining < 0) throw new ArgumentOutOfRangeException(nameof(completedRuns));
            Slot = slot; RunId = runId ?? ""; ClaimId = claimId ?? ""; CompletedRuns = completedRuns;
            AdRemovalOwned = adRemovalOwned; CardsPending = cardsPending; RerollsRemaining = rerollsRemaining;
            RevivePending = revivePending; RewardClaimAvailable = rewardClaimAvailable;
            ResultMultiplierConfigured = resultMultiplierConfigured; ReviveRulesConfigured = reviveRulesConfigured;
        }
    }
    public sealed class RewardedAdOffer
    {
        public RewardedAdHiddenReason HiddenReason { get; }
        public bool Visible => HiddenReason == RewardedAdHiddenReason.None;
        public bool DirectReward { get; }
        internal RewardedAdOffer(RewardedAdHiddenReason reason, bool direct = false) { HiddenReason = reason; DirectReward = direct && Visible; }
    }
    public sealed class RewardedAdIntent
    {
        public string CompletionId { get; }
        public RewardedAdSlot Slot { get; }
        public string RunId { get; }
        public string ClaimId { get; }
        public int Ordinal { get; }
        public int LocalDay { get; }
        internal RewardedAdIntent(RewardedAdsReceipt receipt)
        { CompletionId = receipt.id; Slot = (RewardedAdSlot)receipt.slot; RunId = receipt.run; ClaimId = receipt.claim; Ordinal = receipt.ordinal; LocalDay = receipt.day; }
    }
    public interface IRewardedAdProvider
    {
        bool IsAvailable { get; }
        RewardedAdOutcome Show(RewardedAdSlot slot, string completionId);
    }
    public sealed class RewardedAdsUnavailableProvider : IRewardedAdProvider
    {
        public bool IsAvailable => false;
        public RewardedAdOutcome Show(RewardedAdSlot slot, string completionId) => RewardedAdOutcome.Unavailable;
    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public sealed class RewardedAdsFakeProvider : IRewardedAdProvider
    {
        public bool IsAvailable { get; set; } = true;
        public bool Succeed { get; set; } = true;
        public RewardedAdOutcome Show(RewardedAdSlot slot, string completionId)
            => !IsAvailable ? RewardedAdOutcome.Unavailable : Succeed ? RewardedAdOutcome.Completed : RewardedAdOutcome.Failed;
    }
#endif
}
