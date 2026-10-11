using System;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

public sealed class FeelComparisonCaptureTests
{
    static FeelComparisonCapture Mode(string mode) => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", mode });
    [TestCase("up",0,1)]
    [TestCase("down",0,-1)]
    [TestCase("right",1,0)]
    public void CardinalWalkEvidenceUsesDeclaredHeading(string heading,float x,float y)
    {
        var options=FeelComparisonCapture.Parse(new[]{"--autoplay-capture","--feel-comparison","walk-b","--feel-walk-heading",heading});
        Assert.That(options.Direction(30,30),Is.EqualTo(new Vector2(x,y)));
        Assert.That(options.InputProtocol,Does.Contain(heading));
    }
    [Test] public void HeadingCannotChangeRulesTrialMovement()
    {
        Assert.Throws<ArgumentException>(()=>FeelComparisonCapture.Parse(new[]{"--autoplay-capture","--feel-comparison","rules-off","--feel-walk-heading","up"}));
    }

    [Test] public void DefaultCaptureRemainsInactive() => Assert.That(FeelComparisonCapture.Parse(Array.Empty<string>()), Is.Null);
    [Test] public void WalkViewportAndSpeedAreExplicitAndDoNotEnableContactOrHealth()
    {
        var value=FeelComparisonCapture.Parse(new[]{"--autoplay-capture","--feel-comparison","walk-b","--feel-walk-landscape","--feel-walk-fast"});
        Assert.That(value.Landscape&&value.WalkFast&&value.RequestedHeroB,Is.True);
        Assert.That(value.RequestedRulesOn||value.RequestedFeedback,Is.False);
        Assert.That(value.Window.DurationSeconds,Is.EqualTo(5));
        Assert.Throws<ArgumentException>(()=>FeelComparisonCapture.Parse(new[]{"--autoplay-capture","--feel-walk-fast"}));
        Assert.Throws<ArgumentException>(()=>FeelComparisonCapture.Parse(new[]{"--autoplay-capture","--feel-comparison","rules-on","--feel-walk-landscape"}));
    }
    [Test] public void FailurePreviewUsesThirtySecondsFromZeroAndLandscape()
    {
        var value = FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", "rules-on", "--feel-failure-preview", "--capture-movement", "default" });
        Assert.That(value.FailurePreview && value.Landscape, Is.True);
        Assert.That(value.Window.StartSeconds, Is.Zero); Assert.That(value.Window.EndSeconds, Is.EqualTo(30));
        Assert.That(value.MovementConfiguration, Is.EqualTo("default")); Assert.That(value.InputProtocol, Is.EqualTo("growth-policy-v1"));
        Assert.Throws<InvalidOperationException>(() => value.Window.Ready(426, 30, true));
    }
    [Test] public void FailurePreviewCannotAlterWalkOrOrdinaryCapture()
    {
        Assert.Throws<ArgumentException>(() => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-failure-preview" }));
        Assert.Throws<ArgumentException>(() => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", "walk-a", "--feel-failure-preview" }));
        Assert.Throws<ArgumentException>(() => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", "walk-b", "--capture-movement", "default" }));
        Assert.Throws<ArgumentException>(() => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", "rules-on", "--capture-movement", "other" }));
    }
    [Test] public void ComparisonCannotRunWithoutIsolatedAutoplay()
        => Assert.Throws<ArgumentException>(() => FeelComparisonCapture.Parse(new[] { "--feel-comparison", "walk-a" }));
    [Test] public void ExplicitWindowCannotOverrideComparison()
        => Assert.Throws<ArgumentException>(() => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", "rules-on", "--capture-start-seconds", "0" }));
    [Test] public void WalkPairUsesSameFiveSecondsAndOnlyChangesHero()
    {
        var a = Mode("walk-a"); var b = Mode("walk-b");
        Assert.That(a.Window.StartSeconds, Is.Zero); Assert.That(a.Window.DurationSeconds, Is.EqualTo(5));
        Assert.That(b.RequestedHeroB, Is.True); Assert.That(a.RequestedHeroB, Is.False);
        Assert.That(b.RequestedRulesOn || a.RequestedRulesOn, Is.False);
        for (var tick = 0; tick <= 150; tick++) { Assert.That(a.Direction(tick, 30), Is.EqualTo(Vector2.right)); Assert.That(a.Direction(tick, 30), Is.EqualTo(b.Direction(tick, 30))); }
    }
    [Test] public void RulesPairSharesReactivePolicyAndRejectsFixedDirectionTape()
    {
        var off = Mode("rules-off"); var on = Mode("rules-on");
        Assert.That(on.FailurePreview, Is.False); Assert.That(on.MovementConfiguration, Is.EqualTo("evasive"));
        Assert.That(on.Window.StartSeconds, Is.EqualTo(360)); Assert.That(on.Window.EndSeconds, Is.EqualTo(390));
        Assert.That(on.Window.Landscape, Is.True); Assert.That(off.RequestedRulesOn, Is.False);
        Assert.That(on.RequestedRulesOn && on.RequestedFeedback, Is.True); Assert.That(on.RequestedHeroB, Is.False);
        Assert.That(off.InputProtocol, Is.EqualTo("growth-evasive-policy-v1"));
        Assert.That(on.InputProtocol, Is.EqualTo(off.InputProtocol));
        Assert.Throws<InvalidOperationException>(() => off.Direction(0, 30));
        Assert.Throws<InvalidOperationException>(() => on.Direction(900, 30));
    }
}

public sealed class FieldVideoCaptureTests
{
    [Test] public void FieldVideoIsSeparateFromFiveSecondWalkComparison()
    {
        var field = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "straight" });
        Assert.That(field.Window.DurationSeconds, Is.EqualTo(60));
        Assert.That(field.Topology, Is.EqualTo("field"));
        var walk = FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", "walk-b" });
        Assert.That(walk.Window.DurationSeconds, Is.EqualTo(5));
    }
    [Test] public void ReturnRouteWalksOutForSixtyActualGameplaySeconds()
    {
        var value = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "out-return", "--field-topology", "old" });
        Assert.That(value.Topology, Is.EqualTo("old"));
        Assert.That(value.Window.EndSeconds, Is.EqualTo(125));
        Assert.That(value.Direction(1799, 30, new WorldPoint(1000, 0), new WorldPoint(0, 0), 24), Is.EqualTo(Vector2.right));
        Assert.That(value.Direction(1800, 30, new WorldPoint(1000, 0), new WorldPoint(0, 0), 24), Is.EqualTo(Vector2.left));
        Assert.That(value.Direction(3599, 30, new WorldPoint(1000, 0), new WorldPoint(0, 0), 24), Is.EqualTo(Vector2.left));
        Assert.Throws<InvalidOperationException>(() => value.Window.Ready(2000, 30, true));
        Assert.That(value.InputProtocol, Is.EqualTo("field-right-60s-steer-home-until-125s-v3"));
    }
    [Test] public void FieldWindowDoesNotRelaxOrdinaryCaptureDurationLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSegmentWindow(0, 120));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSegmentWindow(0, 125));
        var args = new[] { "--autoplay-capture", "--field-video", "out-return" };
        Assert.That(FeelComparisonCapture.ResolveWindow(args).DurationSeconds, Is.EqualTo(125));
        Assert.That(AutoplayCapture.IsGoldenMinuteRequested(args), Is.True);
    }
    [TestCase("--capture-duration-seconds")]
    [TestCase("--capture-movement")]
    [TestCase("--feel-comparison")]
    [TestCase("--capture-target-material")]
    public void FieldVideoRejectsConflictingRouteOwnership(string flag)
        => Assert.Throws<ArgumentException>(() => FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "straight", flag, "anything" }));
    [Test] public void FieldVideoRequiresIsolatedCaptureAndKnownTopology()
    {
        Assert.That(FieldVideoCapture.Parse(Array.Empty<string>()), Is.Null);
        Assert.Throws<ArgumentException>(() => FieldVideoCapture.Parse(new[] { "--field-video", "straight" }));
        Assert.Throws<ArgumentException>(() => FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "straight", "--field-topology", "unknown" }));
        Assert.Throws<ArgumentException>(() => FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-topology", "old" }));
    }
    [Test] public void ReturnSteersToActualHomeAndRecordsOnlyObservedArrival()
    {
        var value = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "out-return" });
        var home = new WorldPoint(300, 500);
        value.Observe(0, 30, home, home, 24);
        Assert.That(value.Evidence.arrived, Is.False);
        var direction = value.Direction(1800, 30, new WorldPoint(1300, 900), home, 24);
        Assert.That(direction.x, Is.LessThan(0)); Assert.That(direction.y, Is.LessThan(0));
        Assert.That(value.Evidence.farthestHomeDistance, Is.EqualTo(Math.Sqrt(1000 * 1000 + 400 * 400)).Within(.0001));
        Assert.That(value.Evidence.startX, Is.EqualTo(300));
        Assert.That(value.Direction(2800, 30, new WorldPoint(308, 500), home, 24).x, Is.EqualTo(-8f / 96).Within(.0001));
        Assert.That(value.Evidence.arrived, Is.False);
        Assert.That(value.Direction(2801, 30, new WorldPoint(305, 500), home, 24), Is.EqualTo(Vector2.zero));
        Assert.That(value.Evidence.arrived, Is.True); Assert.That(value.Evidence.arrivalTick, Is.EqualTo(2801));
        Assert.That(value.Evidence.endHomeDistance, Is.EqualTo(5));
        // A later knockback must be visible rather than retaining a false final-home claim.
        Assert.That(value.Direction(2802, 30, new WorldPoint(500, 500), home, 24), Is.EqualTo(Vector2.left));
        Assert.That(value.Evidence.atHomeAtEnd, Is.False); Assert.That(value.Evidence.arrivalTick, Is.EqualTo(2801));
        value.CloseObservation();
        value.Observe(2803, 30, home, home, 24);
        Assert.That(value.Evidence.endTick, Is.EqualTo(2802));
        Assert.That(value.Evidence.atHomeAtEnd, Is.False);
    }
    [Test] public void ReturnAllowsArrivalDuringExtraFiveSecondsButNeverAfterItsWindow()
    {
        var value = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "out-return" });
        var home = new WorldPoint(0, 0);
        value.Observe(3600, 30, new WorldPoint(31, 0), home, 24);
        Assert.That(value.Evidence.arrived, Is.False);
        Assert.That(value.Window.Complete(3749, 30), Is.False);
        value.Observe(3750, 30, new WorldPoint(5, 0), home, 24);
        Assert.That(value.Window.Complete(3750, 30), Is.True);
        Assert.That(value.Evidence.arrived, Is.True);
        Assert.That(value.Evidence.arrivalTick, Is.EqualTo(3750));
        Assert.That(value.Evidence.arrivalDeadzone, Is.EqualTo(6));
        value.Observe(3751, 30, home, home, 24);
        Assert.That(value.Evidence.endTick, Is.EqualTo(3750));
        Assert.That(value.Evidence.endHomeDistance, Is.EqualTo(5));
        var late = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "out-return" });
        late.Observe(3750, 30, new WorldPoint(31, 0), home, 24);
        late.Observe(3751, 30, home, home, 24);
        Assert.That(late.Evidence.arrived, Is.False);
    }
    [Test] public void ArrivalEasingUsesActualLocomotionSpeedAndStraightRouteDoesNotChange()
    {
        var value = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "out-return" });
        var point = new WorldPoint(48, 0); var home = new WorldPoint(0, 0);
        var normal = value.Direction(1800, 30, point, home, 24);
        var fast = value.Direction(1800, 30, point, home, 31.2f);
        Assert.That(Math.Abs(fast.x), Is.LessThan(Math.Abs(normal.x)));
        var straight = FieldVideoCapture.Parse(new[] { "--autoplay-capture", "--field-video", "straight" });
        Assert.That(straight.Direction(1800, 30, point, home, 24), Is.EqualTo(Vector2.right));
        Assert.That(straight.Evidence.arrived, Is.False);
    }
}
