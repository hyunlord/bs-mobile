using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{

    public sealed record Activation(int Damage, int Range, int CooldownTicks, string Shape, int Knockback);
    public sealed record Growth(string Target, int Yield);
    public sealed record ToolDefinition(string Id, string[] Tags, Activation Activation, Growth Growth, string FloorRationale, string[] AntiSynergy);
    public sealed record WeaponDefinition(string Id, string[] Tags, Activation Activation);
    public sealed record HeroDefinition(string Id, string StartingTool, int DamageMultiplier);
    public sealed record RemainsLoopDefinition(int Capacity, int LifetimeTicks, int AbsorptionRadius);
    public sealed record EstateDefinition(string Id, int GrowthMultiplier, [property: OmitWhenNull] RemainsLoopDefinition? RemainsLoop = null);
    public sealed record PolicyDefinition(int DamageMultiplier, int GrowthMultiplier, Dictionary<string, int> CardWeights);
    public sealed record SeasonDefinition(string Name, int DurationTicks, int GrowthMultiplier, int SpawnMultiplier);
    public sealed record EnemyDefinition(string Id, string Target, int Health, int Speed, int Damage, int Range, int AttackCooldownTicks, int Experience);
    public sealed record RarityDefinition(string Name, int Weight, int UpgradeAmount);
    public sealed record MapTuning(int Width, int Height, int CellSize, int LordSpeed, int LordHealth, int EstateRadius, int WaypointPeriodTicks);
    public sealed record FarmTuning(int Capacity, int Spacing, int[] StageTicks, int HarvestRange, int FoodPerHarvest, int ExperiencePerHarvest, int FertilityPerKill, int FertilityGrowthBonus);
    public sealed record BuildingTuning(int SiteCount, int Spacing, int Health, int RepairAmount, int Damage, int Range, int AttackCooldownTicks, int TaxPeriodTicks, int TaxExperience);
    public sealed record PeopleTuning(int InitialPeasants, int MaxPeople, int InitialFood, int FoodCapacity, int FoodPerPerson, int ConsumePeriodTicks, int RecruitPeriodTicks, int DraftDurationTicks, int ReturnSpeed, int WorkerGrowthBonus, int Damage, int Range, int AttackCooldownTicks, int VassalHealth, int SquadSize);
    public sealed record ProgressionTuning(int BaseExperience, int ExperiencePerLevel, int CardCount, int WeaponSlots, int ToolSlots, string StartingWeapon, RarityDefinition[] Rarities, int Rerolls, int Bans, int Locks);
    public sealed record ThreatTuning(int SpawnPeriodTicks, int BaseSpawnCount, int TimeRampTicks, int ProsperityDivisor, int EnemyCap, int SpawnInset, int ContactPeriodTicks);
    public sealed record LoadTuning(int Enemies, int Farms, int Buildings, int People);
    public sealed record WorldTuning(MapTuning Map, FarmTuning Farms, BuildingTuning Buildings, PeopleTuning People, ProgressionTuning Progression, ThreatTuning Threat, LoadTuning Load, SeasonDefinition[] Seasons, int TelemetryPeriodTicks, string DefaultPeopleRule);
    public sealed record Tuning(int TickRate, int DurationTicks, int DamageRollMax, string DefaultHero, string DefaultEstate, Dictionary<string, PolicyDefinition> Policies, WorldTuning World);
    public sealed record ContentCatalog(Tuning Tuning, IReadOnlyDictionary<string, ToolDefinition> Tools, IReadOnlyDictionary<string, HeroDefinition> Heroes, IReadOnlyDictionary<string, EstateDefinition> Estates, IReadOnlyDictionary<string, WeaponDefinition> Weapons, IReadOnlyDictionary<string, EnemyDefinition> Enemies, [property: OmitWhenNull] RuntimeCatalog? Runtime = null, [property: OmitWhenNull] ExperimentDefinition? Experiment = null);

}
