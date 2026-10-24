using System.Collections.Generic;

namespace SowSiege.Core
{
    public sealed record MetaVassalState(int Level, int Rank, int Fragments, bool Unlocked);
    public sealed record MetaPendingRun(long Sequence, string ChapterId, int Seed);
    public sealed record MetaState(int SchemaVersion, int HighestClearedChapter, string ManorPriority,
        Dictionary<string, int> Wallet, Dictionary<string, int> ManorLevels,
        Dictionary<string, MetaVassalState> Vassals, Dictionary<string, long> Metrics,
        string[] CompletedChallenges, string[] UnlockedContentIds, string[] ActiveVassalIds,
        long NextRunSequence, MetaPendingRun? PendingRun, long LastMonotonicSeconds, long LastWallSeconds,
        long IdleRemainderSeconds, long CompletedRuns)
    {
        public MetaRewardReceipt[] RewardReceipts { get; init; } = System.Array.Empty<MetaRewardReceipt>();
        public MetaResultRewardBasis? ResultRewardBasis { get; init; }
        public long NextClaimSequence { get; init; } = 1;
        public MetaPendingClaim[] PendingClaims { get; init; } = System.Array.Empty<MetaPendingClaim>();
        // M3a account estate (#361); null for every account that has not been migrated or started as a prototype estate.
        public MetaEstateState? Estate { get; init; }
    }
    public sealed record MetaRunPlan(MetaState State, MetaPendingRun Run);
    public sealed record MetaRunFacts(int Tick, int DurationTicks, bool Survived, bool BossDefeated, bool Abandoned,
        int Level, int Farms, int Buildings, int People, long Kills, long Harvests, [property: OmitWhenNull] MetaDarkFinishFacts? DarkFinish = null);
    // ADR0064 ignore bonus: estate buildings left asleep in a cleared run raise settlement materials within the run cap.
    public sealed record MetaDarkFinishFacts(int SleepingBuildings, int BonusPermille);
    public sealed record MetaDarkFinish(int SleepingBuildings, int BonusPermille, Dictionary<string, int> Awarded);
    public sealed record MetaSettlement(MetaState State, bool Cleared, Dictionary<string, int> Awarded,
        Dictionary<string, int> Overflow, string[] BuildingsGrown, string[] ChallengesCompleted, [property: OmitWhenNull] MetaDarkFinish? DarkFinish = null);
    public sealed record MetaIdleResult(MetaState State, int CreditedSeconds, string ClockStatus,
        Dictionary<string, int> Awarded, string[] BuildingsGrown);
    public sealed record MetaModifiers(int AttackPermille, int HealthPermille, int MovementPermille,
        int GrowthPermille, int AllyPermille, int ExperiencePermille);
    public sealed record MetaDecodeResult(bool Valid, MetaState? State, string ErrorCode);
}
