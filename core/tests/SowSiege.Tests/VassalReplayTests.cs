using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalReplayTests
{
    [Fact]
    public void ManuallyConstructedDisabledFeelHeaderWritesTheCanonicalAbsentBlock()
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with { Vassals = VassalRunTests.Options() };
        var canonical = ReplayCodec.Header(options);
        var manual = canonical with { Options = canonical.Options with { Feel = new(false, false, false) } };
        using var expected = new MemoryStream();
        using var actual = new MemoryStream();
        ReplayCodec.WriteHeader(expected, canonical);
        ReplayCodec.WriteHeader(actual, manual);
        Assert.Equal(expected.ToArray(), actual.ToArray());
        var session = new InteractiveSession(catalog, options);
        ReplayCodec.WriteEnd(actual, new(0, 0, ReplayEndKind.Quit, session.ComputeStateHash()));
        actual.Position = 0;
        var replay = ReplayCodec.Read(actual);
        Assert.Null(replay.Header.Options.Feel);
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, replay).StateHash);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FormatEightKeepsEstateAndMeleeIndependent(bool estate, bool melee)
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with
        {
            Estate = estate ? WaveEstateTests.Options(catalog).Estate : null,
            MeleeAim = melee ? new() : null,
            Vassals = VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience)
        };
        var session = new InteractiveSession(catalog, options);
        var commands = VassalRecoveryTests.Advance(session, 60);
        using var bytes = new MemoryStream();
        ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(session.Options));
        foreach (var command in commands)
        {
            ReplayCodec.WriteCommand(bytes, command);
        }
        ReplayCodec.WriteEnd(bytes, new(session.NextSequence, session.Simulation.World.Tick, ReplayEndKind.Quit, session.ComputeStateHash()));
        bytes.Position = 0;
        var replay = ReplayCodec.Read(bytes);
        Assert.Equal(ReplayCodec.VassalVersion, replay.Header.FormatVersion);
        Assert.Equal(estate, replay.Header.Options.Estate is not null);
        Assert.Equal(melee, replay.Header.Options.MeleeAim is not null);
        Assert.Equal(VassalRunCodec.Encode(options.Vassals), VassalRunCodec.Encode(replay.Header.Options.Vassals!));
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, replay).StateHash);
    }

    [Fact]
    public void EmptyEnabledRosterIsNotAbsentAndOldFormatsCannotCarryIt()
    {
        var catalog = WaveEstateTests.Catalog();
        var absent = WaveEstateTests.Options(catalog) with { Estate = null, Field = null };
        var enabled = absent with { Vassals = VassalRunTests.Options() };
        Assert.Equal(1, ReplayCodec.Header(absent).FormatVersion);
        Assert.Equal(8, ReplayCodec.Header(enabled).FormatVersion);
        Assert.NotEqual(new InteractiveSession(catalog, absent).ComputeStateHash(), new InteractiveSession(catalog, enabled).ComputeStateHash());
        for (var version = 1; version <= 7; version++)
        {
            Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), ReplayCodec.Header(enabled) with { FormatVersion = version }));
        }
        Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), ReplayCodec.Header(absent) with { FormatVersion = 8 }));
    }

    [Fact]
    public void HostCommandsNeedAnExplicitHostBlockInFormatEight()
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with { Vassals = VassalRunTests.Options() };
        using var bytes = new MemoryStream();
        ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options));
        ReplayCodec.WriteCommand(bytes, new(0, 0, ReplayCommandKind.HostGrantRerollV1, Value: 1));
        ReplayCodec.WriteEnd(bytes, new(1, 0, ReplayEndKind.Quit, new string('A', 64)));
        bytes.Position = 0;
        Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(bytes));
    }

    [Theory]
    [InlineData("wave-c05/run.ssreplay")]
    [InlineData("first-playable-fp01/run.ssreplay")]
    [InlineData("estate-v7/main-75ffc97-l1-30001.ssreplay")]
    public void HistoricalReplayWireRoundTripRemainsByteIdentical(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "benchmarks")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        var original = File.ReadAllBytes(Path.Combine(root.FullName, "benchmarks", relativePath));
        var replay = ReplayCodec.Read(new MemoryStream(original));
        Assert.Null(replay.Header.Options.Vassals);
        using var written = new MemoryStream(); ReplayCodec.WriteHeader(written, replay.Header);
        var checkpoints = replay.Checkpoints.ToDictionary(c => c.AppliedCommands);
        if (checkpoints.TryGetValue(0, out var initial))
        {
            ReplayCodec.WriteCheckpoint(written, initial);
        }
        foreach (var command in replay.Commands)
        {
            ReplayCodec.WriteCommand(written, command);
            if (checkpoints.TryGetValue(command.Sequence + 1, out var checkpoint))
            {
                ReplayCodec.WriteCheckpoint(written, checkpoint);
            }
        }
        ReplayCodec.WriteEnd(written, replay.End);
        Assert.Equal(original, written.ToArray());
    }
}
