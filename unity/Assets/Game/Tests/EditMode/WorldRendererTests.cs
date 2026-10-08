using System;
using NUnit.Framework;
using SowSiege.Core;
using Game.View;
using Game.App.Generated;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class WorldRendererTests
    {
        private static PresentationEvent Event(long id, string shape = "rays") => new(id, 0, PresentationKind.Attack, "test", new(0, 0), new(1, 0), shape, 1000, 0,
            Array.AsReadOnly(Array.Empty<WorldPoint>()), Array.AsReadOnly(Array.Empty<int>()), new(0, 20, 1, 0, Array.AsReadOnly(new[] { new WorldPoint(1000, 0) })));

        [Test] public void EffectPoolDeduplicatesRepeatedPausedFramesAndRemainsBounded()
        {
            var effects = new WorldEffects();
            effects.Accept(new[] { Event(0), Event(1) }); effects.Accept(new[] { Event(0), Event(1) });
            Assert.That(effects.ActiveCount, Is.EqualTo(2)); Assert.That(effects.ActiveVisualProjectiles, Is.EqualTo(2));
            var burst = new PresentationEvent[WorldEffects.Capacity + 1];
            for (var index = 0; index < burst.Length; index++) burst[index] = Event(index + 2);
            effects.Accept(burst);
            Assert.That(effects.ActiveCount, Is.EqualTo(WorldEffects.Capacity)); Assert.That(effects.DroppedCount, Is.GreaterThan(0));
            effects.Advance(10); Assert.That(effects.ActiveCount, Is.Zero);
        }

        [Test] public void UnsupportedCoreShapesAreReportedInsteadOfPretendingToRender()
        {
            var effects = new WorldEffects(); effects.Accept(new[] { Event(0, "unknown-future-shape") });
            Assert.That(effects.UnsupportedShapeCount, Is.EqualTo(1)); Assert.That(effects.ActiveCount, Is.Zero);
        }

        [Test] public void InstancedSubmissionLimitIsAtMost511()
        {
            Assert.That(ShapeBatch.MaximumInstances, Is.InRange(1, 511));
            Assert.That(SpriteBatch.MaximumInstances, Is.EqualTo(511));
        }

        [Test] public void ExperienceRendersBelowReadyAlliesAttacksAndEnemies()
        {
            var experience = GameVisualTokens.ExperienceLayer;
            Assert.That(experience, Is.GreaterThan(GameVisualTokens.GrowthLayer));
            foreach (var layer in new[] { GameVisualTokens.ReadyLayer, GameVisualTokens.AllyLayer, GameVisualTokens.AttackLayer, GameVisualTokens.EnemyLayer, GameVisualTokens.LordLayer })
                Assert.That(experience, Is.LessThan(layer), "XP must not cover actors or ripe growth.");
            Assert.That(GameVisualTokens.EnemyLayer, Is.GreaterThan(GameVisualTokens.AttackLayer));
        }

        [Test] public void AllFirstPlayableFormsAreAcceptedAndCallbacksAreDeduplicated()
        {
            var effects = new WorldEffects();
            var forms = new[] { "sector90", "sector180", "projectile", "piercing", "boomerang", "chain", "orbit", "field", "volley", "nova" };
            var events = new PresentationEvent[forms.Length];
            for (var i = 0; i < forms.Length; i++) events[i] = Event(i, forms[i]);
            var notifications = 0;
            effects.Accept(events, _ => notifications++); effects.Accept(events, _ => notifications++);
            Assert.That(effects.UnsupportedShapeCount, Is.Zero);
            Assert.That(effects.ActiveCount, Is.EqualTo(forms.Length));
            Assert.That(notifications, Is.EqualTo(forms.Length));
        }

        [Test] public void VisualProjectileTelemetryExcludesOnlySuppressedPersistentEvents()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            var snapshot = session.View.CaptureFirstPlayable();
            Assert.That(snapshot, Is.Not.Null);
            var persistentEvent = Event(0, "projectile") with { SourceId = "test:persistent" };
            var allyEvent = Event(1, "projectile") with { SourceId = "test:ally" };
            var frame = session.View.CaptureFrame() with { Events = new[] { persistentEvent, allyEvent } };
            snapshot = snapshot with { Attacks = new[] {
                new ActiveAttackView(10, "test:persistent", "projectile", new(10, 0), new(0, 0), 20, 1, 10),
                new ActiveAttackView(11, "test:persistent", "projectile", new(20, 0), new(10, 0), 20, 1, 10)
            } };
            var owner = new GameObject("Projectile telemetry test");
            try
            {
                var world = owner.AddComponent<WorldRenderer>();
                world.AcceptFrame(frame, snapshot);
                Assert.That(world.ActivePersistentAttacks, Is.EqualTo(2));
                Assert.That(world.ActiveVisualProjectiles, Is.EqualTo(3), "Two persistent attacks plus the independent ally event; the suppressed event is not rendered.");
                world.AcceptFrame(frame, snapshot);
                Assert.That(world.ActiveVisualProjectiles, Is.EqualTo(3), "Repeated paused frames must not duplicate transient events.");
                snapshot = snapshot with { Attacks = Array.Empty<ActiveAttackView>() };
                world.AcceptFrame(frame with { Tick = frame.Tick + 1 }, snapshot);
                Assert.That(world.ActiveVisualProjectiles, Is.EqualTo(2), "When no persistent source is present, both still-live event cues are rendered and counted.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test] public void HitFeedbackExpiresBeforeExperienceWithoutDroppingOtherSignals()
        {
            var hit = Event(0) with { Kind = PresentationKind.Damage };
            var xp = Event(1) with { Kind = PresentationKind.KillExperience };
            var effects = new WorldEffects(); effects.Accept(new[] { hit, xp });
            effects.Advance(GameVisualTokens.HitFlashSeconds + 0.01f);
            Assert.That(effects.ActiveCount, Is.EqualTo(1));
            Assert.That(effects[0].Event.Kind, Is.EqualTo(PresentationKind.KillExperience));
            effects.Advance(GameVisualTokens.ExperienceSeconds);
            Assert.That(effects.ActiveCount, Is.Zero);
        }

        [Test] public void CameraSettingsRejectInvalidCanonicalRanges()
        {
            Assert.Throws<ArgumentException>(() => new WorldCameraSettings(0, 2400, 6000, 600, 120, 800));
            Assert.Throws<ArgumentException>(() => new WorldCameraSettings(1000, 6000, 2400, 600, 120, 800));
        }

        [TestCase(900, 1600)]
        [TestCase(1080, 1080)]
        public void MapCornersRetainWholeActorArtWithoutHalfEmptyViewport(int width, int height)
        {
            var owner = new GameObject("Map edge camera test"); var camera = owner.AddComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, width, height);
            var settings = new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800);
            try
            {
                foreach (var corner in new[] { Vector2.zero, new Vector2(24, 0), new Vector2(0, 24), new Vector2(24, 24) })
                {
                    var follow = new RunCamera(camera, settings); follow.SetMapBounds(24000, 24000);
                    follow.Present(corner, 6000, 0);
                    foreach (var offset in new[] { new Vector2(-0.4f, -0.08f), new Vector2(0.4f, 0.94f) })
                    {
                        var viewport = camera.WorldToViewportPoint(corner + offset);
                        Assert.That(viewport.x, Is.InRange(0f, 1f)); Assert.That(viewport.y, Is.InRange(0f, 1f), "Feet-pivot hero/boss art must fit at every map corner.");
                    }
                    var halfHeight = camera.orthographicSize; var halfWidth = halfHeight * camera.aspect;
                    Assert.That(camera.transform.position.x - halfWidth, Is.GreaterThanOrEqualTo(-GameVisualTokens.CameraOutsideMargin - 0.001f));
                    Assert.That(camera.transform.position.x + halfWidth, Is.LessThanOrEqualTo(24 + GameVisualTokens.CameraOutsideMargin + 0.001f));
                    Assert.That(camera.transform.position.y - halfHeight, Is.GreaterThanOrEqualTo(-GameVisualTokens.CameraOutsideMargin - 0.001f));
                    Assert.That(camera.transform.position.y + halfHeight, Is.LessThanOrEqualTo(24 + GameVisualTokens.CameraOutsideMargin + 0.001f));
                    Assert.That(0.6f * height / (2 * halfHeight), Is.GreaterThanOrEqualTo(79.9f), "A square viewport must retain the portrait actor pixel scale.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test] public void MapCameraReclampsViewportWhenAspectChanges()
        {
            var owner = new GameObject("Map camera resize test"); var camera = owner.AddComponent<Camera>();
            var follow = new RunCamera(camera, new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800));
            follow.SetMapBounds(24000, 24000);
            try
            {
                camera.pixelRect = new Rect(0, 0, 900, 1600); follow.Present(new Vector2(24, 24), 6000, 0);
                follow.SetVisualOffset(new Vector2(0.03f, 0.02f));
                camera.pixelRect = new Rect(0, 0, 1080, 1080); follow.Present(new Vector2(24, 24), 6000, 0);
                Assert.That(camera.orthographicSize, Is.LessThanOrEqualTo(4.051f));
                Assert.That(camera.transform.position.x + camera.orthographicSize * camera.aspect, Is.LessThanOrEqualTo(25.001f));
                Assert.That(camera.transform.position.y + camera.orthographicSize, Is.LessThanOrEqualTo(25.001f));
                var before = camera.transform.position; follow.SetVisualOffset(Vector2.zero);
                Assert.That(camera.transform.position, Is.EqualTo(before), "Resize presentation must already remove the previous shake.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        [Test] public void CameraShakeOffsetDoesNotAccumulateIntoFollowPosition()
        {
            var owner = new GameObject("Camera shake test"); var camera = owner.AddComponent<Camera>();
            var follow = new RunCamera(camera, new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800));
            var lord = new Vector2(4, 7); var offset = new Vector2(0.03f, -0.02f);
            follow.Present(lord, 2000, 0); follow.SetVisualOffset(offset); follow.SetVisualOffset(offset);
            Assert.That(camera.transform.position.x, Is.EqualTo(lord.x + offset.x).Within(0.0001f));
            for (var i = 0; i < 30; i++) { follow.Present(lord, 2000, 1f / 60); follow.SetVisualOffset(offset); }
            follow.Present(lord, 2000, 0); follow.SetVisualOffset(Vector2.zero);
            Assert.That(camera.transform.position.x, Is.EqualTo(lord.x).Within(0.0001f));
            Assert.That(camera.transform.position.y, Is.EqualTo(lord.y).Within(0.0001f));
            UnityEngine.Object.DestroyImmediate(owner);
        }

        [Test] public void ThreatBehindHudStillGetsMarkerInsideUsableWorld()
        {
            var owner = new GameObject("HUD threat camera test"); var camera = owner.AddComponent<Camera>();
            camera.pixelRect = new Rect(0, 0, 400, 800);
            camera.orthographic = true; camera.orthographicSize = 4; camera.transform.position = new Vector3(0, 0, -10);
            var safe = new Rect(0, 0, 400, 600);
            var enemyPoint = camera.ScreenToWorldPoint(new Vector3(200, 700, 10));
            var enemy = new EnemyView(1, "under-hud", new WorldPoint(Mathf.RoundToInt(enemyPoint.x * 1000), Mathf.RoundToInt(enemyPoint.y * 1000)), 1, 1);
            var markers = new ThreatMarkers();
            markers.Update(camera, new[] { enemy }, new WorldPoint(0, 0), 1000, safe);
            Assert.That(markers.Count, Is.EqualTo(1));
            Assert.That(safe.Contains(markers[0].ScreenPosition), Is.True);
            Assert.That(markers[0].ScreenPosition.y, Is.LessThan(safe.yMax));
            UnityEngine.Object.DestroyImmediate(owner);
        }

        [Test] public void ThreatMarkersClusterAndRespectProvidedSafeRectangle()
        {
            var cameraObject = new GameObject("Threat camera test"); var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.transform.position = new(0, 0, -10);
            var markers = new ThreatMarkers();
            var enemies = new EnemyView[100];
            for (var index = 0; index < enemies.Length; index++) enemies[index] = new(index, "test", new(100000, index), 1, 1);
            var safe = new Rect(40, 40, Mathf.Max(100, Screen.width - 80), Mathf.Max(100, Screen.height - 80));
            markers.Update(camera, enemies, new WorldPoint(0, 0), 1000, safe);
            Assert.That(markers.Count, Is.EqualTo(1)); Assert.That(safe.Contains(markers[0].ScreenPosition), Is.True); Assert.That(markers[0].ClusterCount, Is.EqualTo(100));
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
