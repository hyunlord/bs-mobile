using SowSiege.Core;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatInputTests
{
    [Theory]
    [InlineData(3999, 5)]
    [InlineData(4000, 5)]
    [InlineData(4001, 90)]
    public void GraceBoundaryIsInclusive(long now, int tick) =>
        Assert.Equal(tick, WorldCombatInput.DueTick(new(1000, 1000, 5, 5), 1000, now, 30, 300, 3000));

    [Fact]
    public void DuplicateAndClockRollbackNeverRestartGrace()
    {
        var initial = new WorldInputFrontier(4500, 1000, 5, 5);
        var duplicate = WorldCombatInput.Accepted(initial, 2000, 5, 5);
        Assert.Equal(initial, duplicate);
        Assert.Equal(105, WorldCombatInput.DueTick(duplicate, 1000, 2000, 30, 300, 3000));
        var accepted = WorldCombatInput.Accepted(initial, 2000, 6, 6);
        Assert.Equal(4500, accepted.LastReceivedAtMs);
    }

    [Fact]
    public void SequenceOnlyProgressDoesNotRenewGrace()
    {
        var frontier = new WorldInputFrontier(1000, 1000, 0, 0);
        for (var sequence = 1; sequence <= 3; sequence++)
        {
            frontier = WorldCombatInput.Accepted(frontier, 1000 + sequence * 1000, 0, sequence);
            Assert.Equal(1000, frontier.LastReceivedAtMs);
        }
        Assert.Equal(0, WorldCombatInput.DueTick(frontier, 1000, 4000, 30, 300, 3000));
        Assert.Equal(90, WorldCombatInput.DueTick(frontier, 1000, 4001, 30, 300, 3000));
    }

    [Theory]
    [InlineData(13999, 5)]
    [InlineData(14000, 5)]
    [InlineData(14001, 300)]
    public void TickProgressCannotExtendAbsoluteExpiry(long now, int expectedTick)
    {
        var frontier = WorldCombatInput.Accepted(new(13000, 13000, 4, 4), 13999, 5, 5);
        Assert.Equal(expectedTick, WorldCombatInput.DueTick(frontier, 1000, now, 30, 300, 3000));
    }

    [Fact]
    public void AbsoluteDeadlineRoundsFractionalTickUp() =>
        Assert.Equal(4034, WorldCombatInput.AbsoluteDeadline(1000, 30, 1, 3000));

    [Theory]
    [InlineData(1000, 2000, 3001)]
    [InlineData(13999, 2000, 14001)]
    [InlineData(14000, 2000, 14001)]
    public void VerificationReservationCannotDelayAbsoluteRecovery(long now, long maximumWait, long expected) =>
        Assert.Equal(expected, WorldCombatInput.VerificationDeadline(now, maximumWait, 14000));

    [Fact]
    public void VerificationReservationRejectsExpiredRun() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldCombatInput.VerificationDeadline(14001, 2000, 14000));

    [Fact]
    public void LongInterruptionClampsAtLimitWithoutOverflow() =>
        Assert.Equal(300, WorldCombatInput.DueTick(new(1000, 1000, 0, 0), 1000, long.MaxValue, 30, 300, 3000));

    [Fact]
    public void RestartUsesPersistedFrontierAndDoesNotExtendGrace()
    {
        var stored = new WorldInputFrontier(3500, 2000, 30, 30);
        var restored = System.Text.Json.JsonSerializer.Deserialize<WorldInputFrontier>(System.Text.Json.JsonSerializer.Serialize(stored))!;
        Assert.Equal(30, WorldCombatInput.DueTick(restored, 1000, 5000, 30, 300, 3000));
        Assert.Equal(120, WorldCombatInput.DueTick(restored, 1000, 5001, 30, 300, 3000));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(100, 8)]
    public void BedsUseOrdinalTypesAndPreserveEverySoldier(int beds, int admitted)
    {
        var result = WorldCombatInput.SettleLosses([new("test:z", 10, 5), new("test:a", 10, 5)], 80, beds);
        Assert.Equal("test:a", result[0].Id);
        Assert.Equal(admitted, result.Sum(x => x.Admitted));
        Assert.Equal(Math.Min(4, beds), result[0].Admitted);
        Assert.All(result, x => Assert.Equal(x.N, x.Survivors + x.Admitted + x.Lost));
    }

    [Fact]
    public void InvalidCasualtiesAndDuplicateTypesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => WorldCombatInput.SettleLosses([new("test:a", 1, 2)], 80, 1));
        Assert.Throws<ArgumentException>(() => WorldCombatInput.SettleLosses([new("test:a", 1, 0), new("test:a", 1, 0)], 80, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldCombatInput.Accepted(new(1000, 1000, 5, 5), 1001, 4, 6));
    }
}
