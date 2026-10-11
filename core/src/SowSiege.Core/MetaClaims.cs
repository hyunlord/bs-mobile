using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public enum MetaRewardDelivery { LegacyAutoCredit, PendingClaims }
    public enum MetaClaimKind { Idle, Challenge }
    public sealed record MetaPendingClaim(long Sequence, MetaClaimKind Kind, string SourceId,
        Dictionary<string, int> Rewards, int IdleSeconds);
    public sealed record MetaClaimResult(MetaState State, bool Claimed, Dictionary<string, int> Awarded,
        Dictionary<string, int> Overflow, string[] BuildingsGrown);

    public static partial class MetaEngine
    {
        public static MetaClaimResult Claim(MetaCatalog catalog, MetaState state, long sequence)
        {
            var claim = state.PendingClaims.SingleOrDefault(x => x.Sequence == sequence);
            if (claim == null)
            {
                return new MetaClaimResult(Clone(state), false, new Dictionary<string, int>(),
                    new Dictionary<string, int>(), Array.Empty<string>());
            }
            var next = Clone(state) with
            {
                PendingClaims = state.PendingClaims.Where(x => x.Sequence != sequence)
                .Select(x => x with { Rewards = new Dictionary<string, int>(x.Rewards) }).ToArray()
            };
            if (claim.Kind == MetaClaimKind.Challenge)
            {
                var challenge = catalog.Challenges.Single(x => x.Id == claim.SourceId);
                next = next with
                {
                    CompletedChallenges = next.CompletedChallenges.Append(challenge.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    UnlockedContentIds = next.UnlockedContentIds.Union(challenge.UnlockContentIds).OrderBy(x => x, StringComparer.Ordinal).ToArray()
                };
                foreach (string id in challenge.UnlockVassalIds)
                {
                    next.Vassals[id] = next.Vassals[id] with { Unlocked = true };
                }
            }
            var awarded = Credit(catalog, next, claim.Rewards, out var overflow);
            next = CompleteProgression(catalog, next, out var grown, out _, MetaRewardDelivery.PendingClaims);
            return new MetaClaimResult(next, true, awarded, overflow, grown);
        }

        private static MetaState QueueClaim(MetaState state, MetaClaimKind kind, string sourceId,
            Dictionary<string, int> rewards, int idleSeconds)
        {
            if (state.NextClaimSequence == long.MaxValue)
            {
                throw new InvalidOperationException("Claim sequence exhausted.");
            }
            var claim = new MetaPendingClaim(state.NextClaimSequence, kind, sourceId,
                new Dictionary<string, int>(rewards), idleSeconds);
            return state with
            {
                NextClaimSequence = state.NextClaimSequence + 1,
                PendingClaims = state.PendingClaims.Append(claim).ToArray()
            };
        }

        private static MetaState QueueIdle(MetaState state, Dictionary<string, int> rewards, int seconds)
        {
            if (seconds == 0)
            {
                return state;
            }
            var old = state.PendingClaims.SingleOrDefault(x => x.Kind == MetaClaimKind.Idle);
            if (old == null)
            {
                return QueueClaim(state, MetaClaimKind.Idle, "idle", rewards, seconds);
            }
            var combined = new Dictionary<string, int>(old.Rewards);
            foreach (var reward in rewards)
            {
                combined.TryGetValue(reward.Key, out int previous);
                combined[reward.Key] = (int)Math.Min(int.MaxValue, (long)previous + reward.Value);
            }
            var merged = old with { Rewards = combined, IdleSeconds = checked(old.IdleSeconds + seconds) };
            return state with { PendingClaims = state.PendingClaims.Select(x => x.Sequence == old.Sequence ? merged : x).ToArray() };
        }
    }
}
