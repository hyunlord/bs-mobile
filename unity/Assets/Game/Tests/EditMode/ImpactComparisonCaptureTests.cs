using System;
using Game.App;
using NUnit.Framework;

public sealed class ImpactComparisonCaptureTests
{
    static ImpactComparisonCapture Mode(string mode) => ImpactComparisonCapture.Parse(new[] { "--autoplay-capture", "--impact-comparison", mode });
    [Test] public void ComparisonOnlyChangesPresentationAndUsesSameThirtySeconds()
    {
        var before = Mode("baseline"); var after = Mode("applied");
        Assert.That(before.Enabled, Is.False); Assert.That(after.Enabled, Is.True);
        Assert.That(before.Window.StartSeconds, Is.EqualTo(60)); Assert.That(after.Window.StartSeconds, Is.EqualTo(60));
        Assert.That(before.Window.DurationSeconds, Is.EqualTo(30)); Assert.That(after.Window.DurationSeconds, Is.EqualTo(30));
        Assert.That(after.Window.EndSeconds, Is.EqualTo(90));
        Assert.That(before.InputProtocol, Is.EqualTo(after.InputProtocol));
        for (var tick = 0; tick <= 2700; tick++) Assert.That(before.Direction(tick, 30), Is.EqualTo(after.Direction(tick, 30)));
        Assert.That(AutoplayCapture.IsGoldenMinuteRequested(new[] { "--autoplay-capture", "--impact-comparison", "applied" }), Is.True);
        Assert.That(FeelComparisonCapture.ResolveWindow(new[] { "--autoplay-capture", "--impact-comparison", "applied" }).DurationSeconds, Is.EqualTo(30));
    }
    [Test] public void OrdinaryExecutionHasNoComparisonOverride() => Assert.That(ImpactComparisonCapture.Parse(Array.Empty<string>()), Is.Null);
    [Test] public void ComparisonRequiresIsolatedCapture() => Assert.Throws<ArgumentException>(() => ImpactComparisonCapture.Parse(new[] { "--impact-comparison", "baseline" }));
    [TestCase("--feel-comparison"), TestCase("--feel-trial"), TestCase("--field-video"), TestCase("--capture-duration-seconds"), TestCase("--capture-movement"), TestCase("--capture-weak-build")]
    public void RejectsModesThatCouldChangeInputOrRules(string flag) => Assert.Throws<ArgumentException>(() => ImpactComparisonCapture.Parse(new[] { "--autoplay-capture", "--impact-comparison", "applied", flag, "1" }));
    [TestCase("unknown"), TestCase("")]
    public void RejectsInvalidMode(string mode) => Assert.Throws<ArgumentException>(() => Mode(mode));
}
