using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Core
{
    public static partial class WorldCombatCodec
    {
        public const int MaximumBytes = 1048576;
        public const int MaximumEntries = 4096;
        public const int MaximumIdentifierCharacters = 128;
        public const int Sha256HexCharacters = 64;
        internal const int MaximumTextBytes = 512;
        internal const int DigestBytes = 32;
        private static readonly UTF8Encoding Utf8 = new(false, true);

        public static byte[] Encode(WorldCombatOptions options)
        {
            Validate(options);
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
            Text(w, "world-combat-v1"); w.Write(1); w.Write(1u); WriteSnapshot(w, options.Snapshot); w.Flush();
            Require(stream.Length <= MaximumBytes - DigestBytes, "World combat block too large.");
            using var sha = SHA256.Create(); var checksum = sha.ComputeHash(stream.ToArray());
            w.Write(checksum); w.Flush(); return stream.ToArray();
        }

        public static WorldCombatOptions Decode(byte[] bytes)
        {
            if (bytes is null || bytes.Length <= DigestBytes || bytes.Length > MaximumBytes) { throw new InvalidDataException("Invalid world block length."); }
            try
            {
                int bodyLength = bytes.Length - DigestBytes;
                using var sha = SHA256.Create(); var checksum = sha.ComputeHash(bytes, 0, bodyLength);
                for (int i = 0; i < DigestBytes; i++)
                { if (checksum[i] != bytes[bodyLength + i]) { throw new InvalidDataException("World block checksum mismatch."); } }
                using var stream = new MemoryStream(bytes, 0, bodyLength, false);
                using var r = new BinaryReader(stream, Utf8, true);
                if (Text(r) != "world-combat-v1" || r.ReadInt32() != 1 || r.ReadUInt32() != 1u) { throw new InvalidDataException("Unsupported world block schema or flags."); }
                var options = new WorldCombatOptions(ReadSnapshot(r));
                if (stream.Position != stream.Length) { throw new InvalidDataException("Trailing world block bytes."); }
                Validate(options); return options;
            }
            catch (ArgumentException exception) { throw new InvalidDataException("Invalid world combat payload.", exception); }
            catch (EndOfStreamException exception) { throw new InvalidDataException("Truncated world combat payload.", exception); }
            catch (OverflowException exception) { throw new InvalidDataException("Overflow in world combat payload.", exception); }
        }

        public static WorldCombatOptions Freeze(WorldCombatOptions options) => Decode(Encode(options));
        public static string Hash(WorldCombatOptions options)
        {
            using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(Encode(options))).Replace("-", "");
        }
        private static void Text(BinaryWriter w, string value)
        {
            var bytes = Utf8.GetBytes(value); Require(bytes.Length <= MaximumTextBytes, "World text too long.");
            w.Write(bytes.Length); w.Write(bytes);
        }
        private static string Text(BinaryReader r)
        {
            int length = r.ReadInt32();
            if (length < 0 || length > MaximumTextBytes || length > r.BaseStream.Length - r.BaseStream.Position) { throw new InvalidDataException("Invalid world text length."); }
            return Utf8.GetString(r.ReadBytes(length));
        }
        private static bool Boolean(BinaryReader r)
        {
            byte value = r.ReadByte(); if (value > 1) { throw new InvalidDataException("Noncanonical world boolean."); }
            return value == 1;
        }
        private static void List<T>(BinaryWriter w, IReadOnlyList<T> values, Action<BinaryWriter, T> write)
        { w.Write(values.Count); foreach (var value in values) { write(w, value); } }
        private static IReadOnlyList<T> List<T>(BinaryReader r, Func<BinaryReader, T> read)
        {
            int count = r.ReadInt32();
            if (count < 0 || count > MaximumEntries || count > r.BaseStream.Length - r.BaseStream.Position) { throw new InvalidDataException("Invalid world collection length."); }
            var values = new T[count]; for (int i = 0; i < count; i++) { values[i] = read(r); }
            return Array.AsReadOnly(values);
        }
        private static void WriteSnapshot(BinaryWriter w, WorldCombatSnapshot s)
        {
            Text(w, s.ContractHash); w.Write((int)s.Mode); WriteProvenance(w, s.Provenance); WriteBattlefield(w, s.Battlefield);
            WriteHero(w, s.Hero); WriteInput(w, s.Input);
            List(w, s.Objectives, WriteObjective); List(w, s.Troops, WriteTroop); List(w, s.Cohorts, WriteCohort);
            List(w, s.Markers, WriteMarker); List(w, s.Effects, WriteEffect);
        }
        private static WorldCombatSnapshot ReadSnapshot(BinaryReader r) => new(Text(r), (WorldCombatMode)r.ReadInt32(), ReadProvenance(r), ReadBattlefield(r),
            ReadHero(r), ReadInput(r), List(r, ReadObjective), List(r, ReadTroop), List(r, ReadCohort), List(r, ReadMarker), List(r, ReadEffect));
        private static void WriteProvenance(BinaryWriter w, WorldCombatProvenance p)
        {
            Text(w, p.ProfileId); Text(w, p.ProfileHash); Text(w, p.RulesId); w.Write(p.RulesVersion); Text(w, p.RulesHash); Text(w, p.ContentHash);
            w.Write(p.ArithmeticVersion); w.Write(p.RemainderVersion); w.Write(p.OrderVersion); w.Write(p.TickRate); w.Write(p.Seed); Text(w, p.RngAlgorithm);
        }
        private static WorldCombatProvenance ReadProvenance(BinaryReader r) => new(Text(r), Text(r), Text(r), r.ReadInt32(), Text(r), Text(r),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), Text(r));
        private static void WriteBattlefield(BinaryWriter w, WorldBattlefield b) { w.Write(b.MinX); w.Write(b.MinY); w.Write(b.MaxX); w.Write(b.MaxY); }
        private static WorldBattlefield ReadBattlefield(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        private static void WriteStats(BinaryWriter w, WorldCombatStats s) { w.Write(s.Health); w.Write(s.Attack); w.Write(s.Defense); w.Write(s.Speed); w.Write(s.Range); w.Write(s.CooldownTicks); }
        private static WorldCombatStats ReadStats(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        private static void WriteTroop(BinaryWriter w, WorldTroopType t) { Text(w, t.Id); w.Write(t.N); WriteStats(w, t.Stats); List(w, t.Slots, WriteSlot); }
        private static WorldTroopType ReadTroop(BinaryReader r) => new(Text(r), r.ReadInt32(), ReadStats(r), List(r, ReadSlot));
        private static void WriteSlot(BinaryWriter w, WorldTroopSlot s) { w.Write(s.Ordinal); w.Write(s.X); w.Write(s.Y); }
        private static WorldTroopSlot ReadSlot(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        private static void WriteCohort(BinaryWriter w, WorldSpawnCohort c) { Text(w, c.Id); Text(w, c.EnemyId); w.Write(c.SpawnTick); w.Write(c.Count); w.Write(c.X); w.Write(c.Y); WriteStats(w, c.Stats); }
        private static WorldSpawnCohort ReadCohort(BinaryReader r) => new(Text(r), Text(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), ReadStats(r));
        private static void WriteMarker(BinaryWriter w, WorldMarker m) { Text(w, m.Id); w.Write(m.X); w.Write(m.Y); w.Write(m.Radius); w.Write(m.Health); w.Write(m.Required); }
        private static WorldMarker ReadMarker(BinaryReader r) => new(Text(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), Boolean(r));
        private static void WriteObjective(BinaryWriter w, WorldObjective o) { Text(w, o.Id); w.Write((int)o.Kind); Text(w, o.TargetId); w.Write(o.RequiredCount); w.Write(o.HoldTicks); w.Write(o.LimitTicks); }
        private static WorldObjective ReadObjective(BinaryReader r) => new(Text(r), (WorldObjectiveKind)r.ReadInt32(), Text(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        private static void WriteHero(BinaryWriter w, WorldHeroSnapshot h) { Text(w, h.Id); Text(w, h.ContentHash); List(w, h.Skills, WriteSkill); }
        private static WorldHeroSnapshot ReadHero(BinaryReader r) => new(Text(r), Text(r), List(r, ReadSkill));
        private static void WriteSkill(BinaryWriter w, WorldHeroSkill s) { Text(w, s.Id); w.Write(s.Version); Text(w, s.ContentHash); }
        private static WorldHeroSkill ReadSkill(BinaryReader r) => new(Text(r), r.ReadInt32(), Text(r));
        private static void WriteInput(BinaryWriter w, WorldInputPolicy p) { w.Write(p.InputGraceMilliseconds); w.Write(p.MaximumChunkCommands); w.Write(p.MaximumChunkBytes); }
        private static WorldInputPolicy ReadInput(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        private static void WriteEffect(BinaryWriter w, WorldResolvedEffect e)
        { w.Write((int)e.Ability); w.Write(e.Raw); w.Write(e.Cap); w.Write(e.Bonus); w.Write(e.HeroEligible); w.Write(e.StandingEligible); w.Write(e.TemporaryEligible); List(w, e.Sources, WriteSource); }
        private static WorldResolvedEffect ReadEffect(BinaryReader r) => new((MetaAbility)r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), Boolean(r), Boolean(r), Boolean(r), List(r, ReadSource));
        private static void WriteSource(BinaryWriter w, WorldRawEffect s) { w.Write((int)s.Kind); Text(w, s.SourceId); Text(w, s.ContentHash); w.Write(s.Raw); }
        private static WorldRawEffect ReadSource(BinaryReader r) => new((WorldEffectSourceKind)r.ReadInt32(), Text(r), Text(r), r.ReadInt32());
    }
}
