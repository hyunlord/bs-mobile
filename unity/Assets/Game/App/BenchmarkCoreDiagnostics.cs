using System;
using System.Diagnostics;
using System.IO;
using SowSiege.Core;

namespace Game.App
{
    // Host-owned, opt-in measurements. Nested stage durations must not be summed.
    public sealed class BenchmarkCoreDiagnostics : IRuntimeDiagnostics
    {
        const int Stages = 10;
        const int LegacyStages = 5;
        readonly long[] starts = new long[Stages], allocationStarts = new long[Stages];
        readonly long[] elapsed, allocations, tickElapsed, tickAllocations;
        readonly int[] ticks, frames;
        readonly long[] tickStarts, tickEnds;
        readonly double[] snapshotMilliseconds;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        readonly bool profilerMarkers;
        static readonly Unity.Profiling.ProfilerMarker[] StageMarkers = {
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Enemy"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Separation"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Residual"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Work"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Attack"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.MovementAiContact"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Collect"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Harvest"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Spawn"),
            new Unity.Profiling.ProfilerMarker("SowSiege.Core.Clear")
        };
#endif
        readonly bool allocationAvailable;
        long tickStart, tickAllocationStart;
        bool active;
        public int Count { get; private set; }
        public int Capacity => ticks.Length;
        public BenchmarkCoreDiagnostics(int capacity, bool allocationAvailable, bool profilerMarkers = false)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.allocationAvailable = allocationAvailable;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            this.profilerMarkers = profilerMarkers;
#endif
            tickStarts = new long[capacity]; tickEnds = new long[capacity];
            snapshotMilliseconds = new double[capacity];
            ticks = new int[capacity]; frames = new int[capacity];
            tickElapsed = new long[capacity]; tickAllocations = new long[capacity];
            elapsed = new long[checked(capacity * Stages)]; allocations = new long[elapsed.Length];
        }
        public void BeginTick(int tick, int frame)
        {
            if (active || Count == Capacity) throw new InvalidOperationException("Core tick measurement capacity or nesting violation.");
            ticks[Count] = tick; frames[Count] = frame; active = true;
            tickAllocationStart = allocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            tickStart = Stopwatch.GetTimestamp(); tickStarts[Count] = tickStart;
        }
        public void EndTick()
        {
            if (!active) throw new InvalidOperationException("No measured Core tick.");
            tickEnds[Count] = Stopwatch.GetTimestamp();
            tickElapsed[Count] = tickEnds[Count] - tickStart;
            tickAllocations[Count] = allocationAvailable ? GC.GetAllocatedBytesForCurrentThread() - tickAllocationStart : -1;
            active = false; Count++;
        }
        public void RecordSnapshot(int tick, int frame, double milliseconds)
        {
            if (active || Count == 0 || ticks[Count - 1] != tick || frames[Count - 1] != frame || milliseconds < 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
                throw new InvalidOperationException("Snapshot must follow its measured Advance.");
            snapshotMilliseconds[Count - 1] += milliseconds;
        }
        public void Begin(RuntimeStage stage)
        {
            if (!active) return;
            var index = (int)stage;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (profilerMarkers) StageMarkers[index].Begin();
#endif
            allocationStarts[index] = allocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            starts[index] = Stopwatch.GetTimestamp();
        }
        public void End(RuntimeStage stage)
        {
            if (!active) return;
            var index = (int)stage; var offset = Count * Stages + index;
            elapsed[offset] += Stopwatch.GetTimestamp() - starts[index];
            if (allocationAvailable) allocations[offset] += GC.GetAllocatedBytesForCurrentThread() - allocationStarts[index];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (profilerMarkers) StageMarkers[index].End();
#endif
        }
        public void WriteCsv(TextWriter writer)
        {
            writer.WriteLine("frame,tick,advanceMs,advanceAllocatedBytes,enemyMs,separationMs,residualMs,workMs,attackMs,enemyAllocatedBytes,separationAllocatedBytes,residualAllocatedBytes,workAllocatedBytes,attackAllocatedBytes,snapshotMs,advanceStartTimestamp,advanceEndTimestamp,movementAiContactMs,movementAiContactAllocatedBytes,collectMs,collectAllocatedBytes,harvestMs,harvestAllocatedBytes,spawnMs,spawnAllocatedBytes,clearMs,clearAllocatedBytes");
            for (var row = 0; row < Count; row++)
            {
                writer.Write(FormattableString.Invariant($"{frames[row]},{ticks[row]},{tickElapsed[row] * 1000d / Stopwatch.Frequency:R},{tickAllocations[row]}"));
                for (var stage = 0; stage < LegacyStages; stage++) writer.Write(FormattableString.Invariant($",{elapsed[row * Stages + stage] * 1000d / Stopwatch.Frequency:R}"));
                for (var stage = 0; stage < LegacyStages; stage++) writer.Write(FormattableString.Invariant($",{(allocationAvailable ? allocations[row * Stages + stage] : -1)}"));
                writer.Write(FormattableString.Invariant($",{snapshotMilliseconds[row]:R},{tickStarts[row]},{tickEnds[row]}"));
                for (var stage = (int)RuntimeStage.Movement; stage < Stages; stage++)
                    writer.Write(FormattableString.Invariant($",{elapsed[row * Stages + stage] * 1000d / Stopwatch.Frequency:R},{(allocationAvailable ? allocations[row * Stages + stage] : -1)}"));
                writer.WriteLine();
            }
        }
    }
}
