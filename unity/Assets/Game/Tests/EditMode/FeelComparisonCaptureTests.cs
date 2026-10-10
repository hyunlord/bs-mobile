using System;
using Game.App;
using NUnit.Framework;
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
        Assert.That(value.Window.EndSeconds, Is.EqualTo(120));
        Assert.That(value.Direction(1799, 30), Is.EqualTo(Vector2.right));
        Assert.That(value.Direction(1800, 30), Is.EqualTo(Vector2.left));
        Assert.That(value.Direction(3599, 30), Is.EqualTo(Vector2.left));
        Assert.Throws<InvalidOperationException>(() => value.Window.Ready(2000, 30, true));
        Assert.That(value.InputProtocol, Is.EqualTo("field-right-60s-left-60s-v1"));
    }
    [Test] public void FieldWindowDoesNotRelaxOrdinaryCaptureDurationLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CaptureSegmentWindow(0, 120));
        var args = new[] { "--autoplay-capture", "--field-video", "out-return" };
        Assert.That(FeelComparisonCapture.ResolveWindow(args).DurationSeconds, Is.EqualTo(120));
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
}
