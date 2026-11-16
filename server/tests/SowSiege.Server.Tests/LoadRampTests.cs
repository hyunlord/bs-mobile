using Xunit;

namespace SowSiege.Server.Tests;

public sealed class LoadRampTests
{
    [Fact]
    public void ImmediateAdmissionPreservesFullOriginalMeasurementWindow()
    {
        var plan = new LoadRamp(10000, 0, 60);
        Assert.Equal(TimeSpan.Zero, plan.AdmissionAt(9999));
        Assert.Equal(TimeSpan.FromSeconds(60), plan.TotalDuration);
        Assert.Equal("plateau", plan.Phase(TimeSpan.Zero));
        Assert.Equal("drain", plan.Phase(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void TenThousandAtOneHundredPerSecondGetsFullPlateauAfterRamp()
    {
        var plan = new LoadRamp(10000, 100, 60);
        Assert.Equal(TimeSpan.Zero, plan.AdmissionAt(0));
        Assert.Equal(TimeSpan.FromSeconds(1), plan.AdmissionAt(100));
        Assert.Equal(TimeSpan.FromMilliseconds(99990), plan.AdmissionAt(9999));
        Assert.Equal(TimeSpan.FromSeconds(100), plan.RampDuration);
        Assert.Equal(TimeSpan.FromSeconds(160), plan.TotalDuration);
        Assert.Equal("ramp", plan.Phase(TimeSpan.FromSeconds(100).Subtract(TimeSpan.FromTicks(1))));
        Assert.Equal("plateau", plan.Phase(TimeSpan.FromSeconds(100)));
        Assert.Equal(TimeSpan.Zero, plan.MeasuredPlateau(TimeSpan.FromSeconds(99)));
        Assert.Equal(TimeSpan.FromSeconds(20), plan.MeasuredPlateau(TimeSpan.FromSeconds(120)));
        Assert.Equal(TimeSpan.FromSeconds(60), plan.MeasuredPlateau(TimeSpan.FromSeconds(180)));
    }

    [Fact]
    public void FractionalAdmissionRoundsUpAndLateSchedulingDoesNotWaitAgain()
    {
        var plan = new LoadRamp(1000, 3, 60);
        Assert.Equal(TimeSpan.FromTicks(3_333_334), plan.AdmissionAt(1));
        Assert.True(plan.AdmissionAt(1).TotalSeconds >= 1d / 3);
        Assert.Equal(TimeSpan.FromTicks(1), plan.DelayUntilAdmission(1, TimeSpan.FromTicks(3_333_333)));
        Assert.Equal(TimeSpan.Zero, plan.DelayUntilAdmission(1, TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.AdmissionAt(1000));
    }

    [Fact]
    public void SchedulerStallMovesNextAdmissionInsteadOfCatchingUp()
    {
        var plan = new LoadRamp(10000, 100, 60);
        Assert.Equal(TimeSpan.FromMilliseconds(5010), plan.NextAdmissionAfter(TimeSpan.FromSeconds(5)));
        Assert.Equal(TimeSpan.FromMilliseconds(5020), plan.NextAdmissionAfter(TimeSpan.FromMilliseconds(5010)));
        Assert.Equal(TimeSpan.FromSeconds(5), new LoadRamp(1000, 0, 60).NextAdmissionAfter(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void InvalidRatesAreRejected(int rate) => Assert.Throws<ArgumentOutOfRangeException>(() => new LoadRamp(1000, rate, 60));
}
