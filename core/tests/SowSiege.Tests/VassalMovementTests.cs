using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalMovementTests
{
    [Theory]
    [InlineData(100, 103)]
    [InlineData(-100, -103)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    [InlineData(0, 0)]
    public void LastFloorPreservesSignedSymmetry(int delta, int expected)
    {
        var state = new VassalRunState(VassalRunTests.Options(MetaAbility.Movement));
        Assert.Equal(new Position(expected, -expected), VassalMovement.AfterMove(new(0, 0), new(delta, -delta), state));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SessionScalesTheCompletedMovementWithoutChangingFeelRemainder(bool faster, bool empty)
    {
        var baseline = Session(faster, null);
        var options = empty ? VassalRunTests.Options() : VassalRunTests.Options(MetaAbility.Movement);
        var enabled = Session(faster, options);
        var expectedState = new VassalRunState(options);
        foreach (var input in new[] { new PlayerInput(431, -719), new PlayerInput(431, -719), new PlayerInput(-431, 719), new PlayerInput(0, 0) })
        {
            var before = baseline.Simulation.World.Lord;
            var enabledBefore = enabled.Simulation.World.Lord;
            baseline.Apply(new(baseline.NextSequence, baseline.Simulation.World.Tick, ReplayCommandKind.Advance, input));
            enabled.Apply(new(enabled.NextSequence, enabled.Simulation.World.Tick, ReplayCommandKind.Advance, input));
            var after = baseline.Simulation.World.Lord;
            var unscaled = new Position(enabledBefore.X + after.X - before.X, enabledBefore.Y + after.Y - before.Y);
            Assert.Equal(VassalMovement.AfterMove(enabledBefore, unscaled, expectedState), enabled.Simulation.World.Lord);
            if (empty) { Assert.Equal(after, enabled.Simulation.World.Lord); }
            if (faster)
            {
                Assert.Equal(baseline.State.Feel!.RemainderX, enabled.State.Feel!.RemainderX);
                Assert.Equal(baseline.State.Feel.RemainderY, enabled.State.Feel.RemainderY);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalMovementStaysInsideMapAndKeepsOriginalBoundaryRemainderReset(bool faster)
    {
        var session = Session(faster, VassalRunTests.Options(MetaAbility.Movement));
        var map = session.Catalog.Tuning.World.Map;
        session.Simulation.World.Lord = new(map.Width - 1, 1);
        session.Apply(new(0, 0, ReplayCommandKind.Advance, new(1000, -1000)));
        Assert.Equal(new Position(map.Width, 0), session.Simulation.World.Lord);
        if (faster)
        {
            Assert.Equal(0, session.State.Feel!.RemainderX);
            Assert.Equal(0, session.State.Feel.RemainderY);
        }
    }

    private static InteractiveSession Session(bool faster, VassalRunOptions? vassals)
    {
        var catalog = WaveEstateTests.Wave();
        var run = new RunOptions(1, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "random", ManualCards: true);
        return new(catalog, new(run, AimMode.Movement, new string('A', 64), Feel: faster ? new(FasterLord: true) : null, Vassals: vassals));
    }
}
