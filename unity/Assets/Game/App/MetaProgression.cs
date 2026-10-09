#nullable enable
#pragma warning disable IDE0161 // Unity C# 9 requires block-scoped namespaces.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SowSiege.Core;

namespace Game.App
{
    public sealed class MetaStartedRun
    {
        public MetaRunPlan Plan { get; }
        public ContentCatalog Catalog { get; }
        public byte[] ReplayContext { get; }
        internal MetaStartedRun(MetaRunPlan plan, ContentCatalog catalog, byte[] context)
        { Plan = plan; Catalog = catalog; ReplayContext = context; }
    }

    public sealed class MetaProgression
    {
        private readonly AtomicSaveStore store;
        private readonly AtomicSaveStore terminalStore;
        private readonly ContentCatalog source;
        private readonly string dataHash;
        private readonly Func<long> monotonicSeconds;
        private readonly Func<long> wallSeconds;
        private MetaState state;
        public MetaCatalog Catalog { get; }
        public MetaState State => Copy(state);
        public AtomicSaveLoadStatus LoadStatus { get; }
        public bool RecoveredAbandonedRun { get; }
        public bool RecoveredTerminalRun { get; }
        public MetaIdleResult? LastIdle { get; private set; }

        public MetaProgression(MetaCatalog meta, ContentCatalog baseCatalog, string baseDataHash, string directory,
            Func<long>? monotonicSeconds = null, Func<long>? wallSeconds = null)
        {
            Catalog = meta ?? throw new ArgumentNullException(nameof(meta));
            source = baseCatalog ?? throw new ArgumentNullException(nameof(baseCatalog));
            dataHash = baseDataHash;
            this.monotonicSeconds = monotonicSeconds ?? (() => Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            this.wallSeconds = wallSeconds ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            store = new AtomicSaveStore(directory, "progress.bin");
            terminalStore = new AtomicSaveStore(directory, "terminal.bin", 2097160);
            var loaded = store.Load(Validate);
            LoadStatus = loaded.Status;
            state = loaded.Payload == null ? MetaEngine.NewGame(meta) : MetaSaveCodec.Decode(loaded.Payload).State!;
            var terminal = terminalStore.Load(ValidateTerminal);
            if (terminal.Payload != null)
            {
                var snapshots = ReadTerminal(terminal.Payload);
                var before = MetaSaveCodec.Decode(snapshots.Before).State!;
                var after = MetaSaveCodec.Decode(snapshots.After).State!;
                if (state.PendingRun != null && state.PendingRun == before.PendingRun)
                {
                    if (!MetaSaveCodec.Encode(state).SequenceEqual(snapshots.Before))
                    {
                        throw new InvalidDataException("Terminal intent does not match the pending state.");
                    }
                    Publish(after);
                    RecoveredTerminalRun = true;
                }
                else if (after.NextRunSequence > state.NextRunSequence)
                {
                    throw new InvalidDataException("Terminal intent is ahead of available progress; reset is forbidden.");
                }
            }
            if (state.PendingRun != null)
            {
                state = MetaEngine.SettleRun(Catalog, state, state.PendingRun, AbandonedFacts()).State;
                RecoveredAbandonedRun = true;
            }
            var idle = MetaEngine.AdvanceIdle(Catalog, state, this.monotonicSeconds(), this.wallSeconds());
            Publish(idle.State);
            LastIdle = idle;
        }

        public MetaStartedRun BeginRun(string chapterId, int seed)
        {
            var plan = MetaEngine.BeginRun(Catalog, state, chapterId, seed);
            var effective = MetaRunAdapter.ProjectCatalog(source, Catalog, plan.State, plan);
            var context = MetaReplayContext.Encode(plan, dataHash, effective);
            Publish(plan.State);
            return new MetaStartedRun(new MetaRunPlan(Copy(plan.State), plan.Run), effective, context);
        }

        public MetaSettlement SettleRun(MetaRunFacts facts)
        {
            var pending = state.PendingRun ?? throw new InvalidOperationException("No pending run.");
            var settlement = MetaEngine.SettleRun(Catalog, state, pending, facts);
            byte[] intent = WriteTerminal(state, settlement.State);
            var recorded = terminalStore.Load(ValidateTerminal);
            if (recorded.Payload != null)
            {
                var prior = ReadTerminal(recorded.Payload);
                if (MetaSaveCodec.Decode(prior.Before).State!.PendingRun == pending && !recorded.Payload.SequenceEqual(intent))
                {
                    throw new InvalidOperationException("Terminal result already recorded; retry its original facts.");
                }
            }
            terminalStore.Save(intent, ValidateTerminal);
            Publish(settlement.State);
            return settlement;
        }

        public MetaSettlement AbandonRun() => SettleRun(AbandonedFacts());
        public MetaIdleResult AdvanceIdle()
        {
            RequireMenu();
            var result = MetaEngine.AdvanceIdle(Catalog, state, monotonicSeconds(), wallSeconds());
            Publish(result.State); LastIdle = result;
            return result;
        }
        public void SetManorPriority(string id) { RequireMenu(); Publish(MetaEngine.SetManorPriority(Catalog, state, id)); }
        public void UpgradeVassal(string id) { RequireMenu(); Publish(MetaEngine.UpgradeVassal(Catalog, state, id)); }
        public void RankUpVassal(string id) { RequireMenu(); Publish(MetaEngine.RankUpVassal(Catalog, state, id)); }
        public void ToggleVassal(string id) { RequireMenu(); Publish(MetaEngine.ToggleVassal(Catalog, state, id)); }

        private void RequireMenu()
        {
            if (state.PendingRun != null) { throw new InvalidOperationException("Meta actions are unavailable during a pending run."); }
        }
        private MetaRunFacts AbandonedFacts() => new MetaRunFacts(0, source.Tuning.DurationTicks, false, false, true, 0, 0, 0, 0, 0, 0);
        private void Publish(MetaState next)
        {
            byte[] bytes = MetaSaveCodec.Encode(next);
            store.Save(bytes, Validate);
            state = MetaSaveCodec.Decode(bytes).State!;
        }
        private AtomicSaveValidation Validate(byte[] bytes)
        {
            var decoded = MetaSaveCodec.Decode(bytes);
            if (decoded.ErrorCode == "unsupported-version") { return AtomicSaveValidation.UnsupportedVersion; }
            return decoded.Valid && decoded.State != null && MetaValidation.ValidateState(Catalog, decoded.State).Length == 0
                ? AtomicSaveValidation.Valid : AtomicSaveValidation.Corrupt;
        }
        // Keep the last intent after commit: its sequence makes recovery idempotent, even if cleanup fails.
        private static byte[] WriteTerminal(MetaState before, MetaState after)
        {
            byte[] first = MetaSaveCodec.Encode(before), second = MetaSaveCodec.Encode(after);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(first.Length); writer.Write(first); writer.Write(second.Length); writer.Write(second);
            return stream.ToArray();
        }
        private static (byte[] Before, byte[] After) ReadTerminal(byte[] bytes)
        {
            using var stream = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(stream);
            byte[] Read()
            {
                int count = reader.ReadInt32();
                if (count < 44 || count > 1048576 || count > stream.Length - stream.Position)
                {
                    throw new InvalidDataException("Invalid terminal snapshot length.");
                }
                return reader.ReadBytes(count);
            }
            byte[] before = Read(), after = Read();
            if (stream.Position != stream.Length) { throw new InvalidDataException("Trailing terminal bytes."); }
            return (before, after);
        }
        private AtomicSaveValidation ValidateTerminal(byte[] bytes)
        {
            try
            {
                var snapshots = ReadTerminal(bytes);
                var first = Validate(snapshots.Before); var second = Validate(snapshots.After);
                if (first == AtomicSaveValidation.UnsupportedVersion || second == AtomicSaveValidation.UnsupportedVersion)
                { return AtomicSaveValidation.UnsupportedVersion; }
                if (first != AtomicSaveValidation.Valid || second != AtomicSaveValidation.Valid)
                { return AtomicSaveValidation.Corrupt; }
                var before = MetaSaveCodec.Decode(snapshots.Before).State!;
                var after = MetaSaveCodec.Decode(snapshots.After).State!;
                return before.PendingRun != null && after.PendingRun == null && before.NextRunSequence == after.NextRunSequence &&
                    before.CompletedRuns < long.MaxValue && after.CompletedRuns == before.CompletedRuns + 1
                    ? AtomicSaveValidation.Valid : AtomicSaveValidation.Corrupt;
            }
            catch (IOException) { return AtomicSaveValidation.Corrupt; }
        }
        private static MetaState Copy(MetaState value) => MetaSaveCodec.Decode(MetaSaveCodec.Encode(value)).State!;
    }
}
