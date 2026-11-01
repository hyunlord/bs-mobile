using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class ClockTests
{
    [Fact]
    public void BackwardClockDoesNotCreditTheSameIntervalAgain()
    {
        var initial = new ClockState(10000, 10, 0);
        var backward = EstateClock.Observe(initial, 9000, 100);
        Assert.Equal(initial, backward.State);
        Assert.Equal(0, backward.CreditedSeconds);
        var recovered = EstateClock.Observe(backward.State, 11000, 100);
        Assert.Equal(1, recovered.CreditedSeconds);
    }

    [Fact]
    public void FractionalRequestsAndOfflineCapAreNotLostOrReplayed()
    {
        var first = EstateClock.Observe(new ClockState(1000, 0, 0), 1500, 10);
        var second = EstateClock.Observe(first.State, 2000, 10);
        Assert.Equal(1, second.State.LogicalSeconds);
        var capped = EstateClock.Observe(second.State, 102000, 10);
        Assert.Equal(10, capped.CreditedSeconds);
        Assert.Equal(0, EstateClock.Observe(capped.State, 102000, 10).CreditedSeconds);
    }
    [Theory]
    [InlineData(0, "accepted")]
    [InlineData(1, "offline-capped")]
    [InlineData(999, "offline-capped")]
    public void OfflineCapDiscardsEveryExcessMillisecond(int excess, string reason)
    {
        var result = EstateClock.Observe(new ClockState(1000, 0, 0), 11000 + excess, 10);
        Assert.Equal(10, result.CreditedSeconds);
        Assert.Equal(0, result.State.RemainderMilliseconds);
        Assert.Equal(reason, result.Reason);
        var repeated = EstateClock.Observe(result.State, 11000 + excess, 10);
        Assert.Equal(0, repeated.CreditedSeconds);
        var next = EstateClock.Observe(repeated.State, 11001 + excess, 10);
        Assert.Equal(0, next.CreditedSeconds);
        Assert.Equal(1, next.State.RemainderMilliseconds);
    }
}
