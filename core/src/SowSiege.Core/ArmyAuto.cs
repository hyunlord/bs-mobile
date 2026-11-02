using System;
using System.Collections.Generic;
using System.Linq;

namespace SowSiege.Core
{
    /// <summary>Seedless integer simultaneous rounds. No account or direct-survivor state is consulted.</summary>
    public static class ArmyAuto
    {
        public const int MaximumEntries = 4096;
        public const int MaximumTextLength = 128;
        public const long MaximumWork = 200000000;
        private sealed class Slot
        {
            internal readonly ArmyAutoTroop Troop;
            internal readonly int Ordinal;
            internal readonly int Attack;
            internal readonly int Defense;
            internal int Health;
            internal Slot(ArmyAutoTroop troop, int ordinal, int attack, int defense, int health)
            { Troop = troop; Ordinal = ordinal; Attack = attack; Defense = defense; Health = health; }
        }

        public static ArmyAutoResult Resolve(ArmyAutoInput input)
        {
            var value = Normalize(input);
            var rules = value.Rules;
            var inputHash = ArmyAutoEncoding.Hash(ArmyAutoEncoding.Input(value));
            var rulesHash = ArmyAutoEncoding.Hash(ArmyAutoEncoding.Rules(rules));
            var attacker = Slots(value.Attacker, rules, 0);
            var defender = Slots(value.Defender, rules, checked(value.BattlefieldDefense + value.ObjectiveDefense));
            var policy = rules.Policies.Single(p => p.Mode == value.Mode);
            var matchups = rules.Matchups.ToDictionary(m => (m.AttackerId, m.DefenderId), m => m.Multiplier);
            var attackDamage = new long[attacker.Length];
            var defendDamage = new long[defender.Length];
            var round = 0;
            var terminal = Terminal(attacker, defender, false);
            while (terminal is null && round < rules.MaxRounds)
            {
                Array.Clear(attackDamage, 0, attackDamage.Length);
                Array.Clear(defendDamage, 0, defendDamage.Length);
                Attack(attacker, defender, defendDamage, rules.MinDamage, matchups);
                Attack(defender, attacker, attackDamage, rules.MinDamage, matchups);
                Apply(attacker, attackDamage); Apply(defender, defendDamage);
                round++;
                terminal = Terminal(attacker, defender, true);
            }
            terminal ??= WorldTerminalReason.RoundLimit;
            var satisfied = terminal == WorldTerminalReason.AutoVictory &&
                (round == 0 ? policy.UndefendedSatisfiesObjective : policy.VictorySatisfiesObjective);
            var status = satisfied ? "auto-objective-satisfied" : "auto-objective-unsatisfied";
            var result = new ArmyAutoResult(new(value.ContractHash, value.Mode, rulesHash, value.ContentHash,
                terminal.Value, round, Array.AsReadOnly(Array.Empty<WorldObjectiveResult>()), Losses(value.Attacker, attacker), string.Empty),
                inputHash, status, Losses(value.Defender, defender), Results(attacker), Results(defender));
            return result with { Combat = result.Combat with { StateHash = ArmyAutoEncoding.Hash(ArmyAutoEncoding.Result(result)) } };
        }

        public static byte[] EncodeResult(ArmyAutoResult result)
        {
            if (result is null) { throw new ArgumentNullException(nameof(result)); }
            Require(result.Combat is not null && result.Combat.Objectives is not null && result.Combat.Objectives.Count == 0,
                "Automatic results cannot claim direct-combat objective progress.");
            return ArmyAutoEncoding.Result(result);
        }

        private static WorldTerminalReason? Terminal(Slot[] attacker, Slot[] defender, bool fought)
        {
            var a = Alive(attacker); var d = Alive(defender);
            if (!a) { return !d && fought ? WorldTerminalReason.SimultaneousElimination : WorldTerminalReason.AutoDefeat; }
            return !d ? WorldTerminalReason.AutoVictory : null;
        }
        private static bool Alive(Slot[] slots)
        { foreach (var slot in slots) { if (slot.Health > 0) { return true; } } return false; }
        private static void Attack(Slot[] source, Slot[] targets, long[] damage, int minimum,
            Dictionary<(string, string), int> matchups)
        {
            var target = 0;
            while (target < targets.Length && targets[target].Health == 0) { target++; }
            if (target == targets.Length) { return; }
            foreach (var slot in source)
            {
                if (slot.Health == 0) { continue; }
                var amount = Math.Max(minimum, checked((long)slot.Attack * matchups[(slot.Troop.Id, targets[target].Troop.Id)]) / PlayerInput.Scale - targets[target].Defense);
                damage[target] = checked(damage[target] + amount);
            }
        }
        private static void Apply(Slot[] slots, long[] damage)
        { for (var i = 0; i < slots.Length; i++) { slots[i].Health = (int)Math.Max(0, slots[i].Health - damage[i]); } }
        private static IReadOnlyList<ArmyAutoSlotResult> Results(Slot[] slots) =>
            Array.AsReadOnly(slots.Select(s => new ArmyAutoSlotResult(s.Troop.Id, s.Ordinal, s.Health)).ToArray());
        private static IReadOnlyList<WorldTroopResult> Losses(ArmyAutoSide side, Slot[] slots) =>
            Array.AsReadOnly(side.Troops.Select(t => new WorldTroopResult(t.Id, t.Ordinals.Count,
                slots.Count(s => s.Troop.Id == t.Id && s.Health == 0))).ToArray());
        private static Slot[] Slots(ArmyAutoSide side, ArmyAutoRules rules, int extraDefense)
        {
            var attack = Bonus(side, e => e.Attack, rules.AttackBonusCap);
            var defense = Bonus(side, e => e.Defense, rules.DefenseBonusCap);
            var health = Bonus(side, e => e.Health, rules.HealthBonusCap);
            var slots = new List<Slot>();
            foreach (var troop in side.Troops)
            {
                foreach (var ordinal in troop.Ordinals)
                { slots.Add(new(troop, ordinal, checked(troop.Attack + attack), checked(troop.Defense + defense + extraDefense), checked(troop.Health + health))); }
            }
            slots.Sort((a, b) =>
            {
                var c = a.Troop.TargetPriority.CompareTo(b.Troop.TargetPriority);
                if (c == 0) { c = StringComparer.Ordinal.Compare(a.Troop.Id, b.Troop.Id); }
                return c == 0 ? a.Ordinal.CompareTo(b.Ordinal) : c;
            });
            return slots.ToArray();
        }
        private static int Bonus(ArmyAutoSide side, Func<ArmyAutoEffect, int> select, int cap)
        { long sum = 0; foreach (var e in side.Effects) { sum = checked(sum + select(e)); } return (int)Math.Min(cap, sum); }

        internal static ArmyAutoInput Normalize(ArmyAutoInput input)
        {
            Require(input is not null, "Missing army input.");
            Hash(input!.ContractHash); Hash(input.ContentHash);
            Require(input.Mode is WorldCombatMode.AutoPve or WorldCombatMode.CityPvp, "Invalid auto mode.");
            var r = input.Rules;
            Require(r is not null && r.Version == "army-auto-v1", "Invalid auto rules version.");
            Require(r!.MaxRounds > 0 && r.MaxSlotsPerSide > 0 && r.MaxSlotsPerSide <= MaximumEntries && r.MaxStat > 0,
                "Invalid rule limits.");
            Require(r.MinDamage >= 0 && r.AttackBonusCap >= 0 && r.DefenseBonusCap >= 0 && r.HealthBonusCap >= 0,
                "Negative rule coefficient.");
            Require(input.BattlefieldDefense >= 0 && input.ObjectiveDefense >= 0, "Negative battlefield defense.");
            _ = checked(r.MaxStat + r.HealthBonusCap);
            var maximumAttack = checked(r.MaxStat + r.AttackBonusCap);
            _ = checked(r.MaxStat + r.DefenseBonusCap + input.BattlefieldDefense + input.ObjectiveDefense);
            Count(r.Policies); Require(r.Policies.Count == Enum.GetValues(typeof(WorldCombatMode)).Length - 1, "Both auto mode policies are required.");
            var policies = new Dictionary<WorldCombatMode, ArmyAutoPolicy>();
            foreach (var p in r.Policies)
            {
                Require(p is not null && p.Mode is WorldCombatMode.AutoPve or WorldCombatMode.CityPvp, "Invalid policy.");
                Require(!policies.ContainsKey(p!.Mode), "Duplicate policy."); policies.Add(p.Mode, p);
            }
            Count(r.Matchups);
            var keys = new HashSet<(string, string)>();
            foreach (var m in r.Matchups)
            {
                Require(m is not null, "Missing matchup."); Id(m!.AttackerId); Id(m.DefenderId);
                Require(m.Multiplier >= 0 && keys.Add((m.AttackerId, m.DefenderId)), "Invalid or duplicate matchup.");
                _ = checked(Math.Max(r.MinDamage, checked((long)maximumAttack * m.Multiplier) / PlayerInput.Scale) * r.MaxSlotsPerSide);
            }
            var a = Side(input.Attacker, r); var d = Side(input.Defender, r);
            var total = checked(a.Troops.Count + d.Troops.Count + a.Troops.Sum(t => t.Ordinals.Count) + d.Troops.Sum(t => t.Ordinals.Count));
            Require(checked((long)r.MaxRounds * Math.Max(1, total) * Math.Max(1, total)) <= MaximumWork, "Automatic combat work budget exceeded.");
            foreach (var attack in a.Troops)
            {
                foreach (var defend in d.Troops)
                { Require(keys.Contains((attack.Id, defend.Id)) && keys.Contains((defend.Id, attack.Id)), "Missing directional matchup."); }
            }
            return input with
            {
                Attacker = a,
                Defender = d,
                Rules = r with
                { Policies = policies.Values.OrderBy(p => p.Mode).ToArray(), Matchups = r.Matchups.OrderBy(m => m.AttackerId, StringComparer.Ordinal).ThenBy(m => m.DefenderId, StringComparer.Ordinal).ToArray() }
            };
        }
        private static ArmyAutoSide Side(ArmyAutoSide side, ArmyAutoRules rules)
        {
            Require(side is not null, "Missing army side."); Count(side!.Effects); Count(side.Troops);
            var effects = new HashSet<(WorldEffectSourceKind, string)>();
            foreach (var e in side.Effects)
            {
                Require(e is not null, "Missing effect."); Id(e!.Id); Hash(e.ContentHash);
                Require(e.Kind is WorldEffectSourceKind.Hero or WorldEffectSourceKind.Vassal or WorldEffectSourceKind.Research,
                    "Invalid effect kind.");
                Require(e.Attack >= 0 && e.Defense >= 0 && e.Health >= 0 && effects.Add((e.Kind, e.Id)), "Invalid or duplicate effect.");
                Require(side.HeroAvailable || e.Kind != WorldEffectSourceKind.Hero || (e.Attack == 0 && e.Defense == 0 && e.Health == 0), "Unavailable hero contributes effects.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal); var troops = new List<ArmyAutoTroop>(); var count = 0;
            foreach (var t in side.Troops)
            {
                Require(t is not null, "Missing troop."); Id(t!.Id); Count(t.Ordinals);
                Require(ids.Add(t.Id) && t.Health > 0 && t.Health <= rules.MaxStat && t.Attack >= 0 && t.Attack <= rules.MaxStat &&
                    t.Defense >= 0 && t.Defense <= rules.MaxStat && t.TargetPriority >= 0, "Invalid or duplicate troop.");
                var ordinals = t.Ordinals.ToArray(); Array.Sort(ordinals);
                for (var i = 0; i < ordinals.Length; i++) { Require(ordinals[i] >= 0 && (i == 0 || ordinals[i] != ordinals[i - 1]), "Invalid or duplicate ordinal."); }
                count = checked(count + ordinals.Length); Require(count <= rules.MaxSlotsPerSide, "Too many army slots.");
                troops.Add(t with { Ordinals = ordinals });
            }
            return side with
            {
                Effects = side.Effects.OrderBy(e => e.Kind).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray(),
                Troops = troops.OrderBy(t => t.Id, StringComparer.Ordinal).ToArray()
            };
        }
        private static void Count<T>(IReadOnlyList<T> items) => Require(items is not null && items.Count <= MaximumEntries, "Missing or oversized army collection.");
        private static void Id(string value)
        {
            Require(value is not null && value.Length > 0 && value.Length <= MaximumTextLength, "Invalid army identifier.");
            var colon = value!.IndexOf(':'); Require(colon > 0 && colon < value.Length - 1, "Army identifier requires namespace.");
            for (var i = 0; i < value.Length; i++)
            { var c = value[i]; Require(i == colon || (c >= 'a' && c <= 'z') || (i != 0 && i != colon + 1 && ((c >= '0' && c <= '9') || c == '_')), "Invalid army identifier character."); }
        }
        private static void Hash(string value)
        {
            Require(value is not null && value.Length == WorldCombatCodec.Sha256HexCharacters, "Invalid army hash.");
            foreach (var c in value!) { Require((c >= 'A' && c <= 'F') || (c >= '0' && c <= '9'), "Noncanonical army hash."); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) { throw new ArgumentException(message); } }
    }
}
