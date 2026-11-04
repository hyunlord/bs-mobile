using System.Collections.Generic;

namespace SowSiege.Core
{
    public enum HeroActiveKind { Dash }
    public enum HeroSkillKind { Passive, Active }
    public sealed record HeroSkillBinding(string Id, int Version, string ContentHash, HeroSkillKind Kind);
    public sealed record HeroDashParameters(int PreparationTicks, int ThreatRange, int SpeedPerTick, int MaximumDistance,
        int DurationTicks, int CooldownTicks, int MaximumInstances);
    public sealed record HeroActiveDefinition(string Id, int Version, string ContentHash, HeroActiveKind Kind, HeroDashParameters Dash);
    public sealed record HeroActiveWall(string Id, int MinX, int MinY, int MaxX, int MaxY);
    public sealed record HeroActiveEnemyBody(string EnemyId, int Radius, bool EligibleTarget);
    public sealed record HeroActiveOptions(string RulesHash, string ContentHash, string OwnerId, string HeroId,
        string StartingSlotId, string AttackDefinitionId, int HeroRadius, IReadOnlyList<HeroSkillBinding> Skills,
        IReadOnlyList<HeroActiveDefinition> Definitions, IReadOnlyList<HeroActiveWall> Walls, IReadOnlyList<HeroActiveEnemyBody> EnemyBodies);
    public sealed record HeroActiveFrame(int PreparationTicks, int CooldownReadyTick, long ActivationSequence, bool Active,
        int MotionStartTick, int MotionEndTick, int DirectionX, int DirectionY, long RemainderX, long RemainderY, int Distance);
}
