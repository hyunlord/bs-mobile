using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroTauntPriorBytesTests
{
    [Theory]
    [InlineData("taunt-basic")]
    [InlineData("taunt-overlap")]
    [InlineData("taunt-movement")]
    [InlineData("taunt-expiry")]
    [InlineData("taunt-damage")]
    public void TauntOnlyRetainsRawBytesFrozenAt6ad16d4e(string name)
    {
        var fixture = HeroTauntRuntimeTests.Fixture(name == "taunt-overlap" ? 2 : 1,
            configure: a => HeroTauntCodecTests.Change(a, 0, p => p with { DurationTicks = 5 }),
            configureWorld: s => name == "taunt-damage" ? s with
            { Cohorts = new[] { s.Cohorts[0] with { Stats = s.Cohorts[0].Stats with { Attack = 7, Range = 100, CooldownTicks = 1 } } } } : s);
        var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, header);
        EqualFixture(name + "-header.bin", bytes.ToArray());
        var session = new InteractiveSession(fixture.Catalog, fixture.Options); var commands = new List<ReplayCommand>();
        for (var tick = 0; tick < 16; tick++)
        {
            var command = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance,
                new((short)(name == "taunt-movement" && tick == 3 ? 1000 : 0), 0));
            session.Apply(command); commands.Add(command);
            if (tick is 0 or 2 or 15)
            { EqualFixture(name + "-state-" + (tick + 1) + ".bin", WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(session)); }
            if (tick == 2)
            {
                var recovery = WorldRecoveryCodec.Capture(session, commands); EqualFixture(name + "-recovery.bin", recovery);
                Assert.Equal(session.ComputeStateHash(), WorldRecoveryCodec.Restore(fixture.Catalog, fixture.Options.DataHash, recovery).ComputeStateHash());
            }
        }
    }

    private static void EqualFixture(string name, byte[] actual)
    {
        var path = Path.Combine(Path.GetDirectoryName(WorldCombatAbsentStateCompatibilityTests.FixtureDirectory())!, "hero-taunt-6ad16d4e", name);
        Assert.True(File.Exists(path), $"Missing frozen 6ad16d4e099b3911fb9d4444aa228edbbe3d2cd3 fixture: {path}");
        Assert.Equal(File.ReadAllBytes(path), actual);
    }
}
