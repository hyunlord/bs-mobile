using System.Collections.Generic;

namespace SowSiege.Core
{
    public sealed record WeaponLevelDefinition(int Level, int Damage, int Range, int CooldownTicks, int Count, int Pierce, int Knockback);
    public sealed record WeaponCombatWeaponDefinition(string AttackModel, int BeamHalfWidth, WeaponLevelDefinition[] Levels);
    public sealed record WeaponCombatDefinition(int ContractVersion, IReadOnlyDictionary<string, WeaponCombatWeaponDefinition> Weapons);

    internal sealed class WeaponCombatState
    {
        public Position Facing = new(1, 0);
    }
}
