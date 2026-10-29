using System;
using System.IO;

namespace SowSiege.Core
{
    public static partial class ReplayCodec
    {
        internal const int MaximumVassalBlockBytes = VassalRunCodec.MaximumBytes;

        internal static ReplayCommand ReadRecoveryCommand(BinaryReader reader)
        {
            if (reader.ReadInt32() != CommandTag) { throw new InvalidDataException("Recovery command tag mismatch."); }
            var command = new ReplayCommand(reader.ReadInt64(), reader.ReadInt32(), (ReplayCommandKind)reader.ReadInt32(),
                new(reader.ReadInt16(), reader.ReadInt16()), Text(reader), reader.ReadInt32());
            ValidateCommand(command);
            return command;
        }

        private static void WriteVassalHeader(BinaryWriter writer, InteractiveOptions options)
        {
            options = options with { Feel = FeelExperimentOptions.Normalize(options.Feel) };
            writer.Write(1); // Format8 envelope schema, independent of the estate header schema.
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
            var bytes = VassalRunCodec.Encode(options.Vassals!);
            if (bytes.Length > MaximumVassalBlockBytes) { throw new ArgumentException("Vassal header block too large."); }
            writer.Write(bytes.Length); writer.Write(bytes);
        }

        private static InteractiveOptions ReadVassalHeader(BinaryReader reader, RunOptions run, AimMode aim, string dataHash)
        {
            if (reader.ReadInt32() != 1) { throw new InvalidDataException("Unsupported format8 envelope schema."); }
            run = run with { TargetMaterial = Text(reader) };
            FeelExperimentOptions? feel = null;
            if (Boolean(reader))
            {
                var flags = reader.ReadInt32();
                if (flags <= 0 || (flags & ~FeelFlags) != 0) { throw new InvalidDataException("Invalid format8 feel flags."); }
                feel = new((flags & 1) != 0, (flags & ContactFlag) != 0, (flags & HealthFlag) != 0);
            }
            FieldOptions? field = Boolean(reader) ? new(reader.ReadInt32()) : null;
            var host = Boolean(reader) ? ReadHostRewards(reader) : null;
            EstateRunOptions? estate = Boolean(reader) ? new(reader.ReadInt32(), RequiredText(reader), RequiredText(reader)) : null;
            MeleeAimOptions? melee = Boolean(reader) ? new(reader.ReadInt32()) : null;
            var length = reader.ReadInt32();
            if (length <= 0 || length > MaximumVassalBlockBytes) { throw new InvalidDataException("Invalid vassal block size."); }
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) { throw new EndOfStreamException(); }
            return new(run, aim, dataHash, feel, field, host, melee, estate, VassalRunCodec.Decode(bytes));
        }
    }
}
