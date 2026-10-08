using System;
using System.Collections.Generic;

namespace SowSiege.Core
{
    public readonly struct WorldPoint
    {
        public int X { get; }
        public int Y { get; }
        public WorldPoint(int x, int y) { X = x; Y = y; }
    }
    public readonly struct PlayerInput
    {
        public const int Scale = 1000;
        public short X { get; }
        public short Y { get; }
        public PlayerInput(short x, short y)
        {
            if (Math.Abs((int)x) > Scale || Math.Abs((int)y) > Scale) { throw new ArgumentOutOfRangeException(nameof(x)); }
            X = x; Y = y;
        }
    }
    public enum AimMode { Movement, NearestEnemy }
    public enum RunStatus { Running, AwaitingCard, Completed }
    public enum ReplayCommandKind { Advance, ChooseCard, RerollCards, BanCard, LockCard, SetAimMode, SetInvulnerable, SetSpawnPermille, GrantLevel }
    public sealed record InteractiveOptions(RunOptions Run, AimMode InitialAimMode, string DataHash);
    public sealed record ReplayCommand(long Sequence, int Tick, ReplayCommandKind Kind, PlayerInput Input = default, string? CardId = null, int Value = 0);
    public interface IReadOnlyRunView
    {
        RunStatus Status { get; }
        RunFrame CaptureFrame();
        CardOfferView CaptureCards();
    }
    public sealed record LordView(WorldPoint Position, WorldPoint Facing, int Health, int MaxHealth);
    public sealed record EnemyView(int Id, string DefinitionId, WorldPoint Position, int Health, int MaxHealth);
    public sealed record FarmView(int Id, string SourceId, WorldPoint Position, int Stage, bool Ripe);
    public sealed record BuildingView(int Id, string SourceId, WorldPoint Position, bool Built, int Health);
    public sealed record PersonView(int Id, string SourceId, WorldPoint Position, string Role, int Members, int Health);
    public sealed record GroundLootView(int Id, string ItemId, WorldPoint Position);
    public sealed record RemainView(int Id, WorldPoint Position);
    public sealed record EquipmentView(string Id, int Level);
    public sealed record EntityCounts(int Enemies, int People, int Farms, int Buildings, int Projectiles);
    public sealed record CardOfferView(IReadOnlyList<string> Cards, string? LockedCardId, int Rerolls, int Bans, int Locks);
    public enum PresentationKind { Attack, KillExperience, HarvestExperience, TaxExperience, BuildingCompleted }
    public sealed record PresentationEvent(long Id, int Tick, PresentationKind Kind, string SourceId, WorldPoint Origin, WorldPoint Direction, string Shape, int Range, long Amount, IReadOnlyList<WorldPoint> Endpoints, IReadOnlyList<int> HitEntityIds, AttackGeometry? Geometry = null);
    public sealed record AttackGeometry(int InnerRadius, int BeamHalfWidth, int Count, int Pierce, IReadOnlyList<WorldPoint> RayEnds);
    public sealed record RunFrame(int Tick, RunStatus Status, int Season, int SeasonTicksRemaining, int DurationTicks, int TickRate, int MapWidth, int MapHeight, LordView Lord, WorldPoint Estate, long Experience, long RequiredExperience, int Level, int EstateExtent, EntityCounts Counts, IReadOnlyList<EnemyView> Enemies, IReadOnlyList<FarmView> Farms, IReadOnlyList<BuildingView> Buildings, IReadOnlyList<PersonView> People, IReadOnlyList<GroundLootView> Loot, IReadOnlyList<RemainView> Remains, IReadOnlyList<EquipmentView> Equipment, IReadOnlyList<PresentationEvent> Events);
    public sealed record RunSummary(int Seed, int Tick, bool Survived, string EndReason, int Level, long KillExperience, long HarvestExperience, long TaxExperience, long WeaponDamage, long ToolActivationDamage, long ToolGrowthDamage, long AllyDamage, string StateHash);
}
