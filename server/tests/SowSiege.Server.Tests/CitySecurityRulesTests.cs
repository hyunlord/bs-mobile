using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class CitySecurityRulesTests
{
    private static readonly CitySecurityPolicy Policy = new(240, 40, 240, 30, 30,
        [new("grain", 100), new("timber", 100)], 100, "fixture");

    [Theory]
    [InlineData(239, true)]
    [InlineData(240, false)]
    [InlineData(241, false)]
    public void ShieldEndIsExclusive(long now, bool expected) =>
        Assert.Equal(expected, CitySecurityRules.Protected(new(0, 240, 240, 0), now));

    [Fact]
    public void CreationAndActivationNeverExtendAnActiveShield()
    {
        var state = CitySecurityRules.Create(100, Policy, true);
        Assert.Equal(340, state.ShieldUntilMs);
        Assert.Equal("shield-active", CitySecurityRules.Activate(state, 339, false, Policy).Reason);
        var activated = CitySecurityRules.Activate(state, 340, false, Policy);
        Assert.True(activated.Done);
        Assert.Equal(380, activated.State.ShieldUntilMs);
        Assert.Equal(580, activated.State.NextFreeAtMs);
    }

    [Fact]
    public void ClockRollbackDoesNotRestoreExpiredTimeOrBypassCooldown()
    {
        var state = new CityShieldState(400, 350, 500, 450);
        var result = CitySecurityRules.Activate(state, 1, false, Policy);
        Assert.False(result.Done);
        Assert.Equal(400, result.State.UtcHighWatermark);
        Assert.Equal("hostile-cooldown", result.Reason);
    }

    [Fact]
    public void ActiveHostileMarchBlocksShieldEvenAfterPriorReturnCooldown()
    {
        Assert.Equal("hostile-march-active",
            CitySecurityRules.Activate(new(500, 0, 0, 0), 500, true, Policy).Reason);
    }

    [Theory]
    [InlineData(449, false)]
    [InlineData(450, true)]
    public void LastHostileReturnCooldownBoundary(long now, bool expected) =>
        Assert.Equal(expected, CitySecurityRules.Activate(new(400, 0, 0, 450), now, false, Policy).Done);

    [Fact]
    public void LegacyAccountDoesNotReceiveNewPlayerShield() =>
        Assert.Equal(0, CitySecurityRules.Create(500, Policy, false).ShieldUntilMs);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 0)]
    [InlineData(109, 0)]
    [InlineData(110, 1)]
    [InlineData(200, 10)]
    public void ScoutLootRangeUsesOnlyUnprotectedWallet(int wallet, int expected) =>
        Assert.Equal(expected, CitySecurityRules.LootLimit(wallet, 100, 100));
}
