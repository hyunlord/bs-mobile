using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    // This is an unverified transport candidate. Decoding does not restore a session or authenticate runtime bytes.
    public sealed record WorldRecoveryDocument(ReplayHeader Header, string CatalogHash, IReadOnlyList<ReplayCommand> Commands,
        long NextSequence, int Tick, string StateHash, byte[] WorldRuntimeBytes, byte[]? VassalRuntimeBytes, byte[]? HeroActiveRuntimeBytes = null);

    // Decode returns transport only; Restore reexecutes the complete prefix before accepting its frontier.
    public static class WorldRecoveryCodec
    {
        private const string Magic = "SowSiege-world-recovery-v1";
        public const int MaximumBytes = 134217728;
        public const int MaximumRuntimeBytes = 16777216;
        public const int MaximumHeaderBytes = 2097152;
        private const int MinimumCommandBytes = 29;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Capture(InteractiveSession session, IReadOnlyList<ReplayCommand> commands)
        {
            if (session is null || commands is null || session.State.WorldCombat is null) { throw new ArgumentException("World recovery requires a world session and complete prefix."); }
            if (session.State.WorldCombat.Terminal == WorldTerminalReason.Quit) { throw new InvalidOperationException("A quit terminal is verified through its world-end record, not a resumable recovery prefix."); }
            var document = new WorldRecoveryDocument(ReplayCodec.Header(session.Options), session.InitialCatalogHash,
                Array.AsReadOnly(commands.ToArray()), session.NextSequence, session.Simulation.World.Tick, session.ComputeStateHash(),
                session.State.WorldCombat.Encode(), session.State.Vassals?.Encode(), session.State.HeroActive?.Encode());
            var bytes = Encode(document);
            // Compare reexecution even during capture: a caller cannot attach an unrelated command prefix.
            Restore(session.Catalog, session.Options.DataHash, bytes);
            return bytes;
        }

        public static InteractiveSession Restore(ContentCatalog catalog, string actualDataHash, byte[] bytes)
        {
            var document = Decode(bytes);
            if (document.Header.Options.DataHash != actualDataHash || PortableStateCodec.HashCatalog(catalog) != document.CatalogHash)
            { throw new InvalidDataException("World recovery catalog identity mismatch."); }
            try
            {
                var session = new InteractiveSession(catalog, document.Header.Options);
                foreach (var command in document.Commands) { session.Apply(command); }
                if (session.NextSequence != document.NextSequence || session.Simulation.World.Tick != document.Tick || session.ComputeStateHash() != document.StateHash ||
                    !session.State.WorldCombat!.Encode().SequenceEqual(document.WorldRuntimeBytes) ||
                    (session.State.Vassals is { } vassals && !vassals.Encode().SequenceEqual(document.VassalRuntimeBytes!)) ||
                    (session.State.HeroActive is { } active && !active.Encode().SequenceEqual(document.HeroActiveRuntimeBytes!)))
                { throw new InvalidDataException("World recovery frontier or runtime differs from the complete prefix."); }
                return session;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
            { throw new InvalidDataException("World recovery prefix reexecution failed.", exception); }
        }

        public static byte[] Encode(WorldRecoveryDocument document)
        {
            if (document is null) { throw new ArgumentNullException(nameof(document)); }
            Validate(document);
            using var output = new MemoryStream();
            using var writer = new BinaryWriter(output, Utf8, true);
            var active = document.Header.Options.HeroActive is not null;
            WriteText(writer, active ? "SowSiege-world-recovery-v2" : Magic); writer.Write(active ? HeroActiveCodec.EnvelopeSchema : 1);
            using var headerBytes = new MemoryStream(); ReplayCodec.WriteHeader(headerBytes, document.Header);
            WriteBlock(writer, headerBytes.ToArray(), MaximumHeaderBytes);
            WriteText(writer, document.CatalogHash); writer.Write(document.Commands.Count);
            foreach (var command in document.Commands)
            {
                ReplayCodec.WriteCommand(output, command);
                if (output.Length > MaximumBytes) { throw new ArgumentException("World recovery exceeds the size limit."); }
            }
            writer.Write(document.NextSequence); writer.Write(document.Tick); WriteText(writer, document.StateHash);
            WriteBlock(writer, document.WorldRuntimeBytes, MaximumRuntimeBytes);
            writer.Write(document.VassalRuntimeBytes is not null);
            if (document.VassalRuntimeBytes is { } vassals) { WriteBlock(writer, vassals, MaximumRuntimeBytes); }
            if (active) { WriteBlock(writer, document.HeroActiveRuntimeBytes!, MaximumRuntimeBytes); }
            writer.Flush();
            if (output.Length > MaximumBytes) { throw new ArgumentException("World recovery exceeds the size limit."); }
            return output.ToArray();
        }

        public static WorldRecoveryDocument Decode(byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0 || bytes.Length > MaximumBytes)
            { throw new InvalidDataException("Invalid world recovery size."); }
            try
            {
                using var input = new MemoryStream(bytes, false); using var reader = new BinaryReader(input, Utf8, true);
                var magic = ReadText(reader); var schema = reader.ReadInt32();
                if (!(magic == Magic && schema == 1 || magic == "SowSiege-world-recovery-v2" && schema == HeroActiveCodec.EnvelopeSchema))
                { throw new InvalidDataException("Unsupported world recovery format."); }
                var headerBytes = ReadBlock(reader, MaximumHeaderBytes);
                using var headerInput = new MemoryStream(headerBytes, false);
                using var headerReader = new BinaryReader(headerInput, Utf8, true);
                var header = ReplayCodec.ReadHeader(headerReader);
                if (headerInput.Position != headerInput.Length) { throw new InvalidDataException("Trailing world recovery header bytes."); }
                ValidateHeader(header);
                if ((schema == HeroActiveCodec.EnvelopeSchema) != (header.Options.HeroActive is not null)) { throw new InvalidDataException("Active recovery/header version mismatch."); }
                using var canonicalHeader = new MemoryStream(); ReplayCodec.WriteHeader(canonicalHeader, header);
                if (!headerBytes.SequenceEqual(canonicalHeader.ToArray()))
                { throw new InvalidDataException("Noncanonical world recovery header."); }
                var catalogHash = ReadText(reader); ValidateHash(catalogHash);
                var count = reader.ReadInt32();
                if (count < 0 || count > ReplayCodec.MaximumCommands || count > (input.Length - input.Position) / MinimumCommandBytes)
                { throw new InvalidDataException("Invalid world recovery command count."); }
                var commands = new ReplayCommand[count];
                for (var i = 0; i < count; i++) { commands[i] = ReplayCodec.ReadRecoveryCommand(reader); }
                var sequence = reader.ReadInt64(); var tick = reader.ReadInt32(); var stateHash = ReadText(reader);
                var world = ReadBlock(reader, MaximumRuntimeBytes);
                var flag = reader.ReadByte();
                if (flag > 1) { throw new InvalidDataException("Invalid world recovery vassal flag."); }
                var vassals = flag == 1 ? ReadBlock(reader, MaximumRuntimeBytes) : null;
                var active = schema == HeroActiveCodec.EnvelopeSchema ? ReadBlock(reader, MaximumRuntimeBytes) : null;
                if (input.Position != input.Length) { throw new InvalidDataException("Trailing world recovery bytes."); }
                var result = new WorldRecoveryDocument(header, catalogHash, Array.AsReadOnly(commands), sequence, tick, stateHash, world, vassals, active);
                Validate(result);
                return result;
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is ArgumentException || ex is OverflowException || ex is InvalidOperationException)
            { throw new InvalidDataException("Invalid world recovery envelope: " + ex.Message, ex); }
        }

        private static void Validate(WorldRecoveryDocument document)
        {
            ValidateHeader(document.Header); ValidateHash(document.CatalogHash); ValidateHash(document.StateHash);
            if (document.Commands is null || document.Commands.Count > ReplayCodec.MaximumCommands ||
                document.NextSequence != document.Commands.Count || document.Tick < 0)
            { throw new ArgumentException("World recovery requires a complete command prefix."); }
            var limit = document.Header.Options.WorldCombat!.Snapshot.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
            var tick = 0;
            for (var i = 0; i < document.Commands.Count; i++)
            {
                var command = document.Commands[i]; ReplayCodec.ValidateCommand(command);
                if (command.Sequence != i || command.Tick != tick || tick >= limit)
                { throw new ArgumentException("World recovery command frontier mismatch."); }
                if (command.Kind is ReplayCommandKind.SetInvulnerable or ReplayCommandKind.SetSpawnPermille or ReplayCommandKind.GrantLevel)
                { throw new ArgumentException("World recovery cannot contain review commands."); }
                var host = document.Header.Options.HostRewards;
                if (command.Kind == ReplayCommandKind.HostGrantRerollV1 && host?.AllowRerollGrant != true ||
                    command.Kind == ReplayCommandKind.HostReviveV1 && host?.Revive is null)
                { throw new ArgumentException("World recovery host command is not enabled."); }
                if (command.Kind == ReplayCommandKind.Advance) { tick = checked(tick + 1); }
            }
            if (document.Tick != tick) { throw new ArgumentException("World recovery frontier tick mismatch."); }
            ValidateBlock(document.WorldRuntimeBytes, MaximumRuntimeBytes);
            if ((document.Header.Options.Vassals is not null) != (document.VassalRuntimeBytes is not null))
            { throw new ArgumentException("World recovery vassal runtime presence mismatch."); }
            if (document.VassalRuntimeBytes is { } vassals) { ValidateBlock(vassals, MaximumRuntimeBytes); }
            if ((document.Header.Options.HeroActive is not null) != (document.HeroActiveRuntimeBytes is not null)) { throw new ArgumentException("Active runtime presence mismatch."); }
            if (document.HeroActiveRuntimeBytes is { } active) { ValidateBlock(active, MaximumRuntimeBytes); }
        }

        private static void ValidateHeader(ReplayHeader header)
        {
            if (header is null || header.Options is null || header.FormatVersion != ReplayCodec.WorldCombatVersion ||
                header.Options.WorldCombat is null)
            { throw new ArgumentException("World recovery requires a direct format9 header."); }
            WorldCombatCodec.Validate(header.Options.WorldCombat);
            if (header.Options.WorldCombat.Snapshot.Mode != WorldCombatMode.DirectPve)
            { throw new ArgumentException("World recovery requires a direct format9 header."); }
        }

        private static void ValidateHash(string hash)
        {
            ReplayCodec.ValidateHash(hash);
            foreach (var character in hash)
            {
                if (!(character >= '0' && character <= '9' || character >= 'A' && character <= 'F'))
                { throw new ArgumentException("World recovery hashes must be canonical uppercase hexadecimal."); }
            }
        }

        private static void ValidateBlock(byte[] bytes, int maximum)
        {
            if (bytes is null || bytes.Length == 0 || bytes.Length > maximum)
            { throw new ArgumentException("Invalid world recovery block size."); }
        }
        private static void WriteText(BinaryWriter writer, string text) => WriteBlock(writer, Utf8.GetBytes(text), ReplayCodec.MaximumStringBytes);
        private static string ReadText(BinaryReader reader) => Utf8.GetString(ReadBlock(reader, ReplayCodec.MaximumStringBytes));
        private static void WriteBlock(BinaryWriter writer, byte[] bytes, int maximum)
        {
            ValidateBlock(bytes, maximum); writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static byte[] ReadBlock(BinaryReader reader, int maximum)
        {
            var length = reader.ReadInt32();
            if (length <= 0 || length > maximum || length > reader.BaseStream.Length - reader.BaseStream.Position)
            { throw new InvalidDataException("Invalid world recovery block size."); }
            return reader.ReadBytes(length);
        }
    }
}
