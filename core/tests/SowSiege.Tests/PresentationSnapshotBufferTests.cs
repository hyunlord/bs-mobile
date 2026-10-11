using System.Runtime.CompilerServices;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SowSiege.Tests;

[CollectionDefinition(nameof(SnapshotAllocationCollection), DisableParallelization = true)]
public sealed class SnapshotAllocationCollection { }

[Collection(nameof(SnapshotAllocationCollection))]
public sealed class PresentationSnapshotBufferTests
{
    private readonly ITestOutputHelper output;
    public PresentationSnapshotBufferTests(ITestOutputHelper output) { this.output = output; }
    private static InteractiveSession Session(int count)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
        var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64)));
        Populate(session, count); return session;
    }
    private static void Populate(InteractiveSession session, int count)
    {
        var world = session.Simulation.World; world.Enemies.Clear(); world.WaveRuntime!.EnemyActions.Clear();
        var definition = session.Catalog.Enemies.Keys.First();
        for (var id = count; id > 0; id--)
        {
            world.Enemies.Add(new() { Id = id, Definition = definition, Position = new(id, id), Health = 10 });
            world.WaveRuntime.EnemyActions.Add(id, new() { Phase = "approach", Origin = new(id, 0), Target = new(0, id), UntilTick = id });
        }
    }

    [Fact]
    public void BorrowedAndDetachedValuesMatchWithoutChangingStateOrOrdering()
    {
        var session = Session(17); var before = session.ComputeStateHash();
        var detached = session.View.CaptureFrame(); var borrowed = ((IBufferedRunView)session.View).CaptureFrame(new(32));
        Assert.Equal(detached.Enemies.ToArray(), borrowed.Enemies.ToArray()); Assert.Equal(detached.Counts, borrowed.Counts);
        Assert.Equal(17, borrowed.Enemies[0].Id); Assert.Equal(17, borrowed.Enemies.Count);
        var detachedWave = ((IWaveRunView)session.View).CaptureWaveRuntime()!;
        var borrowedWave = ((IBufferedWaveRunView)session.View).CaptureWaveRuntime(new(32))!;
        Assert.Equal(detachedWave.Enemies.ToArray(), borrowedWave.Enemies.ToArray());
        Assert.Equal(1, borrowedWave.Enemies[0].Id); Assert.Equal(17, borrowedWave.Enemies.Count);
        Assert.Equal(before, session.ComputeStateHash());
        Assert.Throws<NotSupportedException>(() => ((IList<EnemyView>)detached.Enemies).Clear());
    }

    [Fact]
    public void TwoBuffersRetainPreviousFrameAndDetachedSnapshotsNeverBorrowStorage()
    {
        var session = Session(3); var view = (IBufferedRunView)session.View; var waveView = (IBufferedWaveRunView)session.View;
        var a = new RunSnapshotBuffer(8); var b = new RunSnapshotBuffer(8); var wa = new WaveSnapshotBuffer(8); var wb = new WaveSnapshotBuffer(8);
        var detached = session.View.CaptureFrame(); var detachedWave = waveView.CaptureWaveRuntime()!;
        var first = view.CaptureFrame(a); var firstWave = waveView.CaptureWaveRuntime(wa)!;
        var expected = first.Enemies.ToArray(); var expectedWave = firstWave.Enemies.ToArray();
        Populate(session, 2); view.CaptureFrame(b); waveView.CaptureWaveRuntime(wb);
        Assert.Equal(expected, first.Enemies.ToArray()); Assert.Equal(expectedWave, firstWave.Enemies.ToArray());
        var replacement = view.CaptureFrame(a); var replacementWave = waveView.CaptureWaveRuntime(wa)!;
        Assert.NotSame(first, replacement); Assert.NotSame(firstWave, replacementWave);
        Assert.Same(first.Enemies, replacement.Enemies); Assert.Equal(2, replacement.Enemies.Count);
        Assert.Equal(expected, detached.Enemies.ToArray()); Assert.Equal(expectedWave, detachedWave.Enemies.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => replacement.Enemies[2]);
    }

    [Fact]
    public void CapacityMustBePreparedExplicitlyAndShrinkingCountsExposeNoStaleEnemies()
    {
        var session = Session(3); var view = (IBufferedRunView)session.View; var waveView = (IBufferedWaveRunView)session.View;
        var run = new RunSnapshotBuffer(2); var wave = new WaveSnapshotBuffer(2);
        Assert.Throws<InvalidOperationException>(() => view.CaptureFrame(run));
        Assert.Throws<InvalidOperationException>(() => waveView.CaptureWaveRuntime(wave));
        Assert.Equal(2, run.EnemyCapacity); Assert.Equal(2, wave.EnemyCapacity);
        run.PrepareEnemyCapacity(3); wave.PrepareEnemyCapacity(3);
        Assert.Equal(3, view.CaptureFrame(run).Enemies.Count); Assert.Equal(3, waveView.CaptureWaveRuntime(wave)!.Enemies.Count);
        Populate(session, 0); Assert.Empty(view.CaptureFrame(run).Enemies); Assert.Empty(waveView.CaptureWaveRuntime(wave)!.Enemies);
        Assert.Equal(3, run.EnemyCapacity); Assert.Equal(3, wave.EnemyCapacity);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunSnapshotBuffer(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => wave.PrepareEnemyCapacity(-1));
    }

    [Fact]
    public void EnemySampleValueEqualityAndAbsentDefaultAreExplicit()
    {
        var enemy = new EnemyView(1, "test:enemy", new(2, 3), 4, 5);
        Assert.Equal(enemy, new EnemyView(1, "test:enemy", new(2, 3), 4, 5));
        Assert.True(enemy == new EnemyView(1, "test:enemy", new(2, 3), 4, 5));
        Assert.Null(default(EnemyView).DefinitionId);
        var wave = new WaveEnemyView(1, "approach", new(2, 3), new(4, 5), 6, true, false, 7);
        Assert.Equal(wave, new WaveEnemyView(1, "approach", new(2, 3), new(4, 5), 6, true, false, 7));
        Assert.Null(default(WaveEnemyView).Phase);
    }

    [Fact]
    public void PreparedCapturesAllocateNoAdditionalBytesPerEnemy()
    {
        var session = Session(800); var run = new RunSnapshotBuffer(1600); var wave = new WaveSnapshotBuffer(1600);
        var view = (IBufferedRunView)session.View; var waveView = (IBufferedWaveRunView)session.View;
        var run800Calls = new long[100]; var wave800Calls = new long[100];
        var run1600Calls = new long[100]; var wave1600Calls = new long[100];
        long Measure(bool includeWave, long[] calls)
        {
            for (var i = 0; i < 10; i++) { if (includeWave) { waveView.CaptureWaveRuntime(wave); } else { view.CaptureFrame(run); } }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                var callBefore = GC.GetAllocatedBytesForCurrentThread();
                if (includeWave) { waveView.CaptureWaveRuntime(wave); } else { view.CaptureFrame(run); }
                calls[i] = GC.GetAllocatedBytesForCurrentThread() - callBefore;
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var threadBefore = Environment.CurrentManagedThreadId;
        long run800, wave800, run1600, wave1600;
        // Background-GC allocation-context retirement can overcount: dotnet/runtime#134855.
        // No-GC isolates the counter, not allocations; unavailable or exhausted regions fail this test.
        Assert.True(GC.TryStartNoGCRegion(16 * 1024 * 1024));
        try
        {
            run800 = Measure(false, run800Calls); wave800 = Measure(true, wave800Calls);
            Populate(session, 1600);
            run1600 = Measure(false, run1600Calls); wave1600 = Measure(true, wave1600Calls);
        }
        finally { GC.EndNoGCRegion(); }
        var threadAfter = Environment.CurrentManagedThreadId;
        // Diagnostics run only after all allocation windows; no sample is discarded or retried.
        output.WriteLine($"Capture allocation thread {threadBefore}->{threadAfter}; warmup=10, measured=100");
        void Report(string phase, long total, long[] calls)
        {
            var histogram = string.Join(", ", calls.GroupBy(bytes => bytes).OrderBy(group => group.Key).Select(group => $"{group.Key} bytes x {group.Count()}"));
            output.WriteLine($"{phase}: aggregate={total}; per-call sum={calls.Sum()}; histogram=[{histogram}]");
        }
        Report("run800", run800, run800Calls); Report("run1600", run1600, run1600Calls);
        Report("wave800", wave800, wave800Calls); Report("wave1600", wave1600, wave1600Calls);
        Assert.Equal(run800, run1600);
        Assert.Equal(wave800, wave1600);
        Assert.True(run800 > 0); Assert.True(wave800 > 0);
    }
    [Fact]
    public void NoGcAllocationWindowStillCountsEveryRealAllocation()
    {
        var empty = new long[100]; var allocated = new long[100];
        GC.KeepAlive(AllocateControl());
        Assert.True(GC.TryStartNoGCRegion(16 * 1024 * 1024));
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                empty[i] = GC.GetAllocatedBytesForCurrentThread() - before;
                before = GC.GetAllocatedBytesForCurrentThread();
                var value = AllocateControl();
                allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
                GC.KeepAlive(value);
            }
        }
        finally { GC.EndNoGCRegion(); }
        Assert.All(empty, bytes => Assert.Equal(0, bytes));
        Assert.All(allocated, bytes => Assert.True(bytes >= 37));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object AllocateControl() => new byte[37];

    [Fact]
    public void BorrowedCountersKeepOrdinalOrderAndDetachedExportsSurviveBothBuffers()
    {
        var session = Session(3); var state = session.Simulation.World.WaveRuntime!;
        state.Counters["가"] = 1; state.Counters["a"] = 2; state.Counters["Z"] = 3;
        var view = (IBufferedWaveRunView)session.View;
        var a = new WaveSnapshotBuffer(4); var b = new WaveSnapshotBuffer(4);
        var borrowed = view.CaptureWaveRuntime(a)!;
        var detached = view.CaptureWaveRuntime()!;
        var exported = WaveSnapshotBuffer.Detach(borrowed);
        Assert.Equal(new[] { "Z", "a", "가" }, borrowed.Counters.Keys);
        Assert.Equal(new long[] { 3, 2, 1 }, borrowed.Counters.Values);
        Assert.Equal(2, borrowed.Counters["a"]);
        Assert.False(borrowed.Counters.TryGetValue("missing", out _));
        Assert.Throws<KeyNotFoundException>(() => borrowed.Counters["missing"]);
        Assert.Throws<ArgumentNullException>(() => borrowed.Counters.ContainsKey(null!));
        state.Counters.Clear(); state.Counters["new"] = 99;
        view.CaptureWaveRuntime(b);
        Assert.Equal(detached.Counters.ToArray(), borrowed.Counters.ToArray());
        Populate(session, 1); view.CaptureWaveRuntime(a);
        Assert.Single(borrowed.Counters); Assert.Equal(99, borrowed.Counters["new"]);
        Assert.False(borrowed.Counters.ContainsKey("a"));
        Assert.Equal(detached.Counters.ToArray(), exported.Counters.ToArray());
        Assert.Equal(detached.Enemies.ToArray(), exported.Enemies.ToArray());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, long>)exported.Counters).Clear());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void ImmutableWorkCacheRefreshesEveryVisibleFieldWithoutMutatingRetainedRecords(int field)
    {
        var work = new WaveWork { Id = 1, Source = "source", Kind = "grain", Position = new(10, 20), Health = 3 };
        var before = work.Capture(0);
        Assert.Same(before, work.Capture(0));
        switch (field)
        {
            case 0: work.Id++; break;
            case 1: work.Source += "x"; break;
            case 2: work.Kind = "building"; break;
            case 3: work.Position = new(20, 30); break;
            case 4: work.Progress++; break;
            case 5: work.Required++; break;
            case 6: work.Complete = true; break;
            case 7: work.Health++; break;
            case 8: work.ParentId++; break;
            case 9: work.Protected = true; break;
            case 10: work.DormantUntil = 1; break;
            case 11: work.WetUntil = 1; break;
            case 12: work.ShipmentActive = true; break;
            case 13: work.Dry = true; break;
        }
        var after = work.Capture(0);
        Assert.NotSame(before, after); Assert.NotEqual(before, after);
        Assert.Same(after, work.Capture(0));
        Assert.Equal(new WaveWorkView(1, "source", "grain", new(10, 20), 0, 0, false, 3, -1, false, false, false, false, false), before);
    }

    [Fact]
    public void WorkTickFlagsExpireWithoutChangingRulesOrOldSnapshots()
    {
        var session = Session(0); var state = session.Simulation.World.WaveRuntime!;
        state.Work.Add(new() { Id = 1, Source = "test:work", Kind = "grain", Health = 2, DormantUntil = 1, WetUntil = 1 });
        var hash = session.ComputeStateHash(); var view = (IWaveRunView)session.View;
        var before = view.CaptureWaveRuntime()!;
        Assert.Equal(hash, session.ComputeStateHash());
        Assert.True(before.Work[0].Dormant); Assert.True(before.Work[0].Wet);
        session.Simulation.World.Tick = 1;
        var after = view.CaptureWaveRuntime()!;
        Assert.False(after.Work[0].Dormant); Assert.False(after.Work[0].Wet);
        Assert.True(before.Work[0].Dormant); Assert.True(before.Work[0].Wet);
        Assert.NotSame(before.Work, after.Work);
    }

}
