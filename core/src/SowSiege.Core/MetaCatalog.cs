using System.Collections.Generic;

namespace SowSiege.Core
{
    public enum MetaAbility { Attack, Health, Movement, Growth, Allies, Experience }
    public enum ManorEffect { VassalLevelCap, Research, IdleCapacity, VassalSlots }
    public enum MetaMetric { Runs, Clears, HighestChapter, Kills, Harvests, Buildings, People, Bosses, SurvivalTicks, VassalLevel }
    public sealed record MetaMaterial(string Id, string Name, int WalletCap, string ArtRole);
    public sealed record MetaTerrain(string Kind, int XPermille, int YPermille, int WidthPermille, int HeightPermille, string ArtRole);
    public sealed record MetaChapter(string Id, int Index, string Name, string Description, int WidthPermille, int HeightPermille,
        int SiteCount, int SiteSpacing, int FarmCapacity, int ThreatPermille, int EnemyHealthPermille, int EnemyDamagePermille,
        string[] EnemyIds, string BossId, MetaTerrain[] Terrain, Dictionary<string, int> RewardCaps);
    public sealed record MetaManorBuilding(string Id, string Name, string Description, string ArtRole, ManorEffect Effect,
        int MaxLevel, Dictionary<string, int> BaseCost, Dictionary<string, int> CostPerLevel);
    public sealed record MetaVassal(string Id, string Name, string Description, string ArtRole, MetaAbility Ability,
        bool InitiallyUnlocked, int BasePermille, int PerLevelPermille, int PerRankPermille, int MaxLevel, int MaxRank,
        int FragmentCap, int[] RankCosts, Dictionary<string, int> LevelCostBase, Dictionary<string, int> LevelCostStep);
    public sealed record MetaChallenge(string Id, string Name, MetaMetric Metric, long Target, int ResearchLevel,
        string[] UnlockContentIds, string[] UnlockVassalIds, Dictionary<string, int> Rewards);
    public sealed record MetaRewardRule(string MaterialId, int PerFarm, int PerBuilding, int PerPerson, int PerHarvest,
        int PerLevel, int PerBoss);
    public sealed record MetaEconomy(int RepeatPermille, int DeathPermille, int AbandonPermille, int MinimumRewardTicks,
        int IdleStepSeconds, int BaseIdleCapSeconds, int IdleCapSecondsPerLevel, int MaximumIdleCapSeconds,
        Dictionary<string, int> IdlePerStep, int ClockToleranceSeconds, int BaseVassalLevelCap, int LevelCapPerForgeLevel,
        int BaseVassalSlots, int BarracksLevelsPerSlot, int MaximumVassalSlots, int FragmentsPerClear, int FragmentsPerDeath,
        MetaRewardRule[] RewardRules);
    public sealed record MetaCatalog(int ContractVersion, MetaMaterial[] Materials, MetaChapter[] Chapters,
        MetaManorBuilding[] ManorBuildings, MetaVassal[] Vassals, MetaChallenge[] Challenges,
        string[] InitialContentIds, MetaEconomy Economy);
}
