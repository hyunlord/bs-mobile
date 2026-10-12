#if UNITY_EDITOR
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
    public sealed class WaveImpactFeedbackTests
    {
        [UnityTest]
        public IEnumerator WaveEventsBridgeOnceAndOptOutRestoresBaselineWithoutChangingFrames()
        {
            if (CanonicalContent.ProfileName != "wave-1a") Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new InteractiveOptions(
                new RunOptions(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true),
                AimMode.Movement, CanonicalContent.DataHash));
            var frame = session.View.CaptureFrame();
            var source = ((IWaveRunView)session.View).CaptureWaveRuntime();
            var point = frame.Lord.Position;
            var owner = new GameObject("Wave impact bridge QA");
            var cameraOwner = new GameObject("Wave impact camera");
            var camera = cameraOwner.AddComponent<Camera>();
            var texture = new RenderTexture(390, 844, 24); camera.targetTexture = texture;
            try
            {
                var world = owner.AddComponent<WorldRenderer>(); var config = CanonicalContent.Presentation.Camera;
                world.Initialize(camera, new WorldCameraSettings(config.WorldUnitsPerUnityUnit, config.MinHalfHeight,
                    config.MaxHalfHeight, config.EstatePadding, config.FollowMilliseconds, config.ZoomMilliseconds),
                    catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
                var wave = source with { Events = new[] { new WaveEvent(100000, frame.Tick, "hit", "test", 17, point, point, 10) } };
                world.AcceptWave(catalog, wave);
                var particles = world.ActiveImpactParticles;
                Assert.That(particles, Is.InRange(3, 5));
                world.AcceptWave(catalog, wave);
                Assert.That(world.ActiveImpactParticles, Is.EqualTo(particles));
                var bossSlow = 0f; world.BossDeathSlowRequested = seconds => bossSlow = seconds;
                world.AcceptWave(catalog, wave with { Events = new[] {
                    new WaveEvent(100001, frame.Tick, "enemy-killed", catalog.WaveRuntime.BossId, 17, point, point, 8) } });
                Assert.That(bossSlow, Is.EqualTo(.3f));
                world.ImpactFeedbackEnabled = false;
                Assert.That(world.ActiveImpactParticles, Is.Zero);
                world.AcceptWave(catalog, wave with { Events = new[] {
                    new WaveEvent(100002, frame.Tick, "hit", "test", 18, point, point, 12) } });
                Assert.That(world.ActiveImpactParticles, Is.Zero);
                Assert.That(session.View.CaptureFrame().Tick, Is.EqualTo(frame.Tick));
                yield return null;
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }
    }
}
#endif
