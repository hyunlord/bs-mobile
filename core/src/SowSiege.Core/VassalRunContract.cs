using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public sealed record VassalEffectRule(MetaAbility Ability, int Base, int PerLevel, int PerRank, int Cap);
    public sealed record VassalDefinition(string Id, MetaAbility Ability);
    public sealed record VassalCapRules(int BaseSlots, int MaximumSlots, int BarracksLevelsPerSlot, int BaseLevelCap,
        int LevelCapPerForgeLevel, int MaximumLevel, int MaximumRank);
    public sealed record VassalRunRules(string Id, int Version, IReadOnlyList<VassalDefinition> Vassals,
        IReadOnlyList<VassalEffectRule> Effects, VassalCapRules Caps, string ContentHash);
    public sealed record VassalRosterEntry(string Id, int Level, int Rank, bool Eligible = true);
    public sealed record VassalEffect(MetaAbility Ability, int Raw, int Bonus);
    public sealed record VassalRunSnapshot(string RulesId, int RulesVersion, string ContentHash, int ArithmeticVersion,
        int RemainderVersion, int EffectiveSlots, int EffectiveLevelCap, int ForgeLevel, int BarracksLevel,
        string BasisHash, IReadOnlyList<VassalRosterEntry> Roster, IReadOnlyList<VassalEffect> Effects);
    public sealed record VassalRunOptions(VassalRunSnapshot Snapshot, VassalRunRules Rules);

    public static class VassalRun
    {
        public static VassalRunOptions Create(MetaCatalog meta, MetaState state, VassalRunRules rules)
        {
            if (meta is null || state is null) { throw new ArgumentNullException(nameof(meta)); }
            ValidateRules(rules);
            var economy = meta.Economy; var caps = rules.Caps;
            Require(economy.BaseVassalSlots == caps.BaseSlots && economy.MaximumVassalSlots == caps.MaximumSlots &&
                economy.BarracksLevelsPerSlot == caps.BarracksLevelsPerSlot && economy.BaseVassalLevelCap == caps.BaseLevelCap &&
                economy.LevelCapPerForgeLevel == caps.LevelCapPerForgeLevel, "Account and run vassal cap rules differ.");
            Require(MetaValidation.ValidateState(meta, state).Length == 0, "Invalid account state.");
            Require(meta.Vassals.Length == rules.Vassals.Count && rules.Vassals.All(d => meta.Vassals.Any(v => v.Id == d.Id && v.Ability == d.Ability)), "Vassal definitions differ from account catalog.");
            int Level(ManorEffect effect) => meta.ManorBuildings.Where(b => b.Effect == effect).Sum(b => state.ManorLevels[b.Id]);
            int forge = Level(ManorEffect.VassalLevelCap), barracks = Level(ManorEffect.VassalSlots);
            int slots = Slots(caps, barracks), cap = LevelCap(caps, forge);
            var roster = state.ActiveVassalIds.OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => new VassalRosterEntry(id, state.Vassals[id].Level, state.Vassals[id].Rank, state.Vassals[id].Unlocked)).ToArray();
            var snapshot = new VassalRunSnapshot(rules.Id, rules.Version, rules.ContentHash, 1, 1, slots, cap,
                forge, barracks, VassalRunCodec.HashBasis(rules, forge, barracks), roster, Calculate(rules, roster));
            return Freeze(new(snapshot, rules));
        }

        public static VassalRunOptions Freeze(VassalRunOptions options)
        {
            Validate(options);
            return new(options.Snapshot with { Roster = Array.AsReadOnly(options.Snapshot.Roster.ToArray()), Effects = Array.AsReadOnly(options.Snapshot.Effects.ToArray()) },
                options.Rules with { Vassals = Array.AsReadOnly(options.Rules.Vassals.ToArray()), Effects = Array.AsReadOnly(options.Rules.Effects.ToArray()) });
        }

        public static void Validate(VassalRunOptions options)
        {
            if (options is null || options.Snapshot is null) { throw new ArgumentNullException(nameof(options)); }
            ValidateRules(options.Rules);
            var s = options.Snapshot; var caps = options.Rules.Caps;
            Require(s.RulesId == options.Rules.Id && s.RulesVersion == options.Rules.Version && s.ArithmeticVersion == 1 && s.RemainderVersion == 1, "Unsupported vassal versions.");
            Require(s.ForgeLevel >= 0 && s.BarracksLevel >= 0 && s.EffectiveSlots == Slots(caps, s.BarracksLevel) && s.EffectiveLevelCap == LevelCap(caps, s.ForgeLevel), "Invalid vassal caps.");
            Require(s.ContentHash == options.Rules.ContentHash && s.BasisHash == VassalRunCodec.HashBasis(options.Rules, s.ForgeLevel, s.BarracksLevel), "Vassal hash mismatch.");
            Require(s.Roster is not null && s.Roster.Count <= s.EffectiveSlots, "Invalid roster count.");
            string? previous = null;
            foreach (var entry in s.Roster!)
            {
                Require(entry is not null && entry.Eligible && entry.Level >= 1 && entry.Level <= s.EffectiveLevelCap && entry.Rank >= 0 && entry.Rank <= caps.MaximumRank, "Invalid roster entry.");
                Require(options.Rules.Vassals.Any(v => v.Id == entry!.Id) && (previous is null || string.CompareOrdinal(previous, entry!.Id) < 0), "Unknown, duplicate or unordered roster.");
                previous = entry!.Id;
            }
            Require(s.Effects is not null && s.Effects.SequenceEqual(Calculate(options.Rules, s.Roster!)), "Vassal effect calculation mismatch.");
        }

        public static void ValidateRules(VassalRunRules rules)
        {
            if (rules is null) { throw new ArgumentNullException(nameof(rules)); }
            Require(rules.Id == "vassal-run-v1" && rules.Version == 1, "Unsupported vassal rules.");
            Require(rules.ContentHash is not null && rules.ContentHash.Length == VassalRunCodec.Sha256HexCharacters &&
                rules.ContentHash.All(c => c >= '0' && c <= '9' || c >= 'A' && c <= 'F'), "Invalid vassal source content hash.");
            Require(rules.Vassals is not null && rules.Vassals.Count >= 1 && rules.Vassals.Count <= VassalRunCodec.MaximumDefinitions, "Invalid vassal definitions.");
            var caps = rules.Caps;
            Require(caps is not null && caps.BaseSlots >= 1 && caps.MaximumSlots >= caps.BaseSlots && caps.MaximumSlots <= rules.Vassals!.Count &&
                caps.BarracksLevelsPerSlot > 0 && caps.BaseLevelCap >= 1 && caps.LevelCapPerForgeLevel >= 0 &&
                caps.MaximumLevel >= caps.BaseLevelCap && caps.MaximumRank >= 0, "Invalid vassal cap rules.");
            var abilities = (MetaAbility[])Enum.GetValues(typeof(MetaAbility));
            Require(rules.Effects is not null && rules.Effects.Count == abilities.Length, "Invalid vassal effect count.");
            for (int i = 0; i < abilities.Length; i++)
            {
                var effect = rules.Effects![i];
                Require(effect is not null && effect.Ability == abilities[i] && effect.Base >= 0 && effect.PerLevel >= 0 && effect.PerRank >= 0 && effect.Cap >= 0,
                    "Invalid or unordered vassal effect rules.");
                long largest = effect!.Base + (long)effect.PerLevel * (caps!.MaximumLevel - 1) + (long)effect.PerRank * caps.MaximumRank;
                Require(largest <= int.MaxValue / caps.MaximumSlots, "Vassal raw effect can overflow.");
            }
            string? previous = null;
            foreach (var definition in rules.Vassals!)
            {
                Require(definition is not null && !string.IsNullOrWhiteSpace(definition.Id) && definition.Id.Length <= VassalRunCodec.MaximumIdentifierCharacters && Enum.IsDefined(typeof(MetaAbility), definition.Ability), "Invalid vassal definition.");
                Require(previous is null || string.CompareOrdinal(previous, definition!.Id) < 0, "Duplicate or unordered vassal definition.");
                previous = definition!.Id;
            }
        }

        private static VassalEffect[] Calculate(VassalRunRules rules, IReadOnlyList<VassalRosterEntry> roster) => rules.Effects.Select(rule =>
        {
            long raw = 0;
            foreach (var member in roster.Where(m => rules.Vassals.Any(v => v.Id == m.Id && v.Ability == rule.Ability)))
            { raw = checked(raw + rule.Base + (long)rule.PerLevel * (member.Level - 1) + (long)rule.PerRank * member.Rank); }
            return new VassalEffect(rule.Ability, checked((int)raw), checked((int)Math.Min(rule.Cap, raw)));
        }).ToArray();
        private static int Slots(VassalCapRules caps, int level) => (int)Math.Min(caps.MaximumSlots, (long)caps.BaseSlots + level / caps.BarracksLevelsPerSlot);
        private static int LevelCap(VassalCapRules caps, int level) => (int)Math.Min(caps.MaximumLevel, caps.BaseLevelCap + (long)caps.LevelCapPerForgeLevel * level);
        internal static void Require(bool condition, string message) { if (!condition) { throw new ArgumentException(message); } }
    }
}
