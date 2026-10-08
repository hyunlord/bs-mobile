using System.Collections;
using System.IO;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class WorldRenderTests
    {
        [UnityTest]
        public IEnumerator InstancedShaderPreservesPalettePerInstanceColorAndTransparency()
        {
            var owner = new GameObject("Instanced color camera"); var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 64, 24); camera.targetTexture = target;
            using (var meshes = new ShapeMeshes())
            using (var batch = new ShapeBatch(meshes[WorldShape.Square], Resources.Load<Shader>("WorldShape"), camera, 50))
            {
                for (var frame = 0; frame < 3; frame++)
                {
                    batch.BeginFrame();
                    batch.Add(Vector2.left, Vector2.one * 0.4f, 0, GamePalette.Enemy);
                    batch.Add(Vector2.right, Vector2.one * 0.4f, 0, new Color(0, 0, 1, 0.5f));
                    batch.Flush(); yield return null;
                }
                var before = RenderTexture.active; RenderTexture.active = target;
                var capture = new Texture2D(128, 64, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 128, 64), 0, 0); capture.Apply(); RenderTexture.active = before;
                var enemy = capture.GetPixel(32, 32); var blue = capture.GetPixel(96, 32);
                Assert.That(enemy.r, Is.EqualTo(GamePalette.Enemy.r).Within(2f / 255));
                Assert.That(enemy.g, Is.EqualTo(GamePalette.Enemy.g).Within(2f / 255));
                Assert.That(enemy.b, Is.EqualTo(GamePalette.Enemy.b).Within(2f / 255));
                Assert.That(blue.b, Is.InRange(0.2f, 0.9f), "Half-alpha blue must blend against black.");
                Assert.That(blue.r, Is.LessThan(0.1f)); Object.Destroy(capture);
            }
            camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstancedBatchSplits1023SubmissionsAndResetsForNextFrame()
        {
            var owner = new GameObject("Instanced batch test camera");
            var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.transform.position = new Vector3(0, 0, -10);
            var shader = Resources.Load<Shader>("WorldShape");
            using (var meshes = new ShapeMeshes())
            using (var batch = new ShapeBatch(meshes[WorldShape.Diamond], shader, camera, 50))
            {
                batch.BeginFrame();
                for (var index = 0; index < 1023; index++)
                    batch.Add(new Vector2(index % 33 * 0.1f, index / 33 * 0.1f), Vector2.one * 0.03f, 0, GamePalette.Enemy);
                batch.Flush();
                Assert.That(batch.SubmittedInstances, Is.EqualTo(1023));
                Assert.That(batch.DrawCalls, Is.EqualTo(3), "1,023 instances require 511 + 511 + 1 submissions.");
                yield return null;
                batch.BeginFrame();
                batch.Add(Vector2.zero, Vector2.one * 0.1f, 0, GamePalette.Lord);
                batch.Flush();
                Assert.That(batch.SubmittedInstances, Is.EqualTo(1));
                Assert.That(batch.DrawCalls, Is.EqualTo(1));
                yield return null;
            }
            Object.Destroy(owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstancedWorldProducesActualPixelsWithoutEntityGameObjects()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            session.Apply(new(0, 0, ReplayCommandKind.Advance));
            var frame = session.View.CaptureFrame();
            var lord = frame.Lord.Position;
            var enemyPoints = new[] { new WorldPoint(lord.X - 900, lord.Y), new WorldPoint(lord.X, lord.Y + 900), new WorldPoint(lord.X + 900, lord.Y) };
            var enemies = new EnemyView[enemyPoints.Length];
            var kinds = new[] { PresentationKind.KillExperience, PresentationKind.HarvestExperience, PresentationKind.TaxExperience };
            var experience = new PresentationEvent[5];
            PresentationEvent Experience(long id, PresentationKind kind, WorldPoint position) => new(id, frame.Tick, kind, "readability-test", position, new WorldPoint(0, 0), "", 0, 1,
                System.Array.AsReadOnly(System.Array.Empty<WorldPoint>()), System.Array.AsReadOnly(System.Array.Empty<int>()));
            experience[0] = Experience(0, PresentationKind.HarvestExperience, lord);
            for (var index = 0; index < enemies.Length; index++)
            {
                enemies[index] = new EnemyView(index, "readability-test", enemyPoints[index], 1, 1);
                experience[index + 1] = Experience(index + 1, kinds[index], enemyPoints[index]);
            }
            var unobstructedExperience = new WorldPoint(lord.X, lord.Y - 900);
            experience[4] = Experience(4, PresentationKind.HarvestExperience, unobstructedExperience);
            frame = new RunFrame(frame.Tick, frame.Status, frame.Season, frame.SeasonTicksRemaining, frame.DurationTicks, frame.TickRate, frame.MapWidth, frame.MapHeight, frame.Lord,
                frame.Estate, frame.Experience, frame.RequiredExperience, frame.Level, frame.EstateExtent, new EntityCounts(enemies.Length, frame.Counts.People, frame.Counts.Farms, frame.Counts.Buildings, 0),
                System.Array.AsReadOnly(enemies), frame.Farms, frame.Buildings, frame.People, frame.Loot, frame.Remains, frame.Equipment, System.Array.AsReadOnly(experience));
            var cameraObject = new GameObject("Instanced world test camera"); var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(360, 720, 24); camera.targetTexture = target;
            var owner = new GameObject("World renderer test"); var world = owner.AddComponent<WorldRenderer>();
            var settings = CanonicalContent.Presentation.Camera;
            world.Initialize(camera, new(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds));
            var safe = new Rect(0, 0, 360, 720);
            for (var index = 0; index < 3; index++) { world.Present(frame, frame, 1, 0, safe); yield return null; }
            Assert.That(owner.transform.childCount, Is.Zero, "Entities must not create GameObjects.");
            Assert.That(world.SubmittedInstances, Is.GreaterThan(frame.Enemies.Count)); Assert.That(world.DrawCalls, Is.GreaterThan(0));
            var previous = RenderTexture.active; RenderTexture.active = target;
            var capture = new Texture2D(360, 720, TextureFormat.RGB24, false); capture.ReadPixels(new Rect(0, 0, 360, 720), 0, 0); capture.Apply(); RenderTexture.active = previous;
            Color Pixel(WorldPoint point)
            {
                var screen = camera.WorldToScreenPoint(new Vector3((float)point.X / settings.WorldUnitsPerUnityUnit, (float)point.Y / settings.WorldUnitsPerUnityUnit, 0));
                return capture.GetPixel(Mathf.FloorToInt(screen.x), Mathf.FloorToInt(screen.y));
            }
            for (var index = 0; index < enemyPoints.Length; index++)
            {
                var pixel = Pixel(enemyPoints[index]);
                Assert.That(pixel.r, Is.EqualTo(GamePalette.Enemy.r).Within(2f / 255), kinds[index] + " must not cover an enemy.");
                Assert.That(pixel.g, Is.EqualTo(GamePalette.Enemy.g).Within(2f / 255));
                Assert.That(pixel.b, Is.EqualTo(GamePalette.Enemy.b).Within(2f / 255));
            }
            var visibleExperience = Pixel(unobstructedExperience);
            Assert.That(visibleExperience.r, Is.EqualTo(GamePalette.HarvestXp.r).Within(2f / 255), "Unobstructed XP must still render.");
            Assert.That(visibleExperience.g, Is.EqualTo(GamePalette.HarvestXp.g).Within(2f / 255));
            Assert.That(visibleExperience.b, Is.EqualTo(GamePalette.HarvestXp.b).Within(2f / 255));
            var center = capture.GetPixel(180, 360); var edge = capture.GetPixel(4, 4);
            Assert.That(center.r, Is.EqualTo(GamePalette.Lord.r).Within(2f / 255), "Experience visuals must not cover the lord.");
            Assert.That(center.g, Is.EqualTo(GamePalette.Lord.g).Within(2f / 255));
            Assert.That(center.b, Is.EqualTo(GamePalette.Lord.b).Within(2f / 255));
            Assert.That(Mathf.Abs(center.r - edge.r) + Mathf.Abs(center.g - edge.g) + Mathf.Abs(center.b - edge.b), Is.GreaterThan(0.05f), "Actual world shader must draw the central lord.");
#if UNITY_EDITOR
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/unity")); Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "instanced-world-portrait.png"), capture.EncodeToPNG());
#endif
            camera.targetTexture = null; Object.Destroy(owner); Object.Destroy(cameraObject); Object.Destroy(capture); target.Release(); Object.Destroy(target);
            yield return null;
        }
    }
}
