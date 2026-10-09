using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WaveReplayTests
{
    [Theory]
    [InlineData(30000)]
    [InlineData(30001)]
    public void NormalInputWaveReplayHasNoDebugCommandsAndMatchesItsStateHash(int seed)
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var original = ContentLoader.Load(data, false, "wave-1a");
        var catalog = original with { Tuning = original.Tuning with { DurationTicks = 900 } };
        var hash = ContentLoader.Hash(data, false);
        using var output = new MemoryStream();
        var expected = InteractiveCli.RecordWaveFixture(catalog, hash, seed, output);
        output.Position = 0;
        var replay = ReplayCodec.Read(output);
        Assert.All(replay.Commands, command => Assert.True(command.Kind is ReplayCommandKind.Advance or ReplayCommandKind.ChooseCard));
        Assert.Contains(replay.Header.Options.Run.TargetMaterial, original.WaveRuntime!.MaterialTargets!.Keys);
        var actual = ReplayRunner.Verify(catalog, hash, replay);
        Assert.Equal(expected.StateHash, actual.StateHash);
        Assert.Equal(expected.Tick, actual.Tick);
    }
    [Fact]
    public void BoundedCaptureUsesExistingQuitTerminalWithoutPretendingToCompleteTheRun()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = ContentLoader.Load(data, false, "wave-1a");
        var hash = ContentLoader.Hash(data, false);
        using var output = new MemoryStream();
        WaveRuntimeFrame? observed = null;
        var end = InteractiveCli.RecordWaveFixture(catalog, hash, 30000, output, 300, frame => observed = frame);
        Assert.NotNull(observed); Assert.NotEmpty(observed.Counters);
        Assert.Equal(300, end.Tick); Assert.Equal(ReplayEndKind.Quit, end.Kind);
        output.Position = 0;
        var result = ReplayRunner.Verify(catalog, hash, ReplayCodec.Read(output));
        Assert.Equal(ReplayEndKind.Quit, result.EndKind); Assert.Equal(end.StateHash, result.StateHash);
    }

}
