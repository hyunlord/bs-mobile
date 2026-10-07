namespace SowSiege.Core;

public sealed record RuntimeCondition(string Kind, string? Value = null, int Minimum = 0);
public sealed record RuntimeEffectDefinition(string Id, string Trigger, string Operation, string Subject, int Amount, int Radius, int DurationTicks, int FoodCost, RuntimeCondition[] Conditions);
public sealed record GrowthActionDefinition(string Target, string Operation, int Yield, int DurationTicks);
public sealed record EquipmentRuntimeDefinition(string Id, GrowthActionDefinition[] GrowthActions, RuntimeEffectDefinition[] Effects);
public sealed record CharterDefinition(string Id, string PolicyCategory, RuntimeEffectDefinition[] Effects);
public sealed record ItemDefinition(string Id, string[] RequiredTags, RuntimeEffectDefinition[] Effects);
public sealed record EvolutionDefinition(string Id, string Kind, string[] InputIds, string BaseId, RuntimeEffectDefinition[] Effects);
public sealed record LootSourceDefinition(string Id, string Kind, int Weight, int FoodCost);
public sealed record RuntimeTuning(int WeaponSlots, int ToolSlots, int CharterSlots, int RecentExperienceWindowTicks, int ExperienceWeightDivisor, int LootPeriodTicks, int LootSpawnRadius, int LootPickupRadius, int MaxGroundLoot, int LootQuantity, LootSourceDefinition[] LootSources);
public sealed record RuntimeCatalog(RuntimeTuning Tuning, IReadOnlyDictionary<string, EquipmentRuntimeDefinition> Equipment, IReadOnlyDictionary<string, CharterDefinition> Charters, IReadOnlyDictionary<string, ItemDefinition> Items, IReadOnlyDictionary<string, EvolutionDefinition> Evolutions);
public sealed record RuntimeEffectTelemetry(string EffectId, string SourceId, string SourceKind, string Trigger, string Operation, string Subject, int Amount, long ActivationCount, long AppliedTotal, int? FirstActivationTick, int? LastActivationTick);
public sealed record LootAcquisition(int Tick, string SourceKind, string SourceId, int SourceEntityId, string ItemId, int Quantity, int StackAfter, int FoodPaid);
public sealed record EvolutionActivation(int Tick, string EvolutionId, string BaseId);
public sealed record RuntimeBuildSnapshot(string[] Weapons, string[] Tools, IReadOnlyDictionary<string, int> Charters, IReadOnlyDictionary<string, int> ItemStacks, string[] Evolutions);
public sealed record RuntimeResult(IReadOnlyList<RuntimeEffectTelemetry> Effects, IReadOnlyList<LootAcquisition> Loot, IReadOnlyList<EvolutionActivation> EvolutionActivations, RuntimeBuildSnapshot Build, IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> ToolGrowthByTarget);
