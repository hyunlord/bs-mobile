using System;
using Game.App;
using NUnit.Framework;
using UnityEngine;

public sealed class FeelComparisonCaptureTests
{
    static FeelComparisonCapture Mode(string mode) => FeelComparisonCapture.Parse(new[] { "--autoplay-capture", "--feel-comparison", mode });
    [Test] public void DefaultCaptureRemainsInactive() => Assert.That(FeelComparisonCapture.Parse(Array.Empty<string>()), Is.Null);
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
