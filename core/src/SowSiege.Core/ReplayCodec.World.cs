using System;
using System.IO;

namespace SowSiege.Core
{
    public static partial class ReplayCodec
    {
        private static void ValidateWorldHeader(ReplayHeader header)
        {
            var options = header.Options;
            var world = options.WorldCombat ?? throw new ArgumentException("Format9 requires a world block.");
            WorldCombatCodec.Validate(world);
            HeroActiveCodec.ValidateBinding(options);
            WorldCombatCodec.ValidateVassals(world, options.Vassals);
            ValidateHeader(Header(options with { WorldCombat = null, HeroActive = null }) with
            {
                RulesVersion = header.RulesVersion,
                StateCodecVersion = header.StateCodecVersion,
                RngAlgorithm = header.RngAlgorithm
            });
            if (options.Field is { Version: FieldOptions.ReviewVersion }) { throw new ArgumentException("Review field radii require replay v6."); }
            var provenance = world.Snapshot.Provenance;
            if (options.Run.EstateLoadout is not null) { throw new ArgumentException("World replay estate selection must use its explicit estate option."); }
            if (provenance.Seed != options.Run.Seed || provenance.RngAlgorithm != header.RngAlgorithm || world.Snapshot.Hero.Id != options.Run.HeroId)
            { throw new ArgumentException("World provenance does not match the replay preamble."); }
        }

        private static void WriteWorldHeader(BinaryWriter writer, InteractiveOptions options)
        {
            options = options with { Feel = FeelExperimentOptions.Normalize(options.Feel) };
            writer.Write(options.HeroActive is null ? 1 : HeroActiveCodec.EnvelopeSchema);
            Text(writer, options.Run.TargetMaterial);
            writer.Write(options.Feel is not null);
            if (options.Feel is { } feel)
            {
                writer.Write((feel.FasterLord ? 1 : 0) | (feel.ImmediateContact ? ContactFlag : 0) | (feel.ReadableHealth ? HealthFlag : 0));
            }
            writer.Write(options.Field is not null);
            if (options.Field is { } field) { writer.Write(field.Version); }
            writer.Write(options.HostRewards is not null);
            if (options.HostRewards is { } host) { WriteHostRewards(writer, host); }
            writer.Write(options.Estate is not null);
            if (options.Estate is { } estate)
            {
                writer.Write(estate.Version); Text(writer, estate.LoadoutId); Text(writer, estate.LoadoutCatalogHash);
            }
            writer.Write(options.MeleeAim is not null);
            if (options.MeleeAim is { } melee) { writer.Write(melee.Version); }
            writer.Write(options.Vassals is not null);
            if (options.Vassals is { } vassals) { WriteWorldBlock(writer, VassalRunCodec.Encode(vassals)); }
            WriteWorldBlock(writer, WorldCombatCodec.Encode(options.WorldCombat!));
            if (options.HeroActive is { } active) { WriteWorldBlock(writer, HeroActiveCodec.Encode(active)); }
        }

        private static InteractiveOptions ReadWorldHeader(BinaryReader reader, RunOptions run, AimMode aim, string dataHash)
        {
            var schema = reader.ReadInt32();
            if (schema != 1 && schema != HeroActiveCodec.EnvelopeSchema) { throw new InvalidDataException("Unsupported format9 envelope schema."); }
            run = run with { TargetMaterial = Text(reader) };
            FeelExperimentOptions? feel = null;
            if (Boolean(reader))
            {
                var flags = reader.ReadInt32();
                if (flags <= 0 || (flags & ~FeelFlags) != 0) { throw new InvalidDataException("Invalid format9 feel flags."); }
                feel = new((flags & 1) != 0, (flags & ContactFlag) != 0, (flags & HealthFlag) != 0);
            }
            FieldOptions? field = Boolean(reader) ? new(reader.ReadInt32()) : null;
            var host = Boolean(reader) ? ReadHostRewards(reader) : null;
            EstateRunOptions? estate = Boolean(reader) ? new(reader.ReadInt32(), RequiredText(reader), RequiredText(reader)) : null;
            MeleeAimOptions? melee = Boolean(reader) ? new(reader.ReadInt32()) : null;
            var vassals = Boolean(reader) ? VassalRunCodec.Decode(ReadWorldBlock(reader, MaximumVassalBlockBytes)) : null;
            var world = WorldCombatCodec.Decode(ReadWorldBlock(reader, WorldCombatCodec.MaximumBytes));
            var active = schema == HeroActiveCodec.EnvelopeSchema ? HeroActiveCodec.Decode(ReadWorldBlock(reader, WorldCombatCodec.MaximumBytes)) : null;
            return new(run, aim, dataHash, feel, field, host, melee, estate, vassals, world, active);
        }

        private static void WriteWorldBlock(BinaryWriter writer, byte[] bytes)
        {
            writer.Write(bytes.Length); writer.Write(bytes);
        }

        private static byte[] ReadWorldBlock(BinaryReader reader, int maximum)
        {
            var length = reader.ReadInt32();
            if (length <= 0 || length > maximum || reader.BaseStream.CanSeek && length > reader.BaseStream.Length - reader.BaseStream.Position)
            { throw new InvalidDataException("Invalid world block length."); }
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) { throw new EndOfStreamException(); }
            return bytes;
        }
    }
}
