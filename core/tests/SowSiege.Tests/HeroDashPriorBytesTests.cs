using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroDashPriorBytesTests
{
    [Theory]
    [InlineData("axis")]
    [InlineData("diagonal")]
    [InlineData("release")]
    [InlineData("body")]
    [InlineData("hit")]
    [InlineData("wall")]
    public void DashOnlyRetainsRawBytesFrozenAt86843feb(string name)
    {
        var fixture = HeroDashRuntimeTests.Fixture(enemyX: name is "body" or "hit" ? 80 : 1000,
            enemyY: name == "hit" ? 10 : 0, damage: name == "hit" ? 7 : 0, range: name == "hit" ? 11 : 0);
        if (name == "wall")
        {
            var map = fixture.Catalog.Tuning.World.Map;
            fixture = HeroDashRuntimeTests.Fixture(configure: a => a with
            { Walls = new[] { new HeroActiveWall("test:wall", map.Width / 2 + 70, map.Height / 2 - 20, map.Width / 2 + 71, map.Height / 2 + 20) } });
        }
        var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, header);
        EqualFixture(name + "-header.bin", bytes.ToArray());
        var session = new InteractiveSession(fixture.Catalog, header.Options); var commands = new List<ReplayCommand>();
        for (var tick = 0; tick < 16; tick++)
        {
            short x = (short)(name == "release" && tick >= 3 ? 0 : 1000), y = (short)(name == "diagonal" ? 333 : 0);
            var command = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance, new(x, y));
            session.Apply(command); commands.Add(command);
            if (tick is 0 or 2 or 15)
            { EqualFixture(name + "-state-" + (tick + 1) + ".bin", WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(session)); }
            if (tick == 2)
            {
                var recovery = WorldRecoveryCodec.Capture(session, commands); EqualFixture(name + "-recovery.bin", recovery);
                var restored = WorldRecoveryCodec.Restore(fixture.Catalog, fixture.Options.DataHash, recovery);
                Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
            }
        }
    }

    private static void EqualFixture(string name, byte[] actual)
    {
        var path = Path.Combine(Path.GetDirectoryName(WorldCombatAbsentStateCompatibilityTests.FixtureDirectory())!, "hero-dash-86843feb", name);
        Assert.True(File.Exists(path), $"Missing frozen 86843febe1510e62868104ee35c7fd8bc5512866 fixture: {path}");
        Assert.Equal(File.ReadAllBytes(path), actual);
    }
}
