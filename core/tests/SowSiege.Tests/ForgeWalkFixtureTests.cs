using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class ForgeWalkFixtureTests
{
    [Fact]
    public void WalkingPilotUsesCurrentRulesAndExactlyThirtySecondsOfOrdinaryEastInput()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = ContentLoader.Load(data, false, "wave-1a");
        var hash = ContentLoader.Hash(data, false);
        using var stream = new MemoryStream();
        var end = ForgeWalkFixtureCli.Record(catalog, hash, stream);
        stream.Position = 0;
        var replay = ReplayCodec.Read(stream);
        Assert.Equal(900, replay.End.Tick);
        Assert.Equal(ReplayEndKind.Quit, replay.End.Kind);
        Assert.Equal(1, replay.Header.Options.Field!.Version);
        Assert.True(replay.Header.Options.Feel!.FasterLord);
        Assert.False(replay.Header.Options.Feel.ImmediateContact);
        Assert.False(replay.Header.Options.Feel.ReadableHealth);
        Assert.Null(replay.Header.Options.HostRewards);
        Assert.Null(replay.Header.Options.MeleeAim);
        Assert.Equal(900, replay.Commands.Count(command => command.Kind == ReplayCommandKind.Advance));
        Assert.All(replay.Commands, command =>
        {
            Assert.True(command.Kind is ReplayCommandKind.Advance or ReplayCommandKind.ChooseCard);
            if (command.Kind == ReplayCommandKind.Advance) { Assert.Equal(1000, command.Input.X); Assert.Equal(0, command.Input.Y); }
        });
        var verified = ReplayRunner.Verify(catalog, hash, replay);
        Assert.Equal(end.StateHash, verified.StateHash);
        Assert.Equal(ReplayEndKind.Quit, verified.EndKind);
        using var repeated = new MemoryStream();
        Assert.Equal(end, ForgeWalkFixtureCli.Record(catalog, hash, repeated));
        Assert.Equal(stream.ToArray(), repeated.ToArray());
    }
}
