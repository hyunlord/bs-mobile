using System.Collections.Generic;

namespace SowSiege.Core
{
    public enum DiagnosticVariant { Control, OffenseOff, InterceptionOff, BothOff }
    public sealed record DiagnosticOptions(DiagnosticVariant Variant);
    public sealed record DiagnosticAttackSource(string ActorKind, string Role, string SourceId, long AttackAttempts, long NoTargetAttempts, long HitCount, long RequestedDamage, long AppliedHpDamage, long SuppressedHpDamage, long CandidateCount, long ShapeEligibleCount, long EffectiveCooldownSum, long KnockbackDistance);
    public sealed record DiagnosticInterception(long EligibleEnemyAttackCount, long CandidatePersonCount, long InterceptCount, long OriginalTargetInRangeCount, long UnknownOriginalTargetCount, long TargetRefreshDueCount, long ActualPersonHpDamage, long GuardZeroDamageCount, long PreventedByModifierAmount, long ReroutedAttackCount, IReadOnlyDictionary<string, long> InterceptedTargetKinds);
    public sealed record DiagnosticTargetDamage(string TargetKind, long AttackAttempts, long RawTargetDamage, long AppliedTargetDamage);
    public sealed record DiagnosticPopulation(string Role, int Entities, int Members, long MemberTicks, long DeathEvents, long MembersLost);
    public sealed record DiagnosticEquipment(string Id, string Kind, int Rank);
    public sealed record DiagnosticGrowth(long WorkerTicks, long BoostedWorkerTicks, long LaborGrowthBonus, long DraftEvents, long ReturnEvents, long WeaponSlotExcluded, long ToolSlotExcluded, long CharterSlotExcluded, long WeaponChoices, long ToolChoices, long CharterChoices, long RankIncrements);
    public sealed record DiagnosticThreat(long SpawnRequested, long SpawnAdmitted, long CapRejected, long AdmittedHp, long KilledCount, long RemovedHp, int LiveEnemyCount, long LiveHp, int NearLordCount, int NearLordRadius, int EnemyCap, int BaseSpawnCount, int TimeRamp, int Prosperity, int ProsperityContribution, int SeasonMultiplier);
    public sealed record DiagnosticSnapshot(int Tick, bool Terminal, bool Survived, string CensorKind, int LordX, int LordY, int LordHealth, int Level, int Food, int Harvests, long KillExperience, long HarvestExperience, long TaxExperience, long WeaponDamage, long ToolActivationDamage, long ToolGrowthDamage, long AllyDamage, int WeaponSlots, int ToolSlots, int CharterSlots, IReadOnlyList<DiagnosticEquipment> Equipment, IReadOnlyList<DiagnosticPopulation> Population, IReadOnlyList<DiagnosticAttackSource> AttackSources, DiagnosticInterception Interception, IReadOnlyList<DiagnosticTargetDamage> TargetDamage, DiagnosticGrowth Growth, DiagnosticThreat Threat);
    public sealed record DiagnosticRngPoint(int Tick, long Draws);
    public sealed record DiagnosticResult(DiagnosticVariant Variant, IReadOnlyList<DiagnosticSnapshot> Snapshots, IReadOnlyList<DiagnosticRngPoint> RngTrace, IReadOnlyList<DiagnosticAttackSource> AttackSources, DiagnosticInterception Interception, IReadOnlyList<DiagnosticTargetDamage> TargetDamage, DiagnosticGrowth Growth, DiagnosticThreat Threat);

}
