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
        long IdleRemainderSeconds, long CompletedRuns);
    public sealed record MetaRunPlan(MetaState State, MetaPendingRun Run);
    public sealed record MetaRunFacts(int Tick, int DurationTicks, bool Survived, bool BossDefeated, bool Abandoned,
        int Level, int Farms, int Buildings, int People, long Kills, long Harvests);
    public sealed record MetaSettlement(MetaState State, bool Cleared, Dictionary<string, int> Awarded,
        Dictionary<string, int> Overflow, string[] BuildingsGrown, string[] ChallengesCompleted);
    public sealed record MetaIdleResult(MetaState State, int CreditedSeconds, string ClockStatus,
        Dictionary<string, int> Awarded, string[] BuildingsGrown);
    public sealed record MetaModifiers(int AttackPermille, int HealthPermille, int MovementPermille,
        int GrowthPermille, int AllyPermille, int ExperiencePermille);
    public sealed record MetaDecodeResult(bool Valid, MetaState? State, string ErrorCode);
}
