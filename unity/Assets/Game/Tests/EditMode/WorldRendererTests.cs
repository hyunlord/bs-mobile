using System;
using NUnit.Framework;
using SowSiege.Core;
using Game.View;
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
        }

        [Test] public void ExperienceRendersBelowReadyAlliesAttacksAndEnemies()
        {
            int Layer(string name) => (int)typeof(WorldRenderer).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetRawConstantValue();
            var experience = Layer("ExperienceLayer");
            Assert.That(experience, Is.GreaterThan(Layer("GrowthLayer")));
            foreach (var name in new[] { "ReadyLayer", "AllyOutlineLayer", "AllyFillLayer", "AttackLayer", "EnemyLayer", "LordOutlineLayer" })
                Assert.That(experience, Is.LessThan(Layer(name)), name + " must remain readable above XP.");
            Assert.That(Layer("EnemyLayer"), Is.GreaterThan(Layer("AttackLayer")));
        }

        [Test] public void CameraSettingsRejectInvalidCanonicalRanges()
        {
            Assert.Throws<ArgumentException>(() => new WorldCameraSettings(0, 2400, 6000, 600, 120, 800));
            Assert.Throws<ArgumentException>(() => new WorldCameraSettings(1000, 6000, 2400, 600, 120, 800));
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
