using System;
using System.IO;
using Game.App;
using NUnit.Framework;

public sealed class KnightReviewTests
{
    static string[] Args(params string[] extra)
    {
        var args = new System.Collections.Generic.List<string> { "--knight-review", "--wave-benchmark", "fixed.ssreplay" };
        args.AddRange(extra); return args.ToArray();
    }
    [Test] public void KnightDefaultsToSelectedScaleAndEarlyWindow()
    {
        var review = KnightReview.Parse(Args()); var camera = CameraScaleComparison.Parse(Args());
        Assert.That(review.ImpactFeedback && review.EdgeWarnings, Is.True);
        Assert.That(camera.Magnification, Is.EqualTo(1.4f)); Assert.That(camera.EndTick, Is.EqualTo(900));
    }
    [Test] public void BaselineOnlyChangesPresentationAndExplicitOriginalScaleIsAvailable()
    {
        var args = Args("--impact-feedback-baseline", "--edge-warnings-off", "--camera-scale", "1");
        var review = KnightReview.Parse(args); var camera = CameraScaleComparison.Parse(args);
        Assert.That(review.ImpactFeedback || review.EdgeWarnings, Is.False);
        Assert.That(camera.Magnification, Is.EqualTo(1f));
    }
    [Test] public void DistinctDamageSampleHasItsOwnIdentityAnd143TickWindow()
    {
        var args = Args("--knight-damage-replay");
        var review = KnightReview.Parse(args); var camera = CameraScaleComparison.Parse(args);
        Assert.That(camera.Window, Is.EqualTo("damage")); Assert.That(camera.StartTick, Is.EqualTo(13500));
        Assert.That(camera.EndTick, Is.EqualTo(13643));
        Assert.That(OfflineReplayCapture.ExpectedFrames(camera.EndTick - camera.StartTick, 30), Is.EqualTo(286));
        Assert.DoesNotThrow(() => review.Validate(KnightReview.DamageSha256, 13643, KnightReview.DamageTerminalHash));
        Assert.Throws<InvalidDataException>(() => review.Validate(KnightReview.C05Sha256, 13643, KnightReview.DamageTerminalHash));
        Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(Args("--knight-damage-replay", "--camera-scale-window", "dense")));
    }
    [TestCase("--hero-motion"), TestCase("--feel-trial"), TestCase("--crowd-replay-benchmark")]
    public void CannotCombineDifferentHeroOrRules(string flag) => Assert.Throws<ArgumentException>(() => KnightReview.Parse(Args(flag, "live")));
    [Test] public void DefaultModeRemainsUnselectedAndOrphanFlagsFail()
    {
        Assert.That(KnightReview.Parse(Array.Empty<string>()), Is.Null);
        Assert.Throws<ArgumentException>(() => KnightReview.Parse(new[] { "--edge-warnings-off" }));
        Assert.Throws<ArgumentException>(() => KnightReview.Parse(new[] { "--knight-review" }));
        Assert.Throws<ArgumentException>(() => CameraScaleComparison.Parse(Args("--camera-scale", "1.8")));
    }
}
