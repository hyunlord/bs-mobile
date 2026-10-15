using System;
using System.Collections.Generic;
using System.IO;
using Game.App;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

public sealed class ForgeWalkReviewTests
{
    string directory, replayPath;
    const string Digest = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    [SetUp] public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        replayPath = Path.Combine(directory, "forge-walk.ssreplay");
        File.WriteAllText(Path.ChangeExtension(replayPath, ".json"), JsonUtility.ToJson(new ForgeWalkReview.Definition
        { contract = "forge-walk-review-v1", profile = "wave-1a", seed = 30001, direction = "E", cameraScale = 1.4f, endTick = 900, dataHash = Digest, stateHash = Digest, replaySha256 = Digest }));
        File.WriteAllText(Path.Combine(directory, "manifest.json"), "{}");
    }
    [TearDown] public void TearDown() => Directory.Delete(directory, true);
    string[] Args() => new[] { "--forge-walk-review", "--wave-benchmark", replayPath, "--hero-production", Path.Combine(directory, "manifest.json"), "--hero-production-direction", "E", "--offline-replay-capture" };
    [Test] public void OrdinaryLaunchDoesNotEnableWalkingPilot() => Assert.That(ForgeWalkReview.Parse(Array.Empty<string>()), Is.Null);
    [Test] public void CutoutBaselineNeedsTheExplicitReviewAndCannotLoadExternalParts()
    {
        var args = BaselineArgs();
        Assert.That(ForgeWalkReview.Parse(args).CutoutBaseline, Is.True);
        Assert.That(M1HeroProductionReview.Parse(args), Is.Null);
        Assert.That(CameraScaleComparison.Parse(args).Magnification, Is.EqualTo(1.4f));
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(new[] { "--forge-cutout-baseline" }));
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(new List<string>(Args()) { "--forge-cutout-baseline" }.ToArray()));
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(new List<string>(args) { "--forge-cutout-baseline" }.ToArray()));
    }
    string[] BaselineArgs() => new[] { "--forge-walk-review", "--forge-cutout-baseline", "--wave-benchmark", replayPath, "--offline-replay-capture" };
    [Test] public void HeightReferenceIsMeasuredBaselineOfTheSameReplay()
    {
        var path = Path.Combine(directory, "baseline.json");
        File.WriteAllText(path, JsonUtility.ToJson(new ForgeWalkReview.HeightReference
        { contract = "forge-cutout-height-v1", fixture = "forge-walk-review-v1", hero = "a-knight-cutout-baseline", replaySha256 = Digest,
            replayVerified = true, cameraScale = 1.4f, startTick = 0, endTick = 900, bodyHeightSamples = 1800, medianBodyHeightWorld = .7f, medianBodyHeightPixels = 130 }));
        var args = new List<string>(Args()) { "--forge-height-reference", path };
        Assert.That(ForgeWalkReview.Parse(args.ToArray()).Height.medianBodyHeightWorld, Is.EqualTo(.7f));
        File.WriteAllText(path, File.ReadAllText(path).Replace("a-knight-cutout-baseline", "3d-baked"));
        Assert.Throws<InvalidDataException>(() => ForgeWalkReview.Parse(args.ToArray()));
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(new List<string>(BaselineArgs()) { "--forge-height-reference", path }.ToArray()));
    }
    [Test] public void PilotFixesCameraWindowDirectionAndOfflineClock()
    {
        var args = Args();
        Assert.That(ForgeWalkReview.Parse(args).Case.endTick, Is.EqualTo(900));
        Assert.That(M1HeroProductionReview.Parse(args).WalkingPilot, Is.True);
        Assert.That(CameraScaleComparison.Parse(args).Magnification, Is.EqualTo(1.4f));
        Assert.That(CameraScaleComparison.Parse(args).StartTick, Is.Zero);
        Assert.That(CameraScaleComparison.Parse(args).EndTick, Is.EqualTo(900));
        Assert.That(OfflineReplayCapture.Requested(args), Is.True);
    }
    [TestCase("--knight-review"), TestCase("--m1-combat-case"), TestCase("--hero-production-terminal-tail"), TestCase("--autoplay-capture"), TestCase("--wave-benchmark-mode"), TestCase("--camera-scale")]
    public void PilotCannotWeakenAnotherReviewOrPerformanceContract(string conflicting)
    {
        var args = new List<string>(Args()) { conflicting };
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(args.ToArray()));
    }
    [Test] public void MissingOfflineOrDuplicateFlagIsRejected()
    {
        var args = new List<string>(Args()); args.Remove("--offline-replay-capture");
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(args.ToArray()));
        args = new List<string>(Args()) { "--forge-walk-review" };
        Assert.Throws<ArgumentException>(() => ForgeWalkReview.Parse(args.ToArray()));
    }
    [Test] public void PilotRejectsUnapprovedDirectionsAndActions()
    {
        var manifest = WalkingManifest();
        Assert.DoesNotThrow(() => WorldRenderer.ValidateWalkingPilot(manifest, "E"));
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateWalkingPilot(manifest, "follow"));
        manifest.clips[0].mirror = true;
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateWalkingPilot(manifest, "E"));
        manifest.clips[0].mirror = false; manifest.clips[0].id = "slash-light";
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateWalkingPilot(manifest, "E"));
    }
    [Test] public void EqualHeightUsesPixelEdgesAndRejectsPaddingAsBodyHeight()
    {
        var clip = WalkingManifest().clips[0];
        clip.bodyTopPixels = new float[16]; clip.bodyBottomPixels = new float[16];
        for (var i = 0; i < 16; i++) { clip.bodyTopPixels[i] = 40; clip.bodyBottomPixels[i] = 440; }
        Assert.DoesNotThrow(() => WorldRenderer.ValidateBodyLandmarks(clip, 512));
        clip.bodyHeightPixels = 401;
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateBodyLandmarks(clip, 512));
        clip.bodyHeightPixels = 400; clip.bodyTopPixels[0] = float.NaN;
        Assert.Throws<InvalidDataException>(() => WorldRenderer.ValidateBodyLandmarks(clip, 512));
    }
    [Test] public void CutoutMeasurementExcludesTransparentPaddingAndTracksActualBonePose()
    {
        var texture = new Texture2D(3, 2, TextureFormat.RGBA32, false);
        try
        {
            var clear = new Color32(0, 0, 0, 0); var opaque = new Color32(255, 255, 255, 255);
            texture.SetPixels32(new[] { clear, opaque, opaque, opaque, clear, clear }); texture.Apply();
            var data = new HeroLiveRigData { jointNames = new[] { "head", "farKnee", "nearKnee" }, sprites = new[] {
                new HeroLivePart { bone = "head", rect = new float[] { 0, 0, 1, 2 }, anchor = new float[] { 0, 0 }, scale = 1 },
                new HeroLivePart { bone = "farKnee", rect = new float[] { 1, 0, 1, 2 }, anchor = new float[] { 1, 1 }, scale = 1 },
                new HeroLivePart { bone = "nearKnee", rect = new float[] { 2, 0, 1, 2 }, anchor = new float[] { 2, 1 }, scale = 1 } } };
            var measure = new HeroHeadFootMeasure(data, texture);
            var pose = new[] { new HeroLiveJoint { y = 10 }, new HeroLiveJoint { y = 20 }, new HeroLiveJoint { y = 20 } };
            Assert.That(measure.Measure(pose), Is.EqualTo(new Vector2(10, 21)));
            pose[2].y = 25;
            Assert.That(measure.Measure(pose), Is.EqualTo(new Vector2(10, 26)));
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    [Test] public void WalkingPilotNeverDereferencesAnUnavailableSlashOrChangesDirection()
    {
        var go = new GameObject("walking-pilot-test");
        try
        {
            var world = go.AddComponent<WorldRenderer>();
            world.EditorPrepareProductionAttackTest(WalkingManifest(), true);
            world.EditorAcceptProductionAttack(new WaveEvent(1, 30, "attack", "core:iron_blade", 42,
                new WorldPoint(0, 0), new WorldPoint(-100, 100), 0), 1);
            Assert.That(world.EditorResolveProductionAttack("E", 0, out var slash), Is.False);
            Assert.That(slash, Is.Null);
            Assert.That(world.HeroProductionDirection, Is.EqualTo("E"));
            Assert.That(world.HeroProductionTargetId, Is.EqualTo(42), "Actual Core attack evidence stays available.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    [Test] public void PilotValidatesNormalInputAndCatalogRatherThanTrustingSidecarAlone()
    {
        var pilot = ForgeWalkReview.Parse(Args());
        var commands = new List<ReplayCommand>();
        for (var tick = 0; tick < 900; tick++) commands.Add(new ReplayCommand(tick, tick, ReplayCommandKind.Advance, new PlayerInput(1000, 0)));
        var options = new InteractiveOptions(new RunOptions(30001, "test:hero", "test:estate", "mixed", ManualCards: true), AimMode.Movement, Digest,
            Feel: new FeelExperimentOptions(FasterLord: true), Field: new FieldOptions());
        var replay = new ReplayDocument(ReplayCodec.Header(options), commands, Array.Empty<ReplayCheckpoint>(), new ReplayEnd(900, 900, ReplayEndKind.Quit, Digest));
        Assert.DoesNotThrow(() => pilot.ValidateReplay(replay, Digest, Digest));
        Assert.Throws<InvalidDataException>(() => pilot.ValidateReplay(replay, Digest, new string('B', 64)));
        commands[10] = new ReplayCommand(10, 10, ReplayCommandKind.Advance, new PlayerInput(0, 1000));
        Assert.Throws<InvalidDataException>(() => pilot.ValidateReplay(replay, Digest, Digest));
        commands[10] = new ReplayCommand(10, 10, ReplayCommandKind.GrantLevel);
        Assert.Throws<InvalidDataException>(() => pilot.ValidateReplay(replay, Digest, Digest));
    }
    static HeroProductionManifest WalkingManifest() => new HeroProductionManifest
    {
        schemaVersion = 1, method = "3d-baked", clips = new[] { new HeroProductionClip
        { id = "walk", direction = "E", texture = "walk.png", columns = 4, rows = 4, frames = 16, fps = 12, pivotX = .5f, pivotY = .1f, bodyHeightPixels = 400, headHeightPixels = 56 } }
    };
}
