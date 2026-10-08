using System.Collections.Generic;
namespace SowSiege.Core
{
    public sealed record FirstPlayableDefinition(int ContractVersion, IReadOnlyDictionary<string, FirstPlayableWeaponDefinition> Weapons,
        IReadOnlyDictionary<string, FirstPlayableEnemyDefinition> Enemies, MapEventDefinition[] MapEvents,
        IReadOnlyDictionary<string, EvolutionRequirement[]> EvolutionRequirements, int BuildingWorkRequired, int BuildingWorkPerActivation,
        int MaxActiveAttacks, [property: OmitWhenNull] IReadOnlyDictionary<string, EvolutionGrowthRequirement>? EvolutionGrowthRequirements = null);
    public sealed record FirstPlayableWeaponDefinition(string Form, int Speed, int Radius, int LifetimeTicks, int HitIntervalTicks, int ChainRange, int SpreadPermille = 0, int BurstIntervalTicks = 0);
    public sealed record FirstPlayableEnemyDefinition(string Rank, int FirstSpawnTick, int RepeatTicks, int Weight);
    public sealed record MapEventDefinition(string Id, string Kind, int FirstSpawnTick, int RepeatTicks, int LifetimeTicks, int SpawnRadius,
        int InteractRadius, int FoodCost, int RewardCount, int HealAmount, int Experience, int Health, string[] ItemIds);
    public sealed record EvolutionRequirement(string EquipmentId, int MinimumLevel);
    public sealed record EvolutionGrowthRequirement(string Target, int Minimum);
    public sealed record ActiveAttackView(int Id, string SourceId, string Form, WorldPoint Position, WorldPoint PreviousPosition, int Radius, int AgeTicks, int LifetimeTicks, bool IsActive = true, int? PresentationRadius = null);
    public sealed record MapEventView(int Id, string DefinitionId, string Kind, WorldPoint Position, int Health, int MaxHealth, int FoodCost, int ExpiresTick);
    public sealed record BuildingProgressView(int Id, string State, int Work, int RequiredWork);
    public sealed record EvolutionClueView(string Id, bool Activated, bool Available, IReadOnlyList<EvolutionRequirement> Requirements, EvolutionGrowthRequirement? GrowthRequirement);
    public sealed record OfferedCardDetail(string Id, string Rarity, int UpgradeAmount, int CurrentLevel, int NextLevel, IReadOnlyList<string> EvolutionIds);
    public sealed record PersonActivityView(int Id, string Activity);
    public sealed record BossView(string DefinitionId, int EntityId, string Rank, string State, int Health, int MaxHealth);
    public sealed record FirstPlayableFrame(IReadOnlyList<ActiveAttackView> Attacks, IReadOnlyList<MapEventView> MapEvents,
        IReadOnlyList<BuildingProgressView> BuildingProgress, IReadOnlyList<OfferedCardDetail> Cards, IReadOnlyList<EvolutionClueView> Evolutions,
        IReadOnlyList<string> Charters, IReadOnlyDictionary<string, int> Items, long Kills, long Harvests,
        string HeroId, IReadOnlyList<PersonActivityView> People, IReadOnlyList<BossView> Bosses, bool BossDefeated);
}
