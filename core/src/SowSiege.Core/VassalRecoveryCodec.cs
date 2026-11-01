using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    // ADR0075: a resumable command prefix, not a terminal replay or a general world snapshot decoder.
    public static class VassalRecoveryCodec
    {
        private const string Magic = "SowSiege-vassal-recovery-v1";
        private const int MaximumCommands = ReplayCodec.MaximumCommands;
        private const int MaximumBytes = 134217728;
        private const int MaximumExtensionBytes = 16777216;
        private const int MaximumHeaderBytes = 131072;
        private const int MinimumCommandBytes = 29;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Capture(InteractiveSession session, IReadOnlyList<ReplayCommand> commands)
        {
            if (session is null || commands is null) { throw new ArgumentNullException(nameof(session)); }
            if (session.State.Vassals is null || session.Options.Vassals is null) { throw new ArgumentException("Recovery requires an enabled vassal run."); }
            session.State.Vassals.ValidateWorld(session.Simulation.World.WaveRuntime!);
            if (commands.Count > MaximumCommands || commands.Count != session.NextSequence) { throw new ArgumentException("Recovery requires the complete command prefix."); }
            var prefix = commands.ToArray();
            var header = ReplayCodec.Header(session.Options);
            var reconstructed = Reconstruct(session.Catalog, session.Options.DataHash, header, prefix, session.InitialCatalogHash);
            var extension = session.State.Vassals.Encode();
            if (reconstructed.ComputeStateHash() != session.ComputeStateHash() ||
                !extension.SequenceEqual(reconstructed.State.Vassals!.Encode()))
            { throw new InvalidDataException("Recovery prefix does not describe this session."); }
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Utf8, true);
            WriteText(writer, Magic); writer.Write(1);
            using var headerBytes = new MemoryStream(); ReplayCodec.WriteHeader(headerBytes, header);
            WriteBlock(writer, headerBytes.ToArray(), MaximumHeaderBytes);
            WriteText(writer, session.InitialCatalogHash);
            writer.Write(prefix.Length);
            foreach (var command in prefix)
            {
                ReplayCodec.WriteCommand(output, command);
                if (output.Length > MaximumBytes) { throw new InvalidDataException("Recovery exceeds the size limit."); }
            }
            writer.Write(session.NextSequence); writer.Write(session.Simulation.World.Tick);
            WriteText(writer, session.ComputeStateHash());
            WriteBlock(writer, extension, MaximumExtensionBytes);
            if (output.Length > MaximumBytes) { throw new InvalidDataException("Recovery exceeds the size limit."); }
            return output.ToArray();
        }

        public static InteractiveSession Restore(ContentCatalog catalog, string actualDataHash, byte[] bytes)
        {
            if (catalog is null || bytes is null) { throw new ArgumentNullException(nameof(catalog)); }
            if (bytes.Length > MaximumBytes) { throw new InvalidDataException("Recovery exceeds the size limit."); }
            try
            {
                using var input = new MemoryStream(bytes, false);
                using var reader = new BinaryReader(input, Utf8, true);
                if (ReadText(reader) != Magic || reader.ReadInt32() != 1) { throw new InvalidDataException("Unsupported recovery format."); }
                var headerBytes = ReadBlock(reader, MaximumHeaderBytes);
                using var headerInput = new MemoryStream(headerBytes, false);
                using var headerReader = new BinaryReader(headerInput, Utf8, true);
                var header = ReplayCodec.ReadHeader(headerReader);
                if (header.FormatVersion != ReplayCodec.VassalVersion || header.Options.Vassals is null || headerInput.Position != headerInput.Length)
                { throw new InvalidDataException("Recovery requires a complete format8 header."); }
                using var canonicalHeader = new MemoryStream(); ReplayCodec.WriteHeader(canonicalHeader, header);
                if (!headerBytes.SequenceEqual(canonicalHeader.ToArray())) { throw new InvalidDataException("Noncanonical recovery header."); }
                var catalogHash = ReadText(reader); ReplayCodec.ValidateHash(catalogHash);
                var count = reader.ReadInt32();
                if (count < 0 || count > MaximumCommands || count > (input.Length - input.Position) / MinimumCommandBytes)
                { throw new InvalidDataException("Invalid recovery command count."); }
                var commands = new ReplayCommand[count];
                var tick = 0;
                for (var i = 0; i < count; i++)
                {
                    var command = ReplayCodec.ReadRecoveryCommand(reader);
                    if (command.Sequence != i || command.Tick != tick) { throw new InvalidDataException("Recovery command frontier mismatch."); }
                    commands[i] = command;
                    if (command.Kind == ReplayCommandKind.Advance) { tick = checked(tick + 1); }
                }
                var sequence = reader.ReadInt64(); var frontierTick = reader.ReadInt32();
                var frontierHash = ReadText(reader); ReplayCodec.ValidateHash(frontierHash);
                var extension = ReadBlock(reader, MaximumExtensionBytes);
                if (sequence != count || frontierTick != tick || input.Position != input.Length)
                { throw new InvalidDataException("Invalid recovery frontier or trailing bytes."); }
                var session = Reconstruct(catalog, actualDataHash, header, commands, catalogHash);
                session.State.Vassals!.ValidateWorld(session.Simulation.World.WaveRuntime!);
                if (session.NextSequence != sequence || session.Simulation.World.Tick != frontierTick ||
                    session.ComputeStateHash() != frontierHash || !extension.SequenceEqual(session.State.Vassals!.Encode()))
                { throw new InvalidDataException("Recovery state or vassal remainder mismatch."); }
                return session;
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is ArgumentException || ex is OverflowException || ex is InvalidOperationException)
            { throw new InvalidDataException("Invalid vassal recovery: " + ex.Message, ex); }
        }

        private static InteractiveSession Reconstruct(ContentCatalog catalog, string dataHash, ReplayHeader header,
            IReadOnlyList<ReplayCommand> commands, string catalogHash)
        {
            if (!string.Equals(dataHash, header.Options.DataHash, StringComparison.Ordinal) ||
                !string.Equals(PortableStateCodec.HashCatalog(catalog), catalogHash, StringComparison.Ordinal))
            { throw new InvalidDataException("Recovery catalog or data identity mismatch."); }
            var restored = new InteractiveSession(catalog, header.Options);
            foreach (var command in commands) { restored.Apply(command); }
            return restored;
        }

        private static void WriteText(BinaryWriter writer, string text)
        {
            var bytes = Utf8.GetBytes(text);
            WriteBlock(writer, bytes, ReplayCodec.MaximumStringBytes);
        }
        private static string ReadText(BinaryReader reader) => Utf8.GetString(ReadBlock(reader, ReplayCodec.MaximumStringBytes));
        private static void WriteBlock(BinaryWriter writer, byte[] bytes, int maximum)
        {
            if (bytes.Length > maximum) { throw new ArgumentException("Recovery block too large."); }
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static byte[] ReadBlock(BinaryReader reader, int maximum)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > maximum || length > reader.BaseStream.Length - reader.BaseStream.Position)
            { throw new InvalidDataException("Invalid recovery block size."); }
            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length) { throw new EndOfStreamException(); }
            return bytes;
        }
    }
}
