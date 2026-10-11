using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
using Xunit.Abstractions;

namespace SowSiege.Tests;

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
        var run800 = Measure(false, run800Calls); var wave800 = Measure(true, wave800Calls);
        Populate(session, 1600);
        var run1600 = Measure(false, run1600Calls); var wave1600 = Measure(true, wave1600Calls);
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
}
