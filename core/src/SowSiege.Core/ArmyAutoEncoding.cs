using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Core
{
    internal static class ArmyAutoEncoding
    {
        internal static byte[] Input(ArmyAutoInput input) => Encode(w =>
        {
            w.Write("army-auto-input-v1"); w.Write(input.ContractHash); w.Write((int)input.Mode); w.Write(input.ContentHash);
            WriteRules(w, input.Rules); Side(w, input.Attacker); Side(w, input.Defender);
            w.Write(input.BattlefieldDefense); w.Write(input.ObjectiveDefense); w.Write("none");
        });
        internal static byte[] Rules(ArmyAutoRules rules) => Encode(w => WriteRules(w, rules));
        internal static byte[] Result(ArmyAutoResult result) => Encode(w =>
        {
            var c = result.Combat;
            w.Write("army-auto-result-v1"); w.Write(c.ContractHash); w.Write((int)c.Mode); w.Write(c.RulesHash);
            w.Write(c.ContentHash); w.Write(result.InputHash); w.Write((int)c.Terminal); w.Write(c.TickOrRound);
            w.Write(result.ObjectiveStatus); Troops(w, c.Troops); Troops(w, result.DefenderTroops);
            Slots(w, result.AttackerSlots); Slots(w, result.DefenderSlots);
            // StateHash is the digest of these bytes and is intentionally not self-encoded.
        });
        private static void WriteRules(BinaryWriter w, ArmyAutoRules r)
        {
            w.Write(r.Version); w.Write(r.MaxRounds); w.Write(r.MaxSlotsPerSide); w.Write(r.MaxStat);
            w.Write(r.MinDamage); w.Write(r.AttackBonusCap); w.Write(r.DefenseBonusCap); w.Write(r.HealthBonusCap);
            w.Write(r.Policies.Count); foreach (var p in r.Policies)
            { w.Write((int)p.Mode); w.Write(p.VictorySatisfiesObjective); w.Write(p.UndefendedSatisfiesObjective); }
            w.Write(r.Matchups.Count); foreach (var m in r.Matchups)
            { w.Write(m.AttackerId); w.Write(m.DefenderId); w.Write(m.Multiplier); }
        }
        private static void Side(BinaryWriter w, ArmyAutoSide side)
        {
            w.Write(side.HeroAvailable); w.Write(side.Effects.Count);
            foreach (var e in side.Effects)
            { w.Write((int)e.Kind); w.Write(e.Id); w.Write(e.ContentHash); w.Write(e.Attack); w.Write(e.Defense); w.Write(e.Health); }
            w.Write(side.Troops.Count); foreach (var t in side.Troops)
            {
                w.Write(t.Id); w.Write(t.Health); w.Write(t.Attack); w.Write(t.Defense); w.Write(t.TargetPriority);
                w.Write(t.Ordinals.Count); foreach (var ordinal in t.Ordinals) { w.Write(ordinal); }
            }
        }
        private static void Troops(BinaryWriter w, IReadOnlyList<WorldTroopResult> troops)
        { w.Write(troops.Count); foreach (var t in troops) { w.Write(t.Id); w.Write(t.N); w.Write(t.K); } }
        private static void Slots(BinaryWriter w, IReadOnlyList<ArmyAutoSlotResult> slots)
        { w.Write(slots.Count); foreach (var s in slots) { w.Write(s.TroopId); w.Write(s.Ordinal); w.Write(s.Health); } }
        internal static string Hash(byte[] bytes)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty); }
        private static byte[] Encode(Action<BinaryWriter> write)
        { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
            write(writer); writer.Flush(); return stream.ToArray(); }
    }
}
