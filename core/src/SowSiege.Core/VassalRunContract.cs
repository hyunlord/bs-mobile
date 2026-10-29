using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    public sealed record VassalEffectRule(MetaAbility Ability, int Base, int PerLevel, int PerRank, int Cap);
    public sealed record VassalDefinition(string Id, MetaAbility Ability);
    public sealed record VassalRunRules(string Id, int Version, IReadOnlyList<VassalDefinition> Vassals, IReadOnlyList<VassalEffectRule> Effects);
    public sealed record VassalRosterEntry(string Id, int Level, int Rank, bool Eligible = true);
    public sealed record VassalEffect(MetaAbility Ability, int Raw, int Bonus);
    public sealed record VassalRunSnapshot(string RulesId, int RulesVersion, string ContentHash, int ArithmeticVersion,
        int RemainderVersion, int EffectiveSlots, int EffectiveLevelCap, int ForgeLevel, int BarracksLevel,
        string BasisHash, IReadOnlyList<VassalRosterEntry> Roster, IReadOnlyList<VassalEffect> Effects);
    public sealed record VassalRunOptions(VassalRunSnapshot Snapshot, VassalRunRules Rules);

    public static class VassalRun
    {
        public static IReadOnlyList<VassalEffectRule> ApprovedEffects { get; } = Array.AsReadOnly(new[]
        {
            new VassalEffectRule(MetaAbility.Attack, 10, 3, 6, 100), new VassalEffectRule(MetaAbility.Health, 20, 5, 10, 150),
            new VassalEffectRule(MetaAbility.Movement, 5, 1, 3, 50), new VassalEffectRule(MetaAbility.Growth, 10, 3, 5, 100),
            new VassalEffectRule(MetaAbility.Allies, 15, 4, 8, 150), new VassalEffectRule(MetaAbility.Experience, 5, 2, 4, 80)
        });

        public static VassalRunOptions Create(MetaCatalog meta, MetaState state, VassalRunRules rules)
        {
            if (meta is null || state is null) { throw new ArgumentNullException(nameof(meta)); }
            ValidateRules(rules);
            var economy = meta.Economy;
            Require(economy.BaseVassalSlots == 1 && economy.MaximumVassalSlots == 3 && economy.BarracksLevelsPerSlot == 4 &&
                economy.BaseVassalLevelCap == 5 && economy.LevelCapPerForgeLevel == 2, "Unsupported vassal cap rules.");
            Require(MetaValidation.ValidateState(meta, state).Length == 0, "Invalid account state.");
            Require(meta.Vassals.Length == rules.Vassals.Count && rules.Vassals.All(d => meta.Vassals.Any(v => v.Id == d.Id && v.Ability == d.Ability)), "Vassal definitions differ from account catalog.");
            int Level(ManorEffect effect) => meta.ManorBuildings.Where(b => b.Effect == effect).Sum(b => state.ManorLevels[b.Id]);
            int forge = Level(ManorEffect.VassalLevelCap), barracks = Level(ManorEffect.VassalSlots);
            int slots = Slots(barracks), cap = LevelCap(forge);
            var roster = state.ActiveVassalIds.OrderBy(id => id, StringComparer.Ordinal)
                .Select(id => new VassalRosterEntry(id, state.Vassals[id].Level, state.Vassals[id].Rank, state.Vassals[id].Unlocked)).ToArray();
            var snapshot = new VassalRunSnapshot(rules.Id, rules.Version, VassalRunCodec.HashRules(rules), 1, 1, slots, cap,
                forge, barracks, VassalRunCodec.HashBasis(forge, barracks), roster, Calculate(rules, roster));
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
            var s = options.Snapshot;
            Require(s.RulesId == options.Rules.Id && s.RulesVersion == 1 && s.ArithmeticVersion == 1 && s.RemainderVersion == 1, "Unsupported vassal versions.");
            Require(s.ForgeLevel >= 0 && s.BarracksLevel >= 0 && s.EffectiveSlots == Slots(s.BarracksLevel) && s.EffectiveLevelCap == LevelCap(s.ForgeLevel), "Invalid vassal caps.");
            Require(s.ContentHash == VassalRunCodec.HashRules(options.Rules) && s.BasisHash == VassalRunCodec.HashBasis(s.ForgeLevel, s.BarracksLevel), "Vassal hash mismatch.");
            Require(s.Roster is not null && s.Roster.Count <= s.EffectiveSlots && s.Roster.Count <= 3, "Invalid roster count.");
            string? previous = null;
            foreach (var entry in s.Roster!)
            {
                Require(entry is not null && entry.Eligible && entry.Level >= 1 && entry.Level <= s.EffectiveLevelCap && entry.Rank >= 0 && entry.Rank <= 5, "Invalid roster entry.");
                Require(options.Rules.Vassals.Any(v => v.Id == entry!.Id) && (previous is null || string.CompareOrdinal(previous, entry!.Id) < 0), "Unknown, duplicate or unordered roster.");
                previous = entry!.Id;
            }
            Require(s.Effects is not null && s.Effects.SequenceEqual(Calculate(options.Rules, s.Roster!)), "Vassal effect calculation mismatch.");
        }

        public static void ValidateRules(VassalRunRules rules)
        {
            if (rules is null) { throw new ArgumentNullException(nameof(rules)); }
            Require(rules.Id == "vassal-run-v1" && rules.Version == 1 && rules.Effects is not null && rules.Effects.SequenceEqual(ApprovedEffects), "Unsupported vassal rules.");
            Require(rules.Vassals is not null && rules.Vassals.Count >= 1 && rules.Vassals.Count <= 64, "Invalid vassal definitions.");
            string? previous = null;
            foreach (var definition in rules.Vassals!)
            {
                Require(definition is not null && !string.IsNullOrWhiteSpace(definition.Id) && definition.Id.Length <= 128 && Enum.IsDefined(typeof(MetaAbility), definition.Ability), "Invalid vassal definition.");
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
        private static int Slots(int level) => (int)Math.Min(3, 1L + level / 4);
        private static int LevelCap(int level) => (int)Math.Min(20, 5L + 2L * level);
        internal static void Require(bool condition, string message) { if (!condition) { throw new ArgumentException(message); } }
    }
}
