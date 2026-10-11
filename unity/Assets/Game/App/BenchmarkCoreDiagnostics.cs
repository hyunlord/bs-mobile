using System;
using System.Diagnostics;
using System.IO;
using SowSiege.Core;

namespace Game.App
{
    // Host-owned, opt-in measurements. Nested stage durations must not be summed.
    public sealed class BenchmarkCoreDiagnostics : IRuntimeDiagnostics
    {
        const int Stages = 5;
        readonly long[] starts = new long[Stages], allocationStarts = new long[Stages];
        readonly long[] elapsed, allocations, tickElapsed, tickAllocations;
        readonly int[] ticks, frames;
        readonly bool allocationAvailable;
        long tickStart, tickAllocationStart;
        bool active;
        public int Count { get; private set; }
        public int Capacity => ticks.Length;
        public BenchmarkCoreDiagnostics(int capacity, bool allocationAvailable)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.allocationAvailable = allocationAvailable;
            ticks = new int[capacity]; frames = new int[capacity];
            tickElapsed = new long[capacity]; tickAllocations = new long[capacity];
            elapsed = new long[checked(capacity * Stages)]; allocations = new long[elapsed.Length];
        }
        public void BeginTick(int tick, int frame)
        {
            if (active || Count == Capacity) throw new InvalidOperationException("Core tick measurement capacity or nesting violation.");
            ticks[Count] = tick; frames[Count] = frame; active = true;
            tickAllocationStart = allocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            tickStart = Stopwatch.GetTimestamp();
        }
        public void EndTick()
        {
            if (!active) throw new InvalidOperationException("No measured Core tick.");
            tickElapsed[Count] = Stopwatch.GetTimestamp() - tickStart;
            tickAllocations[Count] = allocationAvailable ? GC.GetAllocatedBytesForCurrentThread() - tickAllocationStart : -1;
            active = false; Count++;
        }
        public void Begin(RuntimeStage stage)
        {
            if (!active) return;
            var index = (int)stage;
            allocationStarts[index] = allocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            starts[index] = Stopwatch.GetTimestamp();
        }
        public void End(RuntimeStage stage)
        {
            if (!active) return;
            var index = (int)stage; var offset = Count * Stages + index;
            elapsed[offset] += Stopwatch.GetTimestamp() - starts[index];
            if (allocationAvailable) allocations[offset] += GC.GetAllocatedBytesForCurrentThread() - allocationStarts[index];
        }
        public void WriteCsv(TextWriter writer)
        {
            writer.WriteLine("frame,tick,advanceMs,advanceAllocatedBytes,enemyMs,separationMs,residualMs,workMs,attackMs,enemyAllocatedBytes,separationAllocatedBytes,residualAllocatedBytes,workAllocatedBytes,attackAllocatedBytes");
            for (var row = 0; row < Count; row++)
            {
                writer.Write(FormattableString.Invariant($"{frames[row]},{ticks[row]},{tickElapsed[row] * 1000d / Stopwatch.Frequency:R},{tickAllocations[row]}"));
                for (var stage = 0; stage < Stages; stage++) writer.Write(FormattableString.Invariant($",{elapsed[row * Stages + stage] * 1000d / Stopwatch.Frequency:R}"));
                for (var stage = 0; stage < Stages; stage++) writer.Write(FormattableString.Invariant($",{(allocationAvailable ? allocations[row * Stages + stage] : -1)}"));
                writer.WriteLine();
            }
        }
    }
}
