using System.Collections.Generic;

namespace SowSiege.Core
{
    // M3a-1 account estate (design #360 section 6, issue #361). The prototype numbers live in a versioned opt-in file
    // (data/opt-in/account-estate/m3a-prototype-r2.json); every value there is a hypothesis (#126), not a release default.
    public sealed record AccountEstateCost(Dictionary<string, int> Cost, int Seconds);
    public sealed record AccountEstateProduction(string Resource, int PerMinute);
    public sealed record AccountEstateBuildingKind(string Kind, int X, int Y, int Width, int Height, AccountEstateCost[] Levels,
        bool StartsComplete = false, string[]? Branches = null, string? Parent = null, AccountEstateProduction? Produces = null)
    {
        // A method, not a property: the Unity export writes records through their constructor parameters only.
        public int MaxLevel() => StartsComplete ? 1 : Levels.Length;
    }
    public sealed record AccountEstateZoneUnlock(int CompletedProductionSites, Dictionary<string, int> Cost, int Seconds);
    public sealed record AccountEstateZone(string Id, int X, int Y, int Width, int Height, bool OpenAtStart, AccountEstateZoneUnlock? Unlock = null);
    public sealed record AccountEstateQueue(int Reservations, bool AutoStartDefault);
    public sealed record AccountEstateStorage(string StorageKind, int[] GranaryLevelSeconds, int LongAwayBonusSeconds, int MaximumSeconds,
        Dictionary<string, int> BaseStock, int[] GranaryLevelStockPermille, int LongAwayStockPermille, int MaximumStockPermille, string LongAwayBranch);
    public sealed record AccountEstateBranchBonus(string Branch, string Resource, int Permille);
    public sealed record AccountEstateProductionRules(int[] ParentLevelPermille, AccountEstateBranchBonus[] BranchBonus);
    public sealed record AccountEstateConstructionRules(string ReductionBranch, int ReductionPermille, int MaximumReductionPermille);
    public sealed record AccountEstateTask(string Id, string Trigger, Dictionary<string, int> Reward);
    public sealed record AccountEstateConfig(string Version, string Status, int MaximumIntervalSeconds, Dictionary<string, int> InitialWallet,
        Dictionary<string, int> WalletCaps, AccountEstateQueue Queue, AccountEstateZone[] Zones, AccountEstateStorage Storage,
        AccountEstateProductionRules Production, AccountEstateConstructionRules Construction, AccountEstateBuildingKind[] Buildings,
        string[] ProtectedPath, AccountEstateTask[] Tasks);

    // Building instance. Level 0 is a ruin site (not built yet). Production sites keep their own stock, the integer remainder of
    // seconds x per-minute x permille (denominator 60000) and the seconds produced since their stock was last emptied.
    public sealed record MetaEstateBuilding(int Id, string Kind, int X, int Y, int Level, string? Branch = null, int Stock = 0, long Remainder = 0, long StoredSeconds = 0);
    public sealed record MetaEstateWork(int BuildingId, int TargetLevel, long TotalSeconds, long RemainingSeconds);
    public sealed record MetaEstateReservation(int BuildingId, int TargetLevel);
    public sealed record MetaEstateZoneWork(string ZoneId, long RemainingSeconds);
    public sealed record MetaEstateState(string ConfigVersion, string Origin, long LastMonotonicSeconds, long LastWallSeconds, long ElapsedSeconds,
        string[] OpenZones, MetaEstateBuilding[] Buildings, MetaEstateWork? Active, MetaEstateReservation[] Reservations, MetaEstateZoneWork? ZoneWork,
        bool AutoStart, string[] AchievedTasks, string[] ClaimedTasks, int NextBuildingId, bool FirstClaimDone, bool FirstMoveDone);

    public enum AccountEstateClock { Initialized, Accepted, ClockMismatch }
    // Every command first applies the verified interval up to the given observation, then acts; Clock says what the clock check did.
    public sealed record AccountEstateResult(MetaState State, AccountEstateClock Clock, long CreditedSeconds, bool Done, string Reason,
        Dictionary<string, int> Moved);
}
