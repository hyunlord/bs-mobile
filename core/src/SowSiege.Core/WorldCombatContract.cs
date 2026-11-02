using System.Collections.Generic;

namespace SowSiege.Core
{
    public enum WorldCombatMode { DirectPve, AutoPve, CityPvp }
    public enum WorldObjectiveKind { DefeatDesignated, HoldMarker, TimeLimit }
    public enum WorldEffectSourceKind { Hero, Vassal, Research }
    public sealed record WorldCombatProvenance(string ProfileId, string ProfileHash, string RulesId, int RulesVersion,
        string RulesHash, string ContentHash, int ArithmeticVersion, int RemainderVersion, int OrderVersion,
        int TickRate, int Seed, string RngAlgorithm);
    public sealed record WorldBattlefield(int MinX, int MinY, int MaxX, int MaxY);
    public sealed record WorldCombatStats(int Health, int Attack, int Defense, int Speed, int Range, int CooldownTicks);
    public sealed record WorldTroopSlot(int Ordinal, int X, int Y);
    public sealed record WorldTroopType(string Id, int N, WorldCombatStats Stats, IReadOnlyList<WorldTroopSlot> Slots);
    public sealed record WorldSpawnCohort(string Id, string EnemyId, int SpawnTick, int Count, int X, int Y, WorldCombatStats Stats);
    public sealed record WorldMarker(string Id, int X, int Y, int Radius, int Health, bool Required);
    public sealed record WorldObjective(string Id, WorldObjectiveKind Kind, string TargetId, int RequiredCount, int HoldTicks, int LimitTicks);
    public sealed record WorldHeroSkill(string Id, int Version, string ContentHash);
    public sealed record WorldHeroSnapshot(string Id, string ContentHash, IReadOnlyList<WorldHeroSkill> Skills);
    public sealed record WorldInputPolicy(int InputGraceMilliseconds, int MaximumChunkCommands, int MaximumChunkBytes);
    public sealed record WorldRawEffect(WorldEffectSourceKind Kind, string SourceId, string ContentHash, int Raw);
    public sealed record WorldResolvedEffect(MetaAbility Ability, int Raw, int Cap, int Bonus, bool HeroEligible,
        bool StandingEligible, bool TemporaryEligible, IReadOnlyList<WorldRawEffect> Sources);
    public sealed record WorldCombatSnapshot(string ContractHash, WorldCombatMode Mode, WorldCombatProvenance Provenance,
        WorldBattlefield Battlefield, WorldHeroSnapshot Hero, WorldInputPolicy Input, IReadOnlyList<WorldObjective> Objectives,
        IReadOnlyList<WorldTroopType> Troops, IReadOnlyList<WorldSpawnCohort> Cohorts, IReadOnlyList<WorldMarker> Markers,
        IReadOnlyList<WorldResolvedEffect> Effects);
    public sealed record WorldCombatOptions(WorldCombatSnapshot Snapshot);
}
