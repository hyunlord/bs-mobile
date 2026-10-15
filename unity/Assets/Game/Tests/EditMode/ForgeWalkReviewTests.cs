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
