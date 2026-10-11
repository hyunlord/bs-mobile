using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WaveSnapshotIsolationTests
{
    [Fact]
    public void CapturedWaveArraysKeepValuesAndSortedEnemyOrderAcrossLaterCaptures()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        var state = new WaveRuntimeState();
        state.EnemyActions.Add(9, new() { Phase = "charge", Origin = new(10, 20), UntilTick = 20 });
        state.EnemyActions.Add(2, new() { Phase = "approach", Origin = new(30, 40) });
        state.Work.Add(new() { Id = 1, Progress = 3, Position = new(50, 60) });
        state.Items.Add("core:z"); state.Items.Add("core:a");
        state.Counters.Add("count", 7);
        var before = state.Capture(catalog.WaveRuntime!, 10);
        state.EnemyActions[9].Phase = "recover";
        state.EnemyActions.Remove(2);
        state.Work[0].Progress = 99;
        state.Items.Clear(); state.Counters["count"] = 42;
        var after = state.Capture(catalog.WaveRuntime!, 21);
        Assert.Equal(new[] { 2, 9 }, before.Enemies.Select(e => e.Id));
        Assert.Equal("charge", before.Enemies[1].Phase);
        Assert.Equal(3, before.Work[0].Progress);
        Assert.Equal(new[] { "core:a", "core:z" }, before.Items);
        Assert.Equal(7, before.Counters["count"]);
        Assert.Single(after.Enemies);
        Assert.Equal("recover", after.Enemies[0].Phase);
        Assert.Equal(99, after.Work[0].Progress);
        Assert.Empty(after.Items);
        Assert.Equal(42, after.Counters["count"]);
    }
}
