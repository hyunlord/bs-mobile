using System.Security.Cryptography;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldSchemaOneCompatibilityTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void SchemaOneHeaderRetainsFrozenBytes(bool estate, bool melee, bool vassals)
    {
        var document = WorldRecoveryCodecTests.Document(vassals);
        var options = document.Header.Options with
        {
            Estate = estate ? new(1, "test:loadout", new string('C', 64)) : null,
            MeleeAim = melee ? new() : null
        };
        using var stream = new MemoryStream();
        ReplayCodec.WriteHeader(stream, ReplayCodec.Header(options));
        AssertFixture($"header-estate-{estate}-melee-{melee}-vassals-{vassals}.bin", stream.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void SchemaOneStateAndRecoveryRetainFrozenBytes(int ticks)
    {
        var catalog = WaveEstateTests.Wave();
        var options = WorldCombatRuntimeTests.Options(catalog);
        var session = new InteractiveSession(catalog, options);
        var commands = new List<ReplayCommand>();
        for (var tick = 0; tick < ticks; tick++)
        {
            var command = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance);
            session.Apply(command);
            commands.Add(command);
        }
        var state = WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(session);
        AssertFixture($"state-tick-{ticks}.bin", state);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(state)), session.ComputeStateHash());
        AssertFixture($"runtime-tick-{ticks}.bin", session.State.WorldCombat!.Encode());
        var recovery = WorldRecoveryCodec.Capture(session, commands);
        AssertFixture($"recovery-tick-{ticks}.bin", recovery);
        var restored = WorldRecoveryCodec.Restore(catalog, options.DataHash, recovery);
        Assert.Equal(state, WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(restored));
        if (ticks != 3) { return; }
        using var replay = new MemoryStream();
        var header = ReplayCodec.Header(options);
        ReplayCodec.WriteHeader(replay, header);
        foreach (var command in commands) { ReplayCodec.WriteCommand(replay, command); }
        var end = new WorldReplayEnd(session.NextSequence, session.GetWorldCombatResult());
        ReplayCodec.WriteWorldEnd(replay, end);
        AssertFixture("terminal-replay.bin", replay.ToArray());
        AssertFixture("terminal-end.bin", ReplayCodec.EncodeWorldEnd(header, end));
    }

    private static void AssertFixture(string name, byte[] actual)
    {
        var directory = Path.Combine(Path.GetDirectoryName(WorldCombatAbsentStateCompatibilityTests.FixtureDirectory())!, "world-schema1-709869e6");
        var path = Path.Combine(directory, name);
        Assert.True(File.Exists(path), $"Missing baseline fixture from 709869e654df948bec2a1a158a190244cc9ec6ce: {path}");
        Assert.Equal(File.ReadAllBytes(path), actual);
    }
}
