using System.Collections.Generic;

namespace SowSiege.Core
{
    public sealed record ArmyAutoPolicy(WorldCombatMode Mode, bool VictorySatisfiesObjective, bool UndefendedSatisfiesObjective);
    public sealed record ArmyAutoMatchup(string AttackerId, string DefenderId, int Multiplier);
    public sealed record ArmyAutoRules(string Version, int MaxRounds, int MaxSlotsPerSide, int MaxStat, int MinDamage,
        int AttackBonusCap, int DefenseBonusCap, int HealthBonusCap, IReadOnlyList<ArmyAutoPolicy> Policies,
        IReadOnlyList<ArmyAutoMatchup> Matchups);
    // Flat additions have a separate contract from direct-combat permille effects.
    public sealed record ArmyAutoEffect(WorldEffectSourceKind Kind, string Id, string ContentHash, int Attack, int Defense, int Health);
    public sealed record ArmyAutoTroop(string Id, int Health, int Attack, int Defense, int TargetPriority, IReadOnlyList<int> Ordinals);
    public sealed record ArmyAutoSide(bool HeroAvailable, IReadOnlyList<ArmyAutoEffect> Effects, IReadOnlyList<ArmyAutoTroop> Troops);
    public sealed record ArmyAutoInput(string ContractHash, WorldCombatMode Mode, string ContentHash, ArmyAutoRules Rules,
        ArmyAutoSide Attacker, ArmyAutoSide Defender, int BattlefieldDefense, int ObjectiveDefense);
    public sealed record ArmyAutoSlotResult(string TroopId, int Ordinal, int Health);
    public sealed record ArmyAutoResult(WorldCombatResult Combat, string InputHash, string ObjectiveStatus,
        IReadOnlyList<WorldTroopResult> DefenderTroops, IReadOnlyList<ArmyAutoSlotResult> AttackerSlots,
        IReadOnlyList<ArmyAutoSlotResult> DefenderSlots);
}
