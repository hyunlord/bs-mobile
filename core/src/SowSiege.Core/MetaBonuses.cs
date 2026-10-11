using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public enum MetaBonusKind { Result, Idle }
    public sealed record MetaRewardReceipt(string ReceiptId, MetaBonusKind Kind, long SourceSequence);
    public sealed record MetaResultRewardBasis(long RunSequence, Dictionary<string, int> Awarded);
    // Extra reward only: 1000 means one additional copy. Production supplies explicitly approved parameters.
    public sealed record MetaBonusPolicy(int BonusPermille, Dictionary<string, int> MaximumBonus);
    public sealed record MetaBonusResult(MetaState State, bool Granted, Dictionary<string, int> BaseAwarded,
        Dictionary<string, int> BonusAwarded, Dictionary<string, int> Overflow, string[] BuildingsGrown);

    public static partial class MetaEngine
    {
        public static MetaBonusResult ClaimIdleWithBonus(MetaCatalog catalog, MetaState state, long claimSequence,
            string receiptId, MetaBonusPolicy policy)
        {
            ValidateBonus(catalog, state, receiptId, policy);
            if (HasReceipt(state, receiptId, MetaBonusKind.Idle, claimSequence)) { return NoBonus(state); }
            var claim = state.PendingClaims.SingleOrDefault(x => x.Sequence == claimSequence && x.Kind == MetaClaimKind.Idle);
            if (claim == null) { return NoBonus(state); }
            var next = Clone(state) with { PendingClaims = state.PendingClaims.Where(x => x.Sequence != claimSequence).ToArray() };
            var basis = Credit(catalog, next, claim.Rewards, out var overflow);
            return CreditBonus(catalog, next, receiptId, MetaBonusKind.Idle, claimSequence, policy, basis, basis, overflow);
        }

        public static MetaBonusResult GrantResultBonus(MetaCatalog catalog, MetaState state, long runSequence,
            string receiptId, MetaBonusPolicy policy)
        {
            ValidateBonus(catalog, state, receiptId, policy);
            if (HasReceipt(state, receiptId, MetaBonusKind.Result, runSequence)) { return NoBonus(state); }
            var basis = state.ResultRewardBasis;
            if (basis == null || basis.RunSequence != runSequence) { return NoBonus(state); }
            return CreditBonus(catalog, Clone(state), receiptId, MetaBonusKind.Result, runSequence, policy,
                basis.Awarded, new Dictionary<string, int>(), new Dictionary<string, int>());
        }

        private static MetaBonusResult CreditBonus(MetaCatalog catalog, MetaState next, string receiptId,
            MetaBonusKind kind, long sequence, MetaBonusPolicy policy, Dictionary<string, int> basis,
            Dictionary<string, int> baseAwarded, Dictionary<string, int> overflow)
        {
            if (next.RewardReceipts.Length >= 4096) { throw new InvalidOperationException("Reward receipt ledger is full; no reward was granted."); }
            var requested = new Dictionary<string, int>();
            foreach (var material in catalog.Materials)
            {
                basis.TryGetValue(material.Id, out int amount);
                requested[material.Id] = (int)Math.Min(policy.MaximumBonus[material.Id], (long)amount * policy.BonusPermille / 1000);
            }
            var bonus = Credit(catalog, next, requested, out var bonusOverflow);
            foreach (var entry in bonusOverflow)
            {
                overflow.TryGetValue(entry.Key, out int existing);
                overflow[entry.Key] = (int)Math.Min(int.MaxValue, (long)existing + entry.Value);
            }
            next = next with { RewardReceipts = next.RewardReceipts.Append(new MetaRewardReceipt(receiptId, kind, sequence)).ToArray() };
            next = CompleteProgression(catalog, next, out var grown, out _, MetaRewardDelivery.PendingClaims);
            return new MetaBonusResult(next, true, baseAwarded, bonus, overflow, grown);
        }

        private static bool HasReceipt(MetaState state, string receiptId, MetaBonusKind kind, long sequence) =>
            state.RewardReceipts.Any(x => x.ReceiptId == receiptId || (x.Kind == kind && x.SourceSequence == sequence));
        private static MetaBonusResult NoBonus(MetaState state) => new MetaBonusResult(Clone(state), false,
            new Dictionary<string, int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), Array.Empty<string>());
        private static void ValidateBonus(MetaCatalog catalog, MetaState state, string receiptId, MetaBonusPolicy policy)
        {
            if (state.PendingRun != null) { throw new InvalidOperationException("Bonuses require a settled account."); }
            if (string.IsNullOrWhiteSpace(receiptId) || System.Text.Encoding.UTF8.GetByteCount(receiptId) > 1024)
            { throw new ArgumentException("A stable receipt identity is required.", nameof(receiptId)); }
            if (policy == null || policy.BonusPermille < 0 || policy.MaximumBonus == null ||
                policy.MaximumBonus.Count != catalog.Materials.Length || catalog.Materials.Any(x => !policy.MaximumBonus.TryGetValue(x.Id, out int cap) || cap < 0))
            { throw new ArgumentException("Every material requires an explicit nonnegative bonus cap and multiplier.", nameof(policy)); }
        }
    }
}
