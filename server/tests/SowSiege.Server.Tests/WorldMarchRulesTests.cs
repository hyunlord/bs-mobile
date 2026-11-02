using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldMarchRulesTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 10000)]
    [InlineData(7, 0, 70000)]
    [InlineData(7, 6, 67000)]
    [InlineData(7, 100, 64000)]
    public void TravelRoundsUpToSecondsAndClampsDataSpeed(int edges, int speed, long expected) =>
        Assert.Equal(expected, WorldMarchRules.TravelMilliseconds(edges, 10000, speed, 10));

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9999)]
    [InlineData(10000)]
    [InlineData(30000)]
    public void CancellationReturnUsesOnlyActuallyTravelledFraction(long elapsed) =>
        Assert.Equal(Math.Clamp(elapsed, 0, 10000), WorldMarchRules.ReturnTravelMilliseconds(1000, 11000, 1000 + elapsed));

    [Fact]
    public void InvalidAndOverflowTravelAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldMarchRules.TravelMilliseconds(-1, 10000, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldMarchRules.TravelMilliseconds(1, 0, 0, 10));
        Assert.Throws<OverflowException>(() => WorldMarchRules.TravelMilliseconds(int.MaxValue, long.MaxValue, 0, 10));
    }

    [Fact]
    public void ShippedMarchPolicyReadsCapacityAndHeroFromDraft()
    {
        var rules = WorldMarchContent.Load(ContentEstateTests.Root());
        Assert.Equal(2, rules.Slots);
        Assert.Equal(10000, rules.EdgeMilliseconds);
        Assert.Equal(20, rules.Capacity[1]);
        Assert.Equal(40, rules.Capacity[2]);
        Assert.Equal(60, rules.Capacity[3]);
        Assert.Equal("core:frontier_knight", rules.BasicHero);
    }
}
