using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SowSiege.Core
{
    public sealed class WorldCombatApiException : Exception
    {
        public string Code { get; }
        public WorldCombatApiException(string code, string message, Exception? innerException = null)
            : base(message, innerException) { Code = code; }
    }

    public sealed record WorldInputFrontier(byte[] Recovery, long NextSequence, int Tick, string StateHash,
        IReadOnlyList<ReplayCommand> AppendedCommands, WorldCombatResult? Result);

    public static class WorldCombatApi
    {
        public static ReplayHeader CreateContract(ContentCatalog catalog, InteractiveOptions options)
        {
            try
            {
                if (options?.Run is null) { throw new ArgumentNullException(nameof(options)); }
                var header = FreezeHeader(ReplayCodec.Header(options));
                var session = new InteractiveSession(catalog, header.Options);
                return FreezeHeader(ReplayCodec.Header(session.Options));
            }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-contract", "The direct world contract cannot be executed.", exception); }
        }

        public static WorldInputFrontier AcceptPrefix(ContentCatalog catalog, string actualDataHash,
            ReplayHeader issuedHeader, byte[]? acceptedRecovery, IReadOnlyList<ReplayCommand> chunk)
        {
            var header = Contract(issuedHeader, actualDataHash);
            var policy = header.Options.WorldCombat!.Snapshot.Input;
            if (chunk is null || chunk.Count == 0)
            { throw Failure("invalid-prefix", "A new prefix chunk must contain commands."); }
            if (chunk.Count > policy.MaximumChunkCommands)
            { throw Failure("chunk-limit", "The chunk exceeds its issued command limit."); }
            var appended = chunk.ToArray();
            try
            {
                using var bytes = new MemoryStream();
                foreach (var command in appended)
                {
                    ValidatePlayerCommand(command);
                    ReplayCodec.WriteCommand(bytes, command);
                    if (bytes.Length > policy.MaximumChunkBytes)
                    { throw Failure("chunk-limit", "The canonical chunk exceeds its issued byte limit."); }
                }
            }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-command", "The chunk contains an invalid player command.", exception); }

            var session = Restore(catalog, actualDataHash, header, acceptedRecovery, out var commands);
            if (session.State.WorldCombat!.Terminal is not null)
            { throw Failure("battle-terminal", "The accepted battle already has a terminal result."); }
            if (appended.Length > ReplayCodec.MaximumCommands - commands.Count)
            { throw Failure("command-limit", "The complete prefix exceeds its protocol limit."); }
            foreach (var command in appended)
            {
                if (session.State.WorldCombat.Terminal is not null)
                { throw Failure("battle-terminal", "A chunk cannot continue beyond the first terminal."); }
                if (command.Sequence != session.NextSequence || command.Tick != session.Simulation.World.Tick)
                { throw Failure("frontier-conflict", "The command does not extend the accepted frontier."); }
                Apply(session, command);
                commands.Add(command);
            }
            return Capture(session, commands, appended);
        }

        public static WorldInputFrontier AdvanceWithoutInput(ContentCatalog catalog, string actualDataHash,
            ReplayHeader issuedHeader, byte[]? acceptedRecovery, int targetTick)
        {
            var header = Contract(issuedHeader, actualDataHash);
            var limit = header.Options.WorldCombat!.Snapshot.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
            if (targetTick < 0 || targetTick > limit)
            { throw Failure("invalid-frontier", "The requested frontier is outside the issued tick limit."); }
            var session = Restore(catalog, actualDataHash, header, acceptedRecovery, out var commands);
            if (targetTick < session.Simulation.World.Tick)
            { throw Failure("invalid-frontier", "An accepted frontier cannot be rewound."); }
            var appended = new List<ReplayCommand>();
            while (session.Simulation.World.Tick < targetTick && session.State.WorldCombat!.Terminal is null)
            {
                if (commands.Count >= ReplayCodec.MaximumCommands)
                { throw Failure("command-limit", "The complete prefix exceeds its protocol limit."); }
                var tick = session.Simulation.World.Tick;
                var command = session.View.Status == RunStatus.AwaitingCard
                    ? new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard,
                        CardId: session.View.CaptureCards().Cards.OrderBy(id => id, StringComparer.Ordinal).First())
                    : new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance);
                Apply(session, command);
                commands.Add(command); appended.Add(command);
            }
            return Capture(session, commands, appended);
        }

        public static WorldCombatResult VerifyResult(ContentCatalog catalog, string actualDataHash,
            ReplayHeader issuedHeader, byte[]? acceptedRecovery, WorldReplayDocument replay)
        {
            var header = Contract(issuedHeader, actualDataHash);
            if (replay is null) { throw Failure("invalid-result", "A terminal replay is required."); }
            try
            {
                RequireSameHeader(header, replay.Header);
                if (replay.Commands is null || replay.Checkpoints is null || replay.End is null)
                { throw new ArgumentException("Missing replay prefix, checkpoints or result."); }
                if (replay.Commands.Count > ReplayCodec.MaximumCommands)
                { throw Failure("command-limit", "The complete prefix exceeds its protocol limit."); }
                if (replay.Checkpoints.Any(checkpoint => checkpoint is null))
                { throw new ArgumentException("Invalid checkpoint entry."); }
                foreach (var command in replay.Commands) { ValidatePlayerCommand(command); }
                var session = Restore(catalog, actualDataHash, header, acceptedRecovery, out var accepted);
                if (replay.Commands.Count < accepted.Count)
                { throw Failure("frontier-conflict", "The submitted replay omits accepted commands."); }
                for (var i = 0; i < accepted.Count; i++)
                {
                    if (replay.Commands[i] != accepted[i])
                    { throw Failure("frontier-conflict", "The submitted replay rewrites an accepted command."); }
                }
                if (session.State.WorldCombat!.Terminal is not null)
                {
                    if (replay.Commands.Count != accepted.Count)
                    { throw Failure("battle-terminal", "A replay cannot extend an accepted terminal."); }
                    using var expected = new MemoryStream();
                    ReplayCodec.WriteWorldEnd(expected, new(session.NextSequence, session.GetWorldCombatResult()));
                    using var submitted = new MemoryStream(); ReplayCodec.WriteWorldEnd(submitted, replay.End);
                    if (!expected.ToArray().SequenceEqual(submitted.ToArray()))
                    { throw Failure("battle-terminal", "The submitted result replaces an accepted terminal."); }
                }
                return WorldReplayRunner.Verify(catalog, actualDataHash, replay);
            }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-result", "The claimed result differs from authoritative reexecution.", exception); }
        }

        private static ReplayHeader Contract(ReplayHeader issuedHeader, string actualDataHash)
        {
            ReplayHeader header;
            try { header = FreezeHeader(issuedHeader); }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-contract", "The issued contract is invalid.", exception); }
            if (header.Options.DataHash != actualDataHash)
            { throw Failure("data-mismatch", "The consumed data does not match the issued contract."); }
            return header;
        }

        private static ReplayHeader FreezeHeader(ReplayHeader header)
        {
            if (header?.Options?.Run is null || header.FormatVersion != ReplayCodec.WorldCombatVersion ||
                header.Options.WorldCombat?.Snapshot?.Mode != WorldCombatMode.DirectPve || header.Options.HostRewards is not null)
            { throw new ArgumentException("A direct format9 contract without host rewards is required."); }
            using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, header); bytes.Position = 0;
            using var reader = new BinaryReader(bytes, new UTF8Encoding(false, true), true);
            return ReplayCodec.ReadHeader(reader);
        }

        private static void RequireSameHeader(ReplayHeader issued, ReplayHeader candidate)
        {
            var frozen = FreezeHeader(candidate);
            using var expected = new MemoryStream(); ReplayCodec.WriteHeader(expected, issued);
            using var actual = new MemoryStream(); ReplayCodec.WriteHeader(actual, frozen);
            if (!expected.ToArray().SequenceEqual(actual.ToArray()))
            { throw Failure("contract-mismatch", "The submitted header differs from the authoritative contract."); }
        }

        private static InteractiveSession Restore(ContentCatalog catalog, string actualDataHash, ReplayHeader header,
            byte[]? acceptedRecovery, out List<ReplayCommand> commands)
        {
            if (catalog is null) { throw Failure("invalid-contract", "The consumed catalog is required."); }
            if (acceptedRecovery is null)
            {
                commands = new List<ReplayCommand>();
                try { return new InteractiveSession(catalog, header.Options); }
                catch (Exception exception) when (InputFailure(exception))
                { throw Failure("invalid-contract", "The contract does not match the consumed catalog.", exception); }
            }
            try
            {
                if (acceptedRecovery.Length == 0 || acceptedRecovery.Length > WorldRecoveryCodec.MaximumBytes)
                { throw new InvalidDataException("Invalid recovery size."); }
                var bytes = (byte[])acceptedRecovery.Clone();
                var document = WorldRecoveryCodec.Decode(bytes);
                RequireSameHeader(header, document.Header);
                foreach (var command in document.Commands) { ValidatePlayerCommand(command); }
                var session = WorldRecoveryCodec.Restore(catalog, actualDataHash, bytes);
                commands = new List<ReplayCommand>(document.Commands);
                return session;
            }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-prefix", "The accepted prefix could not be reproduced.", exception); }
        }

        private static void ValidatePlayerCommand(ReplayCommand command)
        {
            ReplayCodec.ValidateCommand(command);
            if (command.Kind is ReplayCommandKind.SetInvulnerable or ReplayCommandKind.SetSpawnPermille or
                ReplayCommandKind.GrantLevel or ReplayCommandKind.HostGrantRerollV1 or ReplayCommandKind.HostReviveV1)
            { throw new ArgumentException("Server world input accepts only player commands."); }
        }

        private static void Apply(InteractiveSession session, ReplayCommand command)
        {
            try { session.Apply(command); }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-command", "The command is not valid at this frontier.", exception); }
        }

        private static WorldInputFrontier Capture(InteractiveSession session, IReadOnlyList<ReplayCommand> commands,
            IReadOnlyList<ReplayCommand> appended)
        {
            try
            {
                var recovery = WorldRecoveryCodec.Capture(session, commands);
                return new(recovery, session.NextSequence, session.Simulation.World.Tick, session.ComputeStateHash(),
                    Array.AsReadOnly(appended.ToArray()), session.State.WorldCombat!.Terminal is not null ? session.GetWorldCombatResult() : null);
            }
            catch (Exception exception) when (InputFailure(exception))
            { throw Failure("invalid-prefix", "The resulting frontier could not be reproduced.", exception); }
        }

        private static bool InputFailure(Exception exception) => exception is ArgumentException or InvalidDataException or
            InvalidOperationException or OverflowException or EndOfStreamException;

        private static WorldCombatApiException Failure(string code, string message, Exception? exception = null) => new(code, message, exception);
    }
}
