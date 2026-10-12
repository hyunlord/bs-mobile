using System;
using Game.App;
using NUnit.Framework;

public sealed class HeroMotionComparisonTests
{
    [Test] public void OrdinaryExecutionDoesNotSelectEitherPrototype() => Assert.That(HeroMotionComparison.Parse(Array.Empty<string>()), Is.Null);
    [TestCase("baked", 1), TestCase("live", 2)]
    public void BothModesOnlySelectPresentation(string mode, int expected)
    {
        var parsed = HeroMotionComparison.Parse(new[] { "--hero-motion", mode });
        Assert.That(parsed.Mode, Is.EqualTo(mode)); Assert.That(parsed.RenderMode, Is.EqualTo(expected));
    }
    [Test] public void AllowsIdenticalReplayAndCaptureArgumentsForBothModes()
    {
        foreach (var mode in new[] { "baked", "live" })
            Assert.That(HeroMotionComparison.Parse(new[] { "--hero-motion", mode, "--wave-benchmark", "same.ssreplay", "--crowd-replay-benchmark", "--offline-replay-capture" }), Is.Not.Null);
    }
    [TestCase("other"), TestCase("")]
    public void RejectsUnknownMode(string mode) => Assert.Throws<ArgumentException>(() => HeroMotionComparison.Parse(new[] { "--hero-motion", mode }));
    [Test] public void RejectsMissingValue() => Assert.Throws<ArgumentException>(() => HeroMotionComparison.Parse(new[] { "--hero-motion" }));
    [Test] public void RejectsDuplicateMode() => Assert.Throws<ArgumentException>(() => HeroMotionComparison.Parse(new[] { "--hero-motion", "baked", "--hero-motion", "live" }));
    [TestCase("--hero-concept"), TestCase("--feel-comparison"), TestCase("--feel-trial")]
    public void RejectsChangingPartsOrRules(string flag) => Assert.Throws<ArgumentException>(() => HeroMotionComparison.Parse(new[] { "--hero-motion", "live", flag, "test" }));
    [TestCase("early", 0, 900), TestCase("evolved", 10800, 11530)]
    public void WindowsSelectOriginalReplayTicksWithoutEditingCommands(string window, int start, int end)
    {
        foreach (var mode in new[] { "baked", "live" })
        {
            var parsed = HeroMotionComparisonWindow.Parse(new[] { "--hero-motion", mode, "--hero-motion-window", window, "--crowd-replay-benchmark" });
            Assert.That(parsed.StartTick, Is.EqualTo(start)); Assert.That(parsed.EndTick, Is.EqualTo(end));
            Assert.That(OfflineReplayCapture.ExpectedFrames(end - start, 30), Is.EqualTo((end - start) * 2));
        }
    }
    [Test] public void OrdinaryCrowdBenchmarkKeepsHistoricalWindow() => Assert.That(HeroMotionComparisonWindow.Parse(new[] { "--crowd-replay-benchmark" }), Is.Null);
    [Test] public void WindowCannotSilentlyOverrideOrdinaryBenchmark() => Assert.Throws<ArgumentException>(() => HeroMotionComparisonWindow.Parse(new[] { "--hero-motion-window", "early", "--crowd-replay-benchmark" }));
}
