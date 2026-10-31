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
                world.HitFeedbackEnabled = true;
                var snapshot = WavePresentation.Envelope(catalog, frame, session.View.CaptureCards(), source);
                var heroHit = new PresentationEvent(1, frame.Tick, PresentationKind.LordHit, "test", point,
                    default, "hit", 0, 777, System.Array.Empty<WorldPoint>(), System.Array.Empty<int>());
                world.ShowDamageNumbers = false;
                world.AcceptFrame(frame with { Events = new[] { heroHit } }, snapshot);
                Assert.That(HasVisibleAmount(owner, "777"), Is.False);
                world.ShowDamageNumbers = true;
                world.AcceptFrame(frame with { Events = new[] { heroHit with { Id = 2 } } }, snapshot);
                Assert.That(HasVisibleAmount(owner, "777"), Is.True);
                world.ShowDamageNumbers = false;
                Assert.That(HasVisibleAmount(owner, "777"), Is.False);
                world.ShowDamageNumbers = true;
                world.ShowTransientText = false;
                world.AcceptFrame(frame with { Events = new[] { heroHit with { Id = 3 } } }, snapshot);
                Assert.That(HasVisibleAmount(owner, "777"), Is.False);
                world.ShowTransientText = true;
                var wave = source with { Events = new[] { new WaveEvent(100000, frame.Tick, "hit", "test", 17, point, point, 10) } };
                world.AcceptWave(catalog, wave);
                var particles = world.ActiveImpactParticles;
                Assert.That(particles, Is.InRange(3, 5));
                world.AcceptWave(catalog, wave);
                Assert.That(world.ActiveImpactParticles, Is.EqualTo(particles));
                string groupSource = null;
                foreach (var gear in catalog.WaveRuntime.Gear) { groupSource = gear.Key; break; }
                Assert.That(groupSource, Is.Not.Null);
                var groupHit = new WaveEvent(100001, frame.Tick, "group-hit", groupSource, 19, point, point, 7);
                world.AcceptWave(catalog, wave with { Events = new[] { groupHit } });
                Assert.That(world.ActiveImpactParticles, Is.GreaterThan(particles), "Ally attacks produce real enemy feedback.");
                var groupParticles = world.ActiveImpactParticles;
                world.AcceptWave(catalog, wave with { Events = new[] { groupHit } });
                Assert.That(world.ActiveImpactParticles, Is.EqualTo(groupParticles), "Same event is never applied twice.");
                world.AcceptWave(catalog, wave with { Events = new[] {
                    groupHit with { Id = 100002, Kind = "trained-group-hit", SubjectId = 20 } } });
                Assert.That(world.ActiveImpactParticles, Is.GreaterThan(groupParticles));
                groupParticles = world.ActiveImpactParticles;
                world.AcceptWave(catalog, wave with { Events = new[] {
                    groupHit with { Id = 100003, Source = catalog.WaveRuntime.BossId, SubjectId = 21 },
                    groupHit with { Id = 100004, Amount = 0, SubjectId = 22 } } });
                Assert.That(world.ActiveImpactParticles, Is.EqualTo(groupParticles), "Enemy damage to an ally and zero damage are not enemy hits.");
                var bossSlow = 0f; world.BossDeathSlowRequested = seconds => bossSlow = seconds;
                var globalStop = 0f; world.BossHitStopRequested = seconds => globalStop = seconds;
                world.AcceptWave(catalog, wave with { Events = new[] {
                    new WaveEvent(100005, frame.Tick, "enemy-spawn", catalog.WaveRuntime.BossId, 17, point, point, 0),
                    new WaveEvent(100006, frame.Tick, "hit", "test", 17, point, point, 10) } });
                Assert.That(globalStop, Is.Zero, "M1-3 holds only the struck enemy, including bosses.");
                world.AcceptWave(catalog, wave with { Events = new[] {
                    new WaveEvent(100007, frame.Tick, "enemy-killed", catalog.WaveRuntime.BossId, 17, point, point, 8) } });
                Assert.That(bossSlow, Is.EqualTo(.3f));
                var bigStop = 0f; world.HitStopRequested = seconds => bigStop = seconds;
                string evolution = null;
                // An evolution whose hits land at once (blade and chain hits are deferred until their sweep or strike shows).
                foreach (var pair in catalog.WaveRuntime.Evolutions)
                    if (System.Array.IndexOf(pair.Value.InputIds, "core:iron_blade") < 0 && System.Array.IndexOf(pair.Value.InputIds, "core:storm_fork") < 0) { evolution = pair.Key; break; }
                Assert.That(evolution, Is.Not.Null);
                world.AcceptWave(catalog, wave with { Events = new[] { new WaveEvent(100008, frame.Tick, "hit", "test", 23, point, point, 10) } });
                Assert.That(bigStop, Is.Zero, "An ordinary hit never stops the display.");
                world.AcceptWave(catalog, wave with { Events = new[] { new WaveEvent(100009, frame.Tick, "hit", evolution, 23, point, point, 10) } });
                Assert.That(bigStop, Is.EqualTo(HitReaction.BigHitStopSeconds), "An evolution's hit asks for the short display hitstop.");
                Assert.That(globalStop, Is.Zero, "The boss stop stays the boss's own.");
                world.ImpactFeedbackEnabled = false;
                Assert.That(world.ActiveImpactParticles, Is.Zero);
                world.AcceptWave(catalog, wave with { Events = new[] {
                    new WaveEvent(100010, frame.Tick, "hit", "test", 18, point, point, 12) } });
                Assert.That(world.ActiveImpactParticles, Is.Zero);
                Assert.That(session.View.CaptureFrame().Tick, Is.EqualTo(frame.Tick));
                yield return null;
            }
            finally { Object.Destroy(owner); Object.Destroy(cameraOwner); texture.Release(); Object.Destroy(texture); }
        }

        static bool HasVisibleAmount(GameObject owner, string amount)
        {
            foreach (var label in owner.GetComponentsInChildren<TextMesh>())
                if (label.text == amount) return true;
            return false;
        }
    }
}
#endif
