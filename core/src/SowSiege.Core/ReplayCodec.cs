using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    public sealed record ReplayHeader(int FormatVersion, int RulesVersion, int StateCodecVersion, string RngAlgorithm, InteractiveOptions Options);
    public sealed record ReplayCheckpoint(long AppliedCommands, int Tick, string StateHash);
    public enum ReplayEndKind { Duration, Death, Quit }
    public sealed record ReplayEnd(long AppliedCommands, int Tick, ReplayEndKind Kind, string StateHash);
    public sealed record ReplayDocument(ReplayHeader Header, IReadOnlyList<ReplayCommand> Commands, IReadOnlyList<ReplayCheckpoint> Checkpoints, ReplayEnd End);
    public sealed record ReplayVerification(int Seed, int Tick, string StateHash, ReplayEndKind EndKind);

    public static class ReplayCodec
    {
        public const int Version = 1;
        private const int MaterialTargetVersion = 2;
        private const int CommandTag = 1;
        private const int CheckpointTag = 2;
        private const int EndTag = 3;
        private const int HashLength = 64;
        private const int MaximumStringBytes = 4096;
        private const int MaximumCommands = 1000000;
        private const string Magic = "SowSiege-replay";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public static ReplayHeader Header(InteractiveOptions options) => new(options.Run.TargetMaterial is null ? Version : MaterialTargetVersion, Version, Version, PortableRandom.Algorithm, options);
        internal static void ValidateHash(string hash)
        {
            if (hash is null || hash.Length != HashLength || hash.Any(c => !Uri.IsHexDigit(c))) { throw new ArgumentException("Expected SHA-256 hexadecimal hash."); }
        }
        internal static void ValidateCommand(ReplayCommand c)
        {
            if (c is null || c.Sequence < 0 || c.Tick < 0 || !Enum.IsDefined(typeof(ReplayCommandKind), c.Kind)) { throw new ArgumentException("Invalid command identity."); }
            var card = c.Kind is ReplayCommandKind.ChooseCard or ReplayCommandKind.BanCard or ReplayCommandKind.LockCard;
            if (card ? string.IsNullOrEmpty(c.CardId) || Utf8.GetByteCount(c.CardId) > MaximumStringBytes : c.CardId is not null) { throw new ArgumentException("Invalid card payload."); }
            if (c.Kind != ReplayCommandKind.Advance && (c.Input.X != 0 || c.Input.Y != 0)) { throw new ArgumentException("Unexpected movement payload."); }
            var valid = c.Kind switch
            {
                ReplayCommandKind.SetAimMode => Enum.IsDefined(typeof(AimMode), c.Value),
                ReplayCommandKind.SetInvulnerable => c.Value == 0 || c.Value == 1,
                ReplayCommandKind.SetSpawnPermille => c.Value >= 0 && c.Value <= InteractiveSession.MaximumSpawnPermille,
                _ => c.Value == 0
            };
            if (!valid) { throw new ArgumentException("Invalid command value."); }
        }
        private static void ValidateHeader(ReplayHeader h)
        {
            if ((h.FormatVersion != Version && h.FormatVersion != MaterialTargetVersion) || h.RulesVersion != Version || h.StateCodecVersion != Version || h.RngAlgorithm != PortableRandom.Algorithm) { throw new ArgumentException("Unsupported replay version or RNG."); }
            ValidateHash(h.Options.DataHash);
            if (!h.Options.Run.ManualCards || h.Options.Run.Scenario != "normal" || h.Options.Run.Movement is not null || !Enum.IsDefined(typeof(AimMode), h.Options.InitialAimMode)) { throw new ArgumentException("Invalid interactive options."); }
        }
        private static BinaryWriter Writer(Stream stream) => new(stream, Utf8, true);
        private static void Text(BinaryWriter w, string? value)
        {
            w.Write(value is not null); if (value is null) { return; }
            var bytes = Utf8.GetBytes(value); if (bytes.Length > MaximumStringBytes) { throw new ArgumentException("String too long."); }
            w.Write(bytes.Length); w.Write(bytes);
        }
        private static bool Boolean(BinaryReader r)
        {
            var value = r.ReadByte(); if (value > 1) { throw new InvalidDataException("Invalid boolean."); }
            return value != 0;
        }
        private static string? Text(BinaryReader r)
        {
            if (!Boolean(r)) { return null; }
            var length = r.ReadInt32(); if (length < 0 || length > MaximumStringBytes) { throw new InvalidDataException("Invalid string length."); }
            var bytes = r.ReadBytes(length); if (bytes.Length != length) { throw new EndOfStreamException(); }
            return Utf8.GetString(bytes);
        }
        private static string RequiredText(BinaryReader r) => Text(r) ?? throw new InvalidDataException("Required string missing.");
        public static void WriteHeader(Stream output, ReplayHeader header)
        {
            ValidateHeader(header); using var w = Writer(output); Text(w, Magic); w.Write(header.FormatVersion); w.Write(header.RulesVersion); w.Write(header.StateCodecVersion); Text(w, header.RngAlgorithm);
            var o = header.Options; var r = o.Run;
            w.Write(r.Seed); Text(w, r.HeroId); Text(w, r.EstateId); Text(w, r.Policy); Text(w, r.PeopleRule); Text(w, r.Scenario); w.Write(r.ManualCards); Text(w, r.Movement); w.Write((int)o.InitialAimMode); Text(w, o.DataHash); if (header.FormatVersion == MaterialTargetVersion) Text(w, r.TargetMaterial);
        }
        public static void WriteCommand(Stream output, ReplayCommand command)
        {
            ValidateCommand(command); using var w = Writer(output); w.Write(CommandTag); w.Write(command.Sequence); w.Write(command.Tick); w.Write((int)command.Kind); w.Write(command.Input.X); w.Write(command.Input.Y); Text(w, command.CardId); w.Write(command.Value);
        }
        public static void WriteCheckpoint(Stream output, ReplayCheckpoint checkpoint)
        {
            ValidateHash(checkpoint.StateHash); using var w = Writer(output); w.Write(CheckpointTag); w.Write(checkpoint.AppliedCommands); w.Write(checkpoint.Tick); Text(w, checkpoint.StateHash);
        }
        public static void WriteEnd(Stream output, ReplayEnd end)
        {
            ValidateHash(end.StateHash); if (!Enum.IsDefined(typeof(ReplayEndKind), end.Kind)) { throw new ArgumentException("Invalid terminal kind."); }
            using var w = Writer(output); w.Write(EndTag); w.Write(end.AppliedCommands); w.Write(end.Tick); w.Write((int)end.Kind); Text(w, end.StateHash);
        }
        public static ReplayDocument Read(Stream input)
        {
            try
            {
                using var r = new BinaryReader(input, Utf8, true);
                if (RequiredText(r) != Magic) { throw new InvalidDataException("Replay magic mismatch."); }
                var format = r.ReadInt32(); var rules = r.ReadInt32(); var state = r.ReadInt32(); var rng = RequiredText(r);
                var run = new RunOptions(r.ReadInt32(), RequiredText(r), RequiredText(r), RequiredText(r), Text(r), RequiredText(r), Boolean(r), Text(r));
                var aim = (AimMode)r.ReadInt32(); var hash = RequiredText(r); if (format == MaterialTargetVersion) run = run with { TargetMaterial = RequiredText(r) };
                var header = new ReplayHeader(format, rules, state, rng, new(run, aim, hash)); ValidateHeader(header);
                var commands = new List<ReplayCommand>(); var checkpoints = new List<ReplayCheckpoint>(); var tick = 0;
                while (true)
                {
                    var tag = r.ReadInt32();
                    if (tag == CommandTag)
                    {
                        if (commands.Count >= MaximumCommands) { throw new InvalidDataException("Replay command limit exceeded."); }
                        var command = new ReplayCommand(r.ReadInt64(), r.ReadInt32(), (ReplayCommandKind)r.ReadInt32(), new(r.ReadInt16(), r.ReadInt16()), Text(r), r.ReadInt32()); ValidateCommand(command);
                        if (command.Sequence != commands.Count || command.Tick != tick) { throw new InvalidDataException("Command order or tick mismatch."); }
                        commands.Add(command); if (command.Kind == ReplayCommandKind.Advance) { tick++; }
                    }
                    else if (tag == CheckpointTag)
                    {
                        var checkpoint = new ReplayCheckpoint(r.ReadInt64(), r.ReadInt32(), RequiredText(r)); ValidateHash(checkpoint.StateHash);
                        if (checkpoint.AppliedCommands != commands.Count || checkpoint.Tick != tick || checkpoints.Count > 0 && checkpoint.AppliedCommands <= checkpoints[checkpoints.Count - 1].AppliedCommands) { throw new InvalidDataException("Checkpoint order mismatch."); }
                        checkpoints.Add(checkpoint);
                    }
                    else if (tag == EndTag)
                    {
                        var end = new ReplayEnd(r.ReadInt64(), r.ReadInt32(), (ReplayEndKind)r.ReadInt32(), RequiredText(r)); ValidateHash(end.StateHash);
                        if (!Enum.IsDefined(typeof(ReplayEndKind), end.Kind) || end.AppliedCommands != commands.Count || end.Tick != tick || input.ReadByte() != -1) { throw new InvalidDataException("Terminal state or trailing bytes invalid."); }
                        return new(header, Array.AsReadOnly(commands.ToArray()), Array.AsReadOnly(checkpoints.ToArray()), end);
                    }
                    else { throw new InvalidDataException("Unknown replay tag."); }
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is ArgumentException || ex is OverflowException) { throw new InvalidDataException("Malformed or truncated replay: " + ex.Message, ex); }
        }
    }

    public static class ReplayRunner
    {
        public static ReplayVerification Verify(ContentCatalog catalog, string actualDataHash, ReplayDocument replay)
        {
            // Validate the complete wire representation before creating a mutable session.
            using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, replay.Header);
            var checkpoints = replay.Checkpoints.ToDictionary(c => c.AppliedCommands);
            if (checkpoints.TryGetValue(0, out var initial)) { ReplayCodec.WriteCheckpoint(bytes, initial); }
            foreach (var command in replay.Commands)
            {
                ReplayCodec.WriteCommand(bytes, command);
                if (checkpoints.TryGetValue(command.Sequence + 1, out var checkpoint)) { ReplayCodec.WriteCheckpoint(bytes, checkpoint); }
            }
            if (checkpoints.Keys.Any(key => key < 0 || key > replay.Commands.Count)) { throw new InvalidDataException("Checkpoint outside command stream."); }
            ReplayCodec.WriteEnd(bytes, replay.End); bytes.Position = 0; var validated = ReplayCodec.Read(bytes);
            if (!string.Equals(actualDataHash, validated.Header.Options.DataHash, StringComparison.Ordinal)) { throw new InvalidDataException("Consumed data hash mismatch."); }
            var session = new InteractiveSession(catalog, validated.Header.Options);
            void Check()
            {
                if (checkpoints.TryGetValue(session.NextSequence, out var cp) && (cp.Tick != session.Simulation.World.Tick || !string.Equals(cp.StateHash, session.ComputeStateHash(), StringComparison.Ordinal))) { throw new InvalidDataException("Replay checkpoint mismatch."); }
            }
            Check();
            foreach (var command in validated.Commands) { session.Apply(command); Check(); }
            var end = validated.End; var summary = session.GetSummary();
            var expected = summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit;
            if (end.Kind != expected || end.Tick != summary.Tick || end.StateHash != summary.StateHash) { throw new InvalidDataException("Replay terminal state mismatch."); }
            return new(summary.Seed, summary.Tick, summary.StateHash, end.Kind);
        }
    }
}
