using System;
using Game.App;
using NUnit.Framework;

public sealed class CameraScaleComparisonTests
{
    [TestCase("10",600),TestCase("30",1800)]
    public void KnightProductionDurationIsBounded(string seconds,int frames)
    {
        var value = CameraScaleComparison.Parse(new[] { "--knight-review","--wave-benchmark","same.ssreplay","--camera-scale-window","mid","--m1-review-seconds",seconds });
        Assert.That(OfflineReplayCapture.ExpectedFrames(value.EndTick-value.StartTick,30),Is.EqualTo(frames));
    }
    [TestCase("9"),TestCase("60")]
    public void UnboundedProductionDurationRejected(string seconds)
        => Assert.Throws<ArgumentException>(()=>CameraScaleComparison.Parse(new[] { "--knight-review","--wave-benchmark","same.ssreplay","--m1-review-seconds",seconds }));
    [Test] public void OrphanProductionDurationRejected()
        => Assert.Throws<ArgumentException>(()=>CameraScaleComparison.Parse(new[] { "--m1-review-seconds","10" }));

    static string[] Args(string scale = "1", string window = "early") => new[] { "--camera-scale", scale, "--camera-scale-window", window, "--wave-benchmark", "same.ssreplay", "--hero-motion", "live" };
    [Test] public void OrdinaryBenchmarkUnchanged() => Assert.That(CameraScaleComparison.Parse(new[] { "--wave-benchmark", "same.ssreplay" }), Is.Null);
    [TestCase("1", 1f), TestCase("1.4", 1.4f), TestCase("1.8", 1.8f)]
    public void ExactlyThreeApprovedScales(string input, float expected) => Assert.That(CameraScaleComparison.Parse(Args(input)).Magnification, Is.EqualTo(expected));
    [TestCase("early", 0, 0), TestCase("mid", 10800, 10770), TestCase("dense", 13500, 13470)]
    public void BoundedWindowsHaveOneSecondHistoryAndExactly1800Frames(string name, int start, int preRoll)
    {
        var value = CameraScaleComparison.Parse(Args("1", name));
        Assert.That(value.StartTick, Is.EqualTo(start)); Assert.That(value.PreRollTick, Is.EqualTo(preRoll));
        var frames = 0;
        for (var tick = preRoll; tick <= value.EndTick; tick++)
            for (var half = 0; half < 2; half++)
                if ((tick < value.EndTick || half == 0) && value.CaptureFrame(tick, half * .5f)) frames++;
        Assert.That(frames, Is.EqualTo(1800));
        Assert.That(OfflineReplayCapture.ExpectedFrames(value.EndTick - value.StartTick, 30), Is.EqualTo(1800));
    }
    [TestCase("0"), TestCase("1.2"), TestCase("NaN"), TestCase("Infinity")]
    public void InvalidScaleRejected(string value) => Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(Args(value)));
    [Test] public void InvalidWindowRejected() => Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(Args("1", "other")));
    [TestCase("--crowd-replay-benchmark"), TestCase("--hero-motion-window"), TestCase("--impact-feedback-baseline"), TestCase("--wave-benchmark-mode"), TestCase("--wave-benchmark-repeat")]
    public void OtherConditionChangesRejected(string flag)
    {
        var args = new System.Collections.Generic.List<string>(Args()) { flag, "test" };
        Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(args.ToArray()));
    }
    [Test] public void MissingAndDuplicateFlagsRejected()
    {
        Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(new[] { "--camera-scale", "1" }));
        var args = new System.Collections.Generic.List<string>(Args()) { "--camera-scale", "1.4" };
        Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(args.ToArray()));
    }
    [Test] public void FrozenCameraReviewCanCaptureButOrdinaryC05Cannot()
    {
        var args = new System.Collections.Generic.List<string>(Args()) { "--offline-replay-capture" };
        Assert.That(OfflineReplayCapture.Requested(args.ToArray()), Is.True);
        Assert.Throws<ArgumentException>(() => OfflineReplayCapture.Requested(new[] { "--wave-benchmark", "same.ssreplay", "--offline-replay-capture" }));
    }
}
