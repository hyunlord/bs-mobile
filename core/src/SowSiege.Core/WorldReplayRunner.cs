using System;
using System.IO;
using System.Linq;

namespace SowSiege.Core
{
    public static class WorldReplayRunner
    {
        public static WorldCombatResult Verify(ContentCatalog catalog, string actualDataHash, WorldReplayDocument replay)
        {
            try { return VerifyCore(catalog, actualDataHash, replay); }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OverflowException)
            { throw new InvalidDataException("World replay reexecution failed.", exception); }
        }
        private static WorldCombatResult VerifyCore(ContentCatalog catalog, string actualDataHash, WorldReplayDocument replay)
        {
            if (catalog is null || replay is null) { throw new ArgumentNullException(nameof(catalog)); }
            using var wire = new MemoryStream(); ReplayCodec.WriteHeader(wire, replay.Header);
            var checkpoints = replay.Checkpoints.ToDictionary(c => c.AppliedCommands);
            if (checkpoints.TryGetValue(0, out var initial)) { ReplayCodec.WriteCheckpoint(wire, initial); }
            foreach (var command in replay.Commands)
            {
                ReplayCodec.WriteCommand(wire, command);
                if (checkpoints.TryGetValue(command.Sequence + 1, out var checkpoint)) { ReplayCodec.WriteCheckpoint(wire, checkpoint); }
            }
            if (checkpoints.Keys.Any(key => key < 0 || key > replay.Commands.Count)) { throw new InvalidDataException("World checkpoint outside the prefix."); }
            ReplayCodec.WriteWorldEnd(wire, replay.End); wire.Position = 0;
            var validated = ReplayCodec.ReadWorld(wire);
            if (actualDataHash != validated.Header.Options.DataHash) { throw new InvalidDataException("World consumed data identity mismatch."); }
            var session = new InteractiveSession(catalog, validated.Header.Options);
            void Check()
            {
                if (checkpoints.TryGetValue(session.NextSequence, out var checkpoint) &&
                    (checkpoint.Tick != session.Simulation.World.Tick || checkpoint.StateHash != session.ComputeStateHash()))
                { throw new InvalidDataException("World checkpoint differs from reexecution."); }
            }
            var quits = validated.End.Result.Terminal == WorldTerminalReason.Quit;
            if (!quits || validated.Commands.Count != 0) { Check(); }
            foreach (var command in validated.Commands)
            {
                session.Apply(command);
                if (!quits || session.NextSequence != validated.Commands.Count) { Check(); }
            }
            if (quits) { session.QuitWorldCombat(); Check(); }
            WorldCombatResult actual;
            try { actual = session.GetWorldCombatResult(); }
            catch (InvalidOperationException exception) { throw new InvalidDataException("World prefix has no terminal result.", exception); }
            using var actualBytes = new MemoryStream(); ReplayCodec.WriteWorldEnd(actualBytes, new(session.NextSequence, actual));
            using var claimedBytes = new MemoryStream(); ReplayCodec.WriteWorldEnd(claimedBytes, validated.End);
            if (!actualBytes.ToArray().SequenceEqual(claimedBytes.ToArray())) { throw new InvalidDataException("World terminal result differs from reexecution."); }
            return actual;
        }
    }
}
