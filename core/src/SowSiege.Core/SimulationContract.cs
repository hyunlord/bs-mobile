using System.Text.Json.Serialization;

namespace SowSiege.Core;

// PeopleRule null resolves from JSON World.DefaultPeopleRule. Scenario must be normal or load.
public sealed record RunOptions(int Seed, string HeroId, string EstateId, string Policy, string? PeopleRule = null, string Scenario = "normal", bool ManualCards = false);
public sealed record ToolTelemetry(long ActivationDamage, long GrowthProduced, long GrowthDamage, long Activations);
public sealed record TimeSample(int Tick, int Season, int Level, int Enemies, int Farms, int Buildings, int People, long WeaponDamage, long ToolDamage, long GrowthDamage, long KillExperience, long HarvestExperience, long TaxExperience, int Food, int LordHealth, long AllyDamage);
public sealed record CardChoice(int Tick, string[] Offered, string Chosen, string Rarity, int Level);
public sealed record SimulationResult(string Scope, int Seed, string HeroId, string EstateId, string Policy, string ToolId, int Ticks, long Damage, long Growth, string Hash, string PeopleRule, string Scenario, string EndReason, bool Survived, int Level, int Season, long RandomDraws, long WeaponDamage, IReadOnlyDictionary<string, ToolTelemetry> PerTool, IReadOnlyList<TimeSample> Timeline, long KillExperience, long HarvestExperience, long TaxExperience, int Food, int Peasants, int Militia, int Vassals, int Harvests, int Ruins, int Rebuilds, long EstateTicks, long SpawnedEnemies, string DeathCause, IReadOnlyList<CardChoice> Cards, long AllyDamage, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeResult? Runtime = null);
// Public Simulation API unchanged: ctor(catalog,options); bool IsComplete; void Tick(); SimulationResult Result().
// Additional read-only WorldSnapshot Snapshot for behavioural tests and load evidence.
public sealed record WorldSnapshot(int Tick, int Season, int LordX, int LordY, int LordHealth, int ActiveEnemies, int Farms, int Buildings, int People, int SeedPlots, int RipePlots, int Ruins, int Food, int Peasants, int Militia, int Returning, int Level, int Harvests, int Rebuilds, long SpawnedEnemies, int PopulationMembers);

public sealed record CardOfferSnapshot(string[] Cards, string? LockedCard, int Rerolls, int Bans, int Locks);
