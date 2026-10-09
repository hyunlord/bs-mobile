using System;
using System.Collections.Generic;
namespace SowSiege.Core
{
    public enum WaveAttackKind
    {
        Arc, Orbit, Chain, Homing, HarvestArc, SeedFan, WaterFan, ConstructionSlam, MusterWave
    }
    public enum WaveItemKind
    {
        SeedDetour, CarryWater, HarvestGuard, FrontOrbit, RaiderAim, FieldMeal, PickupRadius, MoveSpeed
    }
    public enum WaveEvolutionKind
    {
        PlantingArc, RepairOrbit, ShelteredPlot
    }
    public enum WaveEnemyKind
    {
        Pursuer, SeedThief, RipeGrazer, Charger, Shield, Ranged, FloodBoss
    }
    public sealed record WaveGearLevel(int Damage, int Range, int CooldownTicks, int Speed, int Count, int LifetimeTicks, int Knockback);
    public sealed record WaveGearDefinition(string Id, string DesignRef, WaveAttackKind Kind, int Damage, int Range, int CooldownTicks, int Speed, int Count, int LifetimeTicks, int Knockback, int WorkTicks, int WorkRadius, int RewardExperience, int Capacity, [property: OmitWhenNull] WaveGearLevel[]? Levels = null);
    public sealed record WaveItemDefinition(string Id, string DesignRef, WaveItemKind Kind, string[] EquipmentIds, int Amount);
    public sealed record WaveEvolutionDefinition(string Id, string DesignRef, WaveEvolutionKind Kind, string[] InputIds);
    public sealed record WaveEnemyDefinition(string Id, string DesignRef, WaveEnemyKind Kind, int TellTicks, int ActiveTicks, int RecoveryTicks, int ProjectileSpeed, int FirstSpawnTick);
    public sealed record WaveRuntimeDefinition(string Revision, string ChapterId, string ChapterDesignRef, string BossId, int BossSpawnTick, int GroupCap, int InitialWorkers, int InitialTimber, int WaterCapacity, int WaterRefillTicks, int PickupRadius, int WetTicks, int StopTicks, int PathSpacing, int PathCapacity, IReadOnlyDictionary<string, WaveGearDefinition> Gear, IReadOnlyDictionary<string, WaveItemDefinition> Items, IReadOnlyDictionary<string, WaveEvolutionDefinition> Evolutions, IReadOnlyDictionary<string, WaveEnemyDefinition> Enemies, [property: OmitWhenNull] IReadOnlyDictionary<string, string>? MaterialTargets = null, int DryAfterTicks = 0);
    public interface IWaveRunView
    {
        WaveRuntimeFrame? CaptureWaveRuntime();
    }
    public sealed record WaveWorkView(int Id, string Source, string Kind, WorldPoint Position, int Progress, int Required, bool Complete, int Health, int ParentId, bool Protected, bool Dormant, bool Wet, bool ShipmentActive, bool Dry);
    public sealed record WaveRewardView(int Id, string Source, WorldPoint Position, int Experience, string CompletionKey);
    public sealed record WaveProjectileView(int Id, string Source, WorldPoint Position, WorldPoint Previous, int TargetId, bool Hostile);
    public sealed record WaveDetourView(WorldPoint Position, int Radius, int UntilTick, string Source);
    public sealed record WaveAttackView(string Source, string Kind, WorldPoint Origin, WorldPoint Position, int Radius, int ExpireTick, int ActivationId = -1);
    public sealed record WaveEnemyView(int Id, string Phase, WorldPoint Origin, WorldPoint Target, int UntilTick, bool Wet, bool Stopped, int BossPhase);
    public sealed record WaveGroupView(int Id, string Source, WorldPoint Position, string Phase, int Mission, int Training, int ReservedFood, int FrontRank, string Formation);
    public sealed record WaveEvent(long Id, int Tick, string Kind, string Source, int SubjectId, WorldPoint Position, WorldPoint Target, int Amount);
    public sealed record WaveRuntimeFrame(string Revision, string ChapterId, int Water, int Timber, int AvailableWorkers, bool BossDefeated, IReadOnlyList<WaveWorkView> Work, IReadOnlyList<WaveRewardView> Rewards, IReadOnlyList<WaveProjectileView> Projectiles, IReadOnlyList<WaveEnemyView> Enemies, IReadOnlyList<WaveGroupView> Groups, IReadOnlyList<WorldPoint> Paths, IReadOnlyList<string> Items, IReadOnlyList<string> Evolutions, IReadOnlyList<WaveEvent> Events, IReadOnlyDictionary<string, long> Counters, IReadOnlyList<WaveAttackView> Attacks, int CarriedWater, IReadOnlyList<WaveDetourView> Detours);
}
