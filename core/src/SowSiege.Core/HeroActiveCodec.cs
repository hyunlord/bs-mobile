using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SowSiege.Core
{
    public static class HeroActiveCodec
    {
        public const int MaximumCoordinate = 1000000;
        internal const int EnvelopeSchema = 2;
        private static readonly UTF8Encoding Utf8 = new(false, true);
        public static HeroActiveOptions Freeze(HeroActiveOptions value) => Decode(Encode(value));
        public static byte[] Encode(HeroActiveOptions value)
        {
            Validate(value);
            using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Utf8, true);
            Text(w, "hero-active-v1"); w.Write(1); w.Write(1u);
            Text(w, value.RulesHash); Text(w, value.ContentHash); Text(w, value.OwnerId); Text(w, value.HeroId);
            Text(w, value.StartingSlotId); Text(w, value.AttackDefinitionId); w.Write(value.HeroRadius);
            w.Write(value.Skills.Count);
            foreach (var s in value.Skills) { Text(w, s.Id); w.Write(s.Version); Text(w, s.ContentHash); w.Write((int)s.Kind); }
            w.Write(value.Definitions.Count);
            foreach (var d in value.Definitions)
            {
                Text(w, d.Id); w.Write(d.Version); Text(w, d.ContentHash); w.Write((int)d.Kind);
                var p = d.Dash; w.Write(p.PreparationTicks); w.Write(p.ThreatRange); w.Write(p.SpeedPerTick); w.Write(p.MaximumDistance);
                w.Write(p.DurationTicks); w.Write(p.CooldownTicks); w.Write(p.MaximumInstances);
            }
            w.Write(value.Walls.Count);
            foreach (var wall in value.Walls) { Text(w, wall.Id); w.Write(wall.MinX); w.Write(wall.MinY); w.Write(wall.MaxX); w.Write(wall.MaxY); }
            w.Write(value.EnemyBodies.Count);
            foreach (var body in value.EnemyBodies) { Text(w, body.EnemyId); w.Write(body.Radius); w.Write(body.EligibleTarget); }
            w.Flush(); Require(stream.Length <= WorldCombatCodec.MaximumBytes - WorldCombatCodec.DigestBytes, "Active block exceeds its protocol limit.");
            using var sha = SHA256.Create(); w.Write(sha.ComputeHash(stream.ToArray())); w.Flush(); return stream.ToArray();
        }
        public static HeroActiveOptions Decode(byte[] bytes)
        {
            if (bytes is null || bytes.Length <= WorldCombatCodec.DigestBytes || bytes.Length > WorldCombatCodec.MaximumBytes) { throw new InvalidDataException("Invalid active block size."); }
            try
            {
                using var sha = SHA256.Create(); var hash = sha.ComputeHash(bytes, 0, bytes.Length - WorldCombatCodec.DigestBytes);
                for (var i = 0; i < hash.Length; i++) { if (hash[i] != bytes[bytes.Length - WorldCombatCodec.DigestBytes + i]) { throw new InvalidDataException("Active checksum mismatch."); } }
                using var stream = new MemoryStream(bytes, 0, bytes.Length - WorldCombatCodec.DigestBytes, false); using var r = new BinaryReader(stream, Utf8, true);
                if (Text(r) != "hero-active-v1" || r.ReadInt32() != 1 || r.ReadUInt32() != 1) { throw new InvalidDataException("Unsupported active schema."); }
                var rules = Text(r); var content = Text(r); var owner = Text(r); var hero = Text(r); var slot = Text(r); var attack = Text(r); var radius = r.ReadInt32();
                var skills = new HeroSkillBinding[Count(r)];
                for (var i = 0; i < skills.Length; i++) { skills[i] = new(Text(r), r.ReadInt32(), Text(r), (HeroSkillKind)r.ReadInt32()); }
                var definitions = new HeroActiveDefinition[Count(r)];
                for (var i = 0; i < definitions.Length; i++)
                { definitions[i] = new(Text(r), r.ReadInt32(), Text(r), (HeroActiveKind)r.ReadInt32(), new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32())); }
                var walls = new HeroActiveWall[Count(r)];
                for (var i = 0; i < walls.Length; i++) { walls[i] = new(Text(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32()); }
                var bodies = new HeroActiveEnemyBody[Count(r)];
                for (var i = 0; i < bodies.Length; i++)
                {
                    var id = Text(r); var bodyRadius = r.ReadInt32(); var flag = r.ReadByte();
                    if (flag > 1) { throw new InvalidDataException("Invalid active target flag."); }
                    bodies[i] = new(id, bodyRadius, flag == 1);
                }
                if (stream.Position != stream.Length) { throw new InvalidDataException("Trailing active bytes."); }
                var value = new HeroActiveOptions(rules, content, owner, hero, slot, attack, radius, Array.AsReadOnly(skills), Array.AsReadOnly(definitions), Array.AsReadOnly(walls), Array.AsReadOnly(bodies));
                Validate(value); return value;
            }
            catch (Exception e) when (e is ArgumentException or EndOfStreamException or OverflowException)
            { throw new InvalidDataException("Invalid active payload.", e); }
        }
        public static void Validate(HeroActiveOptions value)
        {
            if (value is null) { throw new ArgumentNullException(nameof(value)); }
            Hash(value.RulesHash); Hash(value.ContentHash); Id(value.OwnerId); Id(value.HeroId); Id(value.StartingSlotId); Id(value.AttackDefinitionId);
            Require(value.OwnerId == value.HeroId && value.HeroRadius >= 0 && value.HeroRadius <= MaximumCoordinate, "Invalid active owner or radius.");
            Ordered(value.Skills, s => s.Id); Ordered(value.Definitions, d => d.Id); Ordered(value.Walls, w => w.Id); Ordered(value.EnemyBodies, b => b.EnemyId);
            Require(value.Definitions.Count == 1, "Exactly one supported dash definition is required.");
            foreach (var s in value.Skills) { Require(s.Version > 0 && Enum.IsDefined(typeof(HeroSkillKind), s.Kind), "Invalid skill binding."); Hash(s.ContentHash); }
            var activeSkills = value.Skills.Where(s => s.Kind == HeroSkillKind.Active).ToArray();
            Require(activeSkills.Length == value.Definitions.Count, "Missing or extra active skill.");
            for (var i = 0; i < value.Definitions.Count; i++)
            {
                var d = value.Definitions[i]; Hash(d.ContentHash);
                Require(d.Version == 1 && d.Kind == HeroActiveKind.Dash && d.Dash is not null, "Unsupported active ability.");
                Require(activeSkills[i].Id == d.Id && activeSkills[i].Version == d.Version && activeSkills[i].ContentHash == d.ContentHash, "Active definition does not match skill binding.");
                var p = d.Dash ?? throw new ArgumentException("Missing dash parameters.");
                Require(p.PreparationTicks > 0 && p.ThreatRange > 0 && p.ThreatRange <= MaximumCoordinate && p.SpeedPerTick > 0 && p.SpeedPerTick <= MaximumCoordinate &&
                    p.MaximumDistance > 0 && p.MaximumDistance <= MaximumCoordinate && p.DurationTicks > 0 && p.CooldownTicks > 0 && p.MaximumInstances == 1, "Invalid dash parameters or unsupported concurrent dash count.");
            }
            foreach (var w in value.Walls)
            { Require(w.MinX >= 0 && w.MinY >= 0 && w.MaxX <= MaximumCoordinate && w.MaxY <= MaximumCoordinate && w.MinX < w.MaxX && w.MinY < w.MaxY, "Invalid wall bounds."); }
            foreach (var b in value.EnemyBodies) { Require(b.Radius >= 0 && b.Radius <= MaximumCoordinate, "Invalid enemy body radius."); }
        }
        internal static void ValidateBinding(InteractiveOptions options)
        {
            if (options.HeroActive is not { } active) { return; }
            Validate(active);
            WorldCombatCodec.Validate(options.WorldCombat!);
            var world = options.WorldCombat?.Snapshot ?? throw new ArgumentException("Active skills require world combat.");
            Require(world.Mode == WorldCombatMode.DirectPve && active.HeroId == world.Hero.Id && active.HeroId == options.Run.HeroId &&
                active.ContentHash == options.DataHash && active.RulesHash == world.Provenance.RulesHash, "Active identity mismatch.");
            Require(world.Hero.Skills.Count == active.Skills.Count, "Skill binding dimensions mismatch.");
            for (var i = 0; i < active.Skills.Count; i++)
            { var a = active.Skills[i]; var b = world.Hero.Skills[i]; Require(a.Id == b.Id && a.Version == b.Version && a.ContentHash == b.ContentHash, "Skill identity mismatch."); }
            foreach (var effect in world.Effects)
            {
                foreach (var source in effect.Sources)
                {
                    Require(source.Kind != WorldEffectSourceKind.Hero || !active.Skills.Any(skill => skill.Kind == HeroSkillKind.Active && skill.Id == source.SourceId),
                        "Active skills cannot supply passive world effects.");
                }
            }
            var field = world.Battlefield; Require(field.MaxX <= MaximumCoordinate && field.MaxY <= MaximumCoordinate, "Active battlefield exceeds numeric safety bounds.");
            foreach (var c in world.Cohorts)
            { Require(active.EnemyBodies.Any(b => b.EnemyId == c.EnemyId) && c.Stats.Range <= MaximumCoordinate && c.Stats.Speed <= MaximumCoordinate, "Missing body or unsafe enemy geometry."); }
            var limit = world.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
            foreach (var d in active.Definitions)
            { Require(d.Dash.DurationTicks < int.MaxValue - limit && d.Dash.CooldownTicks <= int.MaxValue - limit, "Active tick overflow."); }
        }
        private static void Ordered<T>(IReadOnlyList<T> values, Func<T, string> key)
        {
            if (values is null || values.Count > WorldCombatCodec.MaximumEntries) { throw new ArgumentException("Invalid active collection."); } string? previous = null;
            foreach (var value in values) { if (value is null) { throw new ArgumentException("Null active entry."); } var id = key(value); Id(id); Require(previous is null || StringComparer.Ordinal.Compare(previous, id) < 0, "Unordered active IDs."); previous = id; }
        }
        private static void Id(string value) => Require(!string.IsNullOrEmpty(value) && value.Length <= WorldCombatCodec.MaximumIdentifierCharacters && value.All(c => c >= '!' && c <= '~'), "Invalid active identifier.");
        private static void Hash(string value) => Require(value is not null && value.Length == WorldCombatCodec.Sha256HexCharacters && value.All(c => c >= '0' && c <= '9' || c >= 'A' && c <= 'F'), "Invalid active hash.");
        private static void Require(bool value, string message) { if (!value) { throw new ArgumentException(message); } }
        private static int Count(BinaryReader r) { var count = r.ReadInt32(); if (count < 0 || count > WorldCombatCodec.MaximumEntries || count > r.BaseStream.Length - r.BaseStream.Position) { throw new InvalidDataException("Invalid active count."); } return count; }
        private static void Text(BinaryWriter w, string value) { var bytes = Utf8.GetBytes(value); w.Write(bytes.Length); w.Write(bytes); }
        private static string Text(BinaryReader r) { var length = r.ReadInt32(); if (length < 0 || length > WorldCombatCodec.MaximumTextBytes || length > r.BaseStream.Length - r.BaseStream.Position) { throw new InvalidDataException("Invalid active text."); } return Utf8.GetString(r.ReadBytes(length)); }
    }
}
