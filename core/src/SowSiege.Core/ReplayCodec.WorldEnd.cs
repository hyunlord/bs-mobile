using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SowSiege.Core
{
    public static partial class ReplayCodec
    {
        private const int WorldEndTag = 4;
        private const int MaximumWorldEndBytes = 1048576;
        private const int MaximumWorldResultEntries = 4096;

        public static void WriteWorldEnd(Stream output, WorldReplayEnd end)
        {
            ValidateWorldResult(end);
            using var payload = new MemoryStream();
            using (var w = Writer(payload))
            {
                Text(w, "world-end-v1"); w.Write(1); w.Write(end.AppliedCommands);
                var result = end.Result;
                Text(w, result.ContractHash); w.Write((int)result.Mode); Text(w, result.RulesHash); Text(w, result.ContentHash);
                w.Write((int)result.Terminal); w.Write(result.TickOrRound);
                w.Write(result.Objectives.Count);
                foreach (var objective in result.Objectives) { Text(w, objective.Id); w.Write(objective.Progress); w.Write(objective.Completed); }
                w.Write(result.Troops.Count);
                foreach (var troop in result.Troops) { Text(w, troop.Id); w.Write(troop.N); w.Write(troop.K); }
                Text(w, result.StateHash);
            }
            if (payload.Length > MaximumWorldEndBytes) { throw new ArgumentException("World end payload too large."); }
            using var writer = Writer(output); writer.Write(WorldEndTag); WriteWorldBlock(writer, payload.ToArray());
        }

        private static WorldReplayEnd ReadWorldEnd(BinaryReader reader)
        {
            using var payload = new MemoryStream(ReadWorldBlock(reader, MaximumWorldEndBytes), false);
            using var r = new BinaryReader(payload, Utf8, true);
            if (RequiredText(r) != "world-end-v1" || r.ReadInt32() != 1) { throw new InvalidDataException("Unsupported world end schema."); }
            var sequence = r.ReadInt64();
            var contract = RequiredText(r); var mode = (WorldCombatMode)r.ReadInt32();
            var rules = RequiredText(r); var content = RequiredText(r);
            var terminal = (WorldTerminalReason)r.ReadInt32(); var tick = r.ReadInt32();
            var objectives = new WorldObjectiveResult[ReadWorldResultCount(r)];
            for (var i = 0; i < objectives.Length; i++) { objectives[i] = new(RequiredText(r), r.ReadInt32(), Boolean(r)); }
            var troops = new WorldTroopResult[ReadWorldResultCount(r)];
            for (var i = 0; i < troops.Length; i++) { troops[i] = new(RequiredText(r), r.ReadInt32(), r.ReadInt32()); }
            var hash = RequiredText(r);
            if (payload.Position != payload.Length) { throw new InvalidDataException("Trailing world end bytes."); }
            var end = new WorldReplayEnd(sequence, new(contract, mode, rules, content, terminal, tick,
                Array.AsReadOnly(objectives), Array.AsReadOnly(troops), hash));
            ValidateWorldResult(end); return end;
        }

        private static int ReadWorldResultCount(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > MaximumWorldResultEntries || count > reader.BaseStream.Length - reader.BaseStream.Position)
            { throw new InvalidDataException("Invalid world result count."); }
            return count;
        }

        private static void ValidateWorldResult(WorldReplayEnd end)
        {
            if (end is null || end.Result is null || end.AppliedCommands < 0 || end.AppliedCommands > MaximumCommands) { throw new ArgumentException("Invalid world terminal sequence."); }
            var result = end.Result;
            WorldCanonicalHash(result.ContractHash); WorldCanonicalHash(result.RulesHash); WorldCanonicalHash(result.ContentHash); WorldCanonicalHash(result.StateHash);
            if (!Enum.IsDefined(typeof(WorldCombatMode), result.Mode) || !Enum.IsDefined(typeof(WorldTerminalReason), result.Terminal) || result.TickOrRound < 0)
            { throw new ArgumentException("Invalid world terminal identity."); }
            if (result.Objectives is null || result.Troops is null || result.Objectives.Count > MaximumWorldResultEntries || result.Troops.Count > MaximumWorldResultEntries)
            { throw new ArgumentException("Invalid world result arrays."); }
            string? previous = null;
            foreach (var objective in result.Objectives)
            {
                if (objective is null || objective.Progress < 0) { throw new ArgumentException("Invalid objective progress."); }
                WorldResultId(objective.Id, previous); previous = objective.Id;
            }
            previous = null;
            foreach (var troop in result.Troops)
            {
                if (troop is null || troop.N < 0 || troop.K < 0 || troop.K > troop.N) { throw new ArgumentException("Invalid N/K."); }
                WorldResultId(troop.Id, previous); previous = troop.Id;
            }
        }

        private static void WorldCanonicalHash(string value)
        {
            ValidateHash(value);
            if (value.Any(c => c >= 'a' && c <= 'f')) { throw new ArgumentException("World hashes must use uppercase canonical hex."); }
        }

        private static void WorldResultId(string id, string? previous)
        {
            if (string.IsNullOrEmpty(id) || Utf8.GetByteCount(id) > MaximumStringBytes || id.Any(c => c > '\u007F' || char.IsControl(c)) || previous is not null && StringComparer.Ordinal.Compare(previous, id) >= 0)
            { throw new ArgumentException("Noncanonical or unordered result identifier."); }
        }

        public static void ValidateWorldEnd(ReplayHeader header, WorldReplayEnd end)
        {
            ValidateHeader(header); ValidateWorldResult(end);
            var input = header.Options.WorldCombat?.Snapshot ?? throw new ArgumentException("World end requires format9.");
            var result = end.Result;
            if (result.ContractHash != input.ContractHash || result.Mode != input.Mode || result.RulesHash != input.Provenance.RulesHash || result.ContentHash != input.Provenance.ContentHash)
            { throw new ArgumentException("World result provenance mismatch."); }
            if (result.Troops.Count != input.Troops.Count || result.Objectives.Count != input.Objectives.Count) { throw new ArgumentException("World result dimensions mismatch."); }
            for (var i = 0; i < input.Troops.Count; i++)
            {
                if (result.Troops[i].Id != input.Troops[i].Id || result.Troops[i].N != input.Troops[i].N) { throw new ArgumentException("World result N differs from the issued snapshot."); }
            }
            var limit = input.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
            if (input.Mode == WorldCombatMode.DirectPve)
            {
                if (result.TickOrRound > limit || (int)result.Terminal >= (int)WorldTerminalReason.AutoVictory || result.Terminal == WorldTerminalReason.Timeout && result.TickOrRound != limit)
                { throw new ArgumentException("World direct terminal conflicts with the time limit or mode."); }
                if (result.Terminal == WorldTerminalReason.Quit && result.TickOrRound == limit)
                { throw new ArgumentException("The limit frontier already has a terminal outcome before a later quit."); }
                if (result.Terminal == WorldTerminalReason.RequiredMarkerDestroyed && !input.Markers.Any(m => m.Required))
                { throw new ArgumentException("Required-marker failure needs an issued required marker."); }
            }
            for (var i = 0; i < input.Objectives.Count; i++)
            {
                var expected = input.Objectives[i]; var actual = result.Objectives[i];
                var maximum = expected.Kind == WorldObjectiveKind.DefeatDesignated ? expected.RequiredCount : expected.Kind == WorldObjectiveKind.HoldMarker ? expected.HoldTicks : expected.LimitTicks;
                if (actual.Id != expected.Id || actual.Progress > maximum || actual.Completed != (actual.Progress == maximum))
                { throw new ArgumentException("World objective result conflicts with its contract."); }
                if (input.Mode == WorldCombatMode.DirectPve && expected.Kind == WorldObjectiveKind.TimeLimit && actual.Progress != result.TickOrRound)
                { throw new ArgumentException("World time-limit progress must equal the terminal frontier tick."); }
                if (input.Mode == WorldCombatMode.DirectPve && expected.Kind == WorldObjectiveKind.HoldMarker && actual.Progress > result.TickOrRound)
                { throw new ArgumentException("World continuous hold cannot exceed elapsed ticks."); }
                if (result.Terminal == WorldTerminalReason.ObjectivesCompleted && expected.Kind != WorldObjectiveKind.TimeLimit && !actual.Completed)
                { throw new ArgumentException("World success requires every gameplay objective."); }
            }
            if (input.Mode == WorldCombatMode.DirectPve && (result.Terminal is WorldTerminalReason.Quit or WorldTerminalReason.Timeout) &&
                input.Objectives.Where(o => o.Kind != WorldObjectiveKind.TimeLimit).All(o => result.Objectives.Any(r => r.Id == o.Id && r.Completed)))
            { throw new ArgumentException("Completed objectives take precedence over quit or timeout."); }
        }

        public static WorldReplayDocument ReadWorld(Stream input)
        {
            try
            {
                using var r = new BinaryReader(input, Utf8, true);
                var header = ReadHeader(r);
                if (header.FormatVersion != WorldCombatVersion || header.Options.WorldCombat!.Snapshot.Mode != WorldCombatMode.DirectPve)
                { throw new InvalidDataException("Expected a direct world replay."); }
                var limit = header.Options.WorldCombat.Snapshot.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).LimitTicks;
                var commands = new List<ReplayCommand>(); var checkpoints = new List<ReplayCheckpoint>(); var tick = 0;
                while (true)
                {
                    var tag = r.ReadInt32();
                    if (tag == CommandTag)
                    {
                        if (commands.Count >= MaximumCommands || tick >= limit) { throw new InvalidDataException("World command limit exceeded."); }
                        var command = new ReplayCommand(r.ReadInt64(), r.ReadInt32(), (ReplayCommandKind)r.ReadInt32(), new(r.ReadInt16(), r.ReadInt16()), Text(r), r.ReadInt32());
                        ValidateCommand(command);
                        if (command.Sequence != commands.Count || command.Tick != tick) { throw new InvalidDataException("World command order mismatch."); }
                        if (command.Kind is ReplayCommandKind.SetInvulnerable or ReplayCommandKind.SetSpawnPermille or ReplayCommandKind.GrantLevel)
                        { throw new InvalidDataException("World replay cannot contain review commands."); }
                        if (command.Kind == ReplayCommandKind.HostGrantRerollV1 && header.Options.HostRewards?.AllowRerollGrant != true ||
                            command.Kind == ReplayCommandKind.HostReviveV1 && header.Options.HostRewards?.Revive is null)
                        { throw new InvalidDataException("Host command without issued host rules."); }
                        commands.Add(command); if (command.Kind == ReplayCommandKind.Advance) { tick = checked(tick + 1); }
                    }
                    else if (tag == CheckpointTag)
                    {
                        var checkpoint = new ReplayCheckpoint(r.ReadInt64(), r.ReadInt32(), RequiredText(r)); WorldCanonicalHash(checkpoint.StateHash);
                        if (checkpoint.AppliedCommands != commands.Count || checkpoint.Tick != tick || checkpoints.Count > 0 && checkpoint.AppliedCommands <= checkpoints[checkpoints.Count - 1].AppliedCommands)
                        { throw new InvalidDataException("World checkpoint order mismatch."); }
                        checkpoints.Add(checkpoint);
                    }
                    else if (tag == WorldEndTag)
                    {
                        var end = ReadWorldEnd(r); ValidateWorldEnd(header, end);
                        if (end.AppliedCommands != commands.Count || end.Result.TickOrRound != tick || input.ReadByte() != -1) { throw new InvalidDataException("World terminal frontier or trailing bytes mismatch."); }
                        return new(header, Array.AsReadOnly(commands.ToArray()), Array.AsReadOnly(checkpoints.ToArray()), end);
                    }
                    else { throw new InvalidDataException("Unknown world replay tag."); }
                }
            }
            catch (Exception ex) when (ex is EndOfStreamException || ex is ArgumentException || ex is OverflowException)
            { throw new InvalidDataException("Malformed world replay.", ex); }
        }
    }
}
