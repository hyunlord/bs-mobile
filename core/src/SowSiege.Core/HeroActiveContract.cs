using System.Collections.Generic;

namespace SowSiege.Core
{
    public enum HeroActiveKind { Dash, TauntShield, ZoneSlow }
    public enum HeroSkillKind { Passive, Active }
    public sealed record HeroSkillBinding(string Id, int Version, string ContentHash, HeroSkillKind Kind);
    public sealed record HeroDashParameters(int PreparationTicks, int ThreatRange, int SpeedPerTick, int MaximumDistance,
        int DurationTicks, int CooldownTicks, int MaximumInstances);
    public sealed record HeroActiveDefinition(string Id, int Version, string ContentHash, HeroActiveKind Kind, HeroDashParameters? Dash, [property: OmitWhenNull] HeroTauntParameters? Taunt = null, [property: OmitWhenNull] HeroZoneParameters? Zone = null);
    public sealed record HeroTauntEnemyPolicy(string EnemyId, bool Eligible);
    public sealed record HeroTauntParameters(int PreparationTicks, int Radius, int StopRadius, int DurationTicks,
        int CooldownTicks, int MaximumInstances, int MaximumTargets, int ShieldHalfAngleCosPermille,
        int DamageReductionPermille, int DamageReductionCapPermille, IReadOnlyList<HeroTauntEnemyPolicy> EnemyPolicies);
    public sealed record HeroZoneEnemyPolicy(string EnemyId, bool Eligible);
    public sealed record HeroZoneParameters(int TargetRange, int TellTicks, int Radius, int DurationTicks,
        int CooldownTicks, int MaximumInstances, int MaximumTargets, int SlowPermille, int SlowCapPermille,
        int MinimumPositiveSpeed, IReadOnlyList<HeroZoneEnemyPolicy> EnemyPolicies);
    public sealed record HeroZoneCooldownFrame(string DefinitionId, int CooldownReadyTick);
    public sealed record HeroZoneInstanceFrame(string OwnerId, string DefinitionId, long Sequence, int StartTick,
        int ActiveStartTick, int EndTick, WorldPoint Center, bool Active);
    public sealed record HeroSlowSourceFrame(string OwnerId, long ZoneSequence, int TargetId, int Magnitude, int ValidUntilTick);
    public sealed record HeroZoneFrame(IReadOnlyList<HeroZoneCooldownFrame> Definitions,
        IReadOnlyList<HeroZoneInstanceFrame> Zones, IReadOnlyList<HeroSlowSourceFrame> Sources);
    public sealed record HeroSlowFrame(int RawPermille, int CappedPermille, int ValidUntilTick);
    public sealed record HeroActiveCooldownFrame(string DefinitionId, int PreparationTicks, int CooldownReadyTick);
    public sealed record HeroTauntInstanceFrame(string OwnerId, string DefinitionId, long Sequence, int StartTick,
        int EndTick, WorldPoint Center, WorldPoint Direction, bool ShieldAvailable, IReadOnlyList<int> Claims);
    public sealed record HeroActiveEffectsFrame(long ActivationSequence, IReadOnlyList<HeroActiveCooldownFrame> Definitions,
        IReadOnlyList<HeroTauntInstanceFrame> Taunts);
    public sealed record HeroActiveWall(string Id, int MinX, int MinY, int MaxX, int MaxY);
    public sealed record HeroActiveEnemyBody(string EnemyId, int Radius, bool EligibleTarget);
    public sealed record HeroActiveOptions(string RulesHash, string ContentHash, string OwnerId, string HeroId,
        string StartingSlotId, string AttackDefinitionId, int HeroRadius, IReadOnlyList<HeroSkillBinding> Skills,
        IReadOnlyList<HeroActiveDefinition> Definitions, IReadOnlyList<HeroActiveWall> Walls, IReadOnlyList<HeroActiveEnemyBody> EnemyBodies);
    public sealed record HeroActiveFrame(int PreparationTicks, int CooldownReadyTick, long ActivationSequence, bool Active,
        int MotionStartTick, int MotionEndTick, int DirectionX, int DirectionY, long RemainderX, long RemainderY, int Distance);
}
