using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Core
{
    public static class VassalRunCodec
    {
        public const int MaximumBytes = 65536;
        private static readonly UTF8Encoding Utf8 = new(false, true);

        public static byte[] Encode(VassalRunOptions options)
        {
            VassalRun.Validate(options);
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
            Text(w, "vassal-run-v1"); w.Write(1); w.Write(1u);
            WriteRules(w, options.Rules);
            var s = options.Snapshot;
            Text(w, s.ContentHash); w.Write(s.ArithmeticVersion); w.Write(s.RemainderVersion);
            w.Write(s.EffectiveSlots); w.Write(s.EffectiveLevelCap); w.Write(s.ForgeLevel); w.Write(s.BarracksLevel); Text(w, s.BasisHash);
            w.Write(s.Roster.Count);
            foreach (var entry in s.Roster) { Text(w, entry.Id); w.Write(entry.Level); w.Write(entry.Rank); w.Write(entry.Eligible); }
            w.Write(s.Effects.Count);
            foreach (var effect in s.Effects) { w.Write((int)effect.Ability); w.Write(effect.Raw); w.Write(effect.Bonus); }
            w.Flush(); var bytes = stream.ToArray();
            VassalRun.Require(bytes.Length <= MaximumBytes, "Vassal payload too large."); return bytes;
        }

        public static VassalRunOptions Decode(byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumBytes) { throw new InvalidDataException("Invalid vassal payload length."); }
            try
            {
                using var stream = new MemoryStream(bytes, false); using var r = new BinaryReader(stream, Utf8, true);
                if (Text(r) != "vassal-run-v1" || r.ReadInt32() != 1 || r.ReadUInt32() != 1u) { throw new InvalidDataException("Unsupported vassal payload schema or flags."); }
                var rules = ReadRules(r);
                string content = Text(r); int arithmetic = r.ReadInt32(), remainder = r.ReadInt32();
                int slots = r.ReadInt32(), cap = r.ReadInt32(), forge = r.ReadInt32(), barracks = r.ReadInt32(); string basis = Text(r);
                var roster = new VassalRosterEntry[Count(r, 3)];
                for (int i = 0; i < roster.Length; i++)
                {
                    string id = Text(r); int level = r.ReadInt32(), rank = r.ReadInt32();
                    if (r.ReadByte() != 1) { throw new InvalidDataException("Vassal must be eligible."); }
                    roster[i] = new(id, level, rank);
                }
                var effects = new VassalEffect[Count(r, 6)];
                for (int i = 0; i < effects.Length; i++) { effects[i] = new((MetaAbility)r.ReadInt32(), r.ReadInt32(), r.ReadInt32()); }
                if (stream.Position != stream.Length) { throw new InvalidDataException("Trailing vassal bytes."); }
                return VassalRun.Freeze(new(new(rules.Id, rules.Version, content, arithmetic, remainder, slots, cap, forge, barracks, basis, roster, effects), rules));
            }
            catch (ArgumentException exception) { throw new InvalidDataException("Invalid vassal payload.", exception); }
            catch (EndOfStreamException exception) { throw new InvalidDataException("Truncated vassal payload.", exception); }
        }

        public static string Hash(VassalRunOptions options) => Digest(Encode(options));
        public static string HashRules(VassalRunRules rules)
        {
            VassalRun.ValidateRules(rules);
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
            Text(w, "vassal-content-v1"); WriteRules(w, rules); w.Flush(); return Digest(stream.ToArray());
        }
        internal static string HashBasis(int forge, int barracks)
        {
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
            Text(w, "vassal-cap-basis-v1"); w.Write(1); w.Write(3); w.Write(4); w.Write(5); w.Write(2); w.Write(20); w.Write(5);
            w.Write(forge); w.Write(barracks); w.Flush(); return Digest(stream.ToArray());
        }
        internal static string Digest(byte[] bytes)
        {
            using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        private static void WriteRules(BinaryWriter w, VassalRunRules rules)
        {
            Text(w, rules.Id); w.Write(rules.Version); w.Write(rules.Vassals.Count);
            foreach (var definition in rules.Vassals) { Text(w, definition.Id); w.Write((int)definition.Ability); }
            w.Write(rules.Effects.Count);
            foreach (var rule in rules.Effects) { w.Write((int)rule.Ability); w.Write(rule.Base); w.Write(rule.PerLevel); w.Write(rule.PerRank); w.Write(rule.Cap); }
        }
        private static VassalRunRules ReadRules(BinaryReader r)
        {
            string id = Text(r); int version = r.ReadInt32(); var definitions = new VassalDefinition[Count(r, 64)];
            for (int i = 0; i < definitions.Length; i++) { definitions[i] = new(Text(r), (MetaAbility)r.ReadInt32()); }
            var effects = new VassalEffectRule[Count(r, 6)];
            for (int i = 0; i < effects.Length; i++) { effects[i] = new((MetaAbility)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()); }
            return new(id, version, definitions, effects);
        }
        private static int Count(BinaryReader r, int maximum)
        {
            int count = r.ReadInt32(); if (count < 0 || count > maximum) { throw new InvalidDataException("Invalid vassal array length."); }
            return count;
        }
        private static void Text(BinaryWriter w, string value)
        {
            var bytes = Utf8.GetBytes(value); if (bytes.Length > 512) { throw new ArgumentException("Vassal string too long."); }
            w.Write(bytes.Length); w.Write(bytes);
        }
        private static string Text(BinaryReader r)
        {
            int length = Count(r, 512); var bytes = r.ReadBytes(length);
            if (bytes.Length != length) { throw new EndOfStreamException(); }
            return Utf8.GetString(bytes);
        }
    }
}
