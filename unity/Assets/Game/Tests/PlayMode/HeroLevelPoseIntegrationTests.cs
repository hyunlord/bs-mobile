using System.Collections;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class HeroLevelPoseIntegrationTests
    {
        [UnityTest]
        public IEnumerator ActualLevelChangeSurvivesThreeSecondsOfPausedWorldThenRendersOnResume()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30000, catalog.Tuning.DefaultHero,
                catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var baseline = session.View.CaptureFrame();
            var wave = ((IWaveRunView)session.View).CaptureWaveRuntime();
            var snapshot = WavePresentation.Envelope(catalog, baseline, session.View.CaptureCards(), wave);
            var owner = new GameObject("Queued knight level pose");
            var cameraOwner = new GameObject("Queued knight level camera");
            var camera = cameraOwner.AddComponent<Camera>();
            var target = new RenderTexture(390, 844, 24); camera.targetTexture = target;
            try
            {
                var world = owner.AddComponent<WorldRenderer>();
                var config = CanonicalContent.Presentation.Camera;
                world.Initialize(camera, new WorldCameraSettings(config.WorldUnitsPerUnityUnit, config.MinHalfHeight, config.MaxHalfHeight,
                    config.EstatePadding, config.FollowMilliseconds, config.ZoomMilliseconds), catalog.Tuning.DefaultEstate, baseline.MapWidth, baseline.MapHeight);
                // The knight sprite sheets draw the hero by default (#268); this test checks the live knight rig, so ask for it.
                world.HeroRigExplicit = true;
                world.PrepareHeroKnight(); world.AcceptWave(catalog, wave);
                var safe = new Rect(0, 0, 390, 844);
                world.Present(baseline, baseline, snapshot, 1, 0, safe);
                Assert.That(world.HeroLevelPosePending, Is.False, "Starting level is not a level-up event.");
                var gainedLevel = baseline with { Tick = baseline.Tick + 1, Level = baseline.Level + 1 };
                world.PresentationPaused = true;
                for (var frame = 0; frame < 180; frame++) world.Present(gainedLevel, gainedLevel, snapshot, 1, 1f / 60, safe);
                Assert.That(world.HeroLevelPosePending, Is.True);
                Assert.That(world.RenderedHeroLevelPoseProgress, Is.EqualTo(-1));
                yield return null;
                world.PresentationPaused = false;
                world.Present(gainedLevel, gainedLevel, snapshot, 1, .1f, safe);
                Assert.That(world.RenderedHeroLevelPoseProgress, Is.Zero);
                world.Present(gainedLevel, gainedLevel, snapshot, 1, .1f, safe);
                Assert.That(world.RenderedHeroLevelPoseProgress, Is.GreaterThan(0));
                yield return null;
                Assert.That(world.HeroMotionLiveMetrics.DeformedSkinCount, Is.EqualTo(14));
            }
            finally
            {
                camera.targetTexture = null; target.Release();
                Object.Destroy(owner); Object.Destroy(cameraOwner); Object.Destroy(target);
            }
            yield return null;
        }
    }
}
