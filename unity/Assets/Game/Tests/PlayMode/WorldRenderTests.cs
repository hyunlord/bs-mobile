using System.Collections;
using System.IO;
using System.Linq;
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
        public IEnumerator SpriteBatchDrawsDistinctAtlasRectsAlphaAnd511Splits()
        {
            var owner = new GameObject("Sprite atlas camera"); var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 64, 24); camera.targetTexture = target;
            var atlas = new Texture2D(4, 2, TextureFormat.RGBA32, false);
            atlas.SetPixels(new[] { Color.red, Color.red, new Color(0, 1, 0, 0.5f), new Color(0, 1, 0, 0.5f), Color.red, Color.red, new Color(0, 1, 0, 0.5f), new Color(0, 1, 0, 0.5f) }); atlas.Apply();
            using (var batch = new SpriteBatch(atlas, Resources.Load<Shader>("WorldSprite"), camera, GameVisualTokens.EnemyLayer))
            {
                batch.BeginFrame();
                for (var i = 0; i < 1023; i++) batch.Add(new Vector2(100, 100), Vector2.one, Vector2.one * 0.5f, new Rect(0, 0, 0.5f, 1), 0, Color.white);
                batch.Flush();
                Assert.That(batch.SubmittedInstances, Is.EqualTo(1023)); Assert.That(batch.DrawCalls, Is.EqualTo(3));
                yield return null;
                for (var frame = 0; frame < 3; frame++)
                {
                    batch.BeginFrame();
                    batch.Add(Vector2.left, Vector2.one * 0.8f, Vector2.one * 0.5f, new Rect(0, 0, 0.5f, 1), 0, new Color(0.6f, 1, 1, 1));
                    batch.Add(Vector2.right, Vector2.one * 0.8f, Vector2.one * 0.5f, new Rect(0.5f, 0, 0.5f, 1), 0, Color.white);
                    batch.Add(Vector2.zero, Vector2.one * 0.4f, Vector2.one * 0.5f, new Rect(0, 0, 0.5f, 1), 0, Color.white, 1);
                    batch.Flush(); yield return null;
                }
                Assert.That(batch.SubmittedInstances, Is.EqualTo(3)); Assert.That(batch.DrawCalls, Is.EqualTo(1));
                var before = RenderTexture.active; RenderTexture.active = target;
                var capture = new Texture2D(128, 64, TextureFormat.RGB24, false); capture.ReadPixels(new Rect(0, 0, 128, 64), 0, 0); capture.Apply(); RenderTexture.active = before;
                var left = capture.GetPixel(32, 32); var right = capture.GetPixel(96, 32);
                Assert.That(left.r, Is.EqualTo(0.6f).Within(0.08f), "Per-instance tint must preserve sRGB color."); Assert.That(left.g, Is.LessThan(0.05f));
                var flash = capture.GetPixel(64, 32);
                Assert.That(flash.r, Is.GreaterThan(0.95f)); Assert.That(flash.g, Is.GreaterThan(0.95f)); Assert.That(flash.b, Is.GreaterThan(0.95f));
                Assert.That(right.g, Is.InRange(0.2f, 0.9f)); Assert.That(right.r, Is.LessThan(0.05f), "Per-instance UV must sample the second atlas cell.");
                Object.Destroy(capture);
            }
            camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(atlas); Object.Destroy(owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator InstancedWorldProducesActualPixelsWithoutEntityGameObjects()
        {
            var catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            session.Apply(new(0, 0, ReplayCommandKind.Advance));
            var frame = session.View.CaptureFrame();
            var firstPlayable = session.View.CaptureFirstPlayable();
            Assert.That(firstPlayable, Is.Not.Null);
            var enemyIds = catalog.Enemies.Keys.Take(3).ToArray();
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
                enemies[index] = new EnemyView(index, enemyIds[index], enemyPoints[index], 1, 1);
                experience[index + 1] = Experience(index + 1, kinds[index], enemyPoints[index]);
            }
            var unobstructedExperience = new WorldPoint(lord.X, lord.Y - 900);
            experience[4] = Experience(4, PresentationKind.KillExperience, unobstructedExperience);
            frame = new RunFrame(frame.Tick, frame.Status, frame.Season, frame.SeasonTicksRemaining, frame.DurationTicks, frame.TickRate, frame.MapWidth, frame.MapHeight, frame.Lord,
                frame.Estate, frame.Experience, frame.RequiredExperience, frame.Level, frame.EstateExtent, new EntityCounts(enemies.Length, frame.Counts.People, frame.Counts.Farms, frame.Counts.Buildings, 0),
                System.Array.AsReadOnly(enemies), frame.Farms, frame.Buildings, frame.People, frame.Loot, frame.Remains, frame.Equipment, System.Array.AsReadOnly(experience));
            var cameraObject = new GameObject("Instanced world test camera"); var camera = cameraObject.AddComponent<Camera>();
            var target = new RenderTexture(360, 720, 24); camera.targetTexture = target;
            var owner = new GameObject("World renderer test"); var world = owner.AddComponent<WorldRenderer>();
            var settings = CanonicalContent.Presentation.Camera;
            world.Initialize(camera, new(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
            var safe = new Rect(0, 0, 360, 720);
            for (var index = 0; index < 3; index++) { world.Present(frame, frame, firstPlayable, 1, 0, safe); yield return null; }
            Assert.That(owner.transform.childCount, Is.Zero, "Entities must not create GameObjects.");
            Assert.That(world.SubmittedInstances, Is.GreaterThan(frame.Enemies.Count)); Assert.That(world.DrawCalls, Is.GreaterThan(0));
            var previous = RenderTexture.active; RenderTexture.active = target;
            var capture = new Texture2D(360, 720, TextureFormat.RGB24, false); capture.ReadPixels(new Rect(0, 0, 360, 720), 0, 0); capture.Apply(); RenderTexture.active = previous;
#if UNITY_EDITOR
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/unity")); Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "instanced-world-portrait.png"), capture.EncodeToPNG());
#endif
            var varied = capture.GetPixels().Select(c => new Color32((byte)(c.r * 15), (byte)(c.g * 15), (byte)(c.b * 15), 255)).Distinct().Count();
            Assert.That(varied, Is.GreaterThan(20), "Authored world must draw textured multicolor pixels.");
#if UNITY_EDITOR
            void AssertAuthoredPixel(ArtVisual visual, WorldPoint point, string message, Texture2D background = null)
            {
                var path = UnityEditor.AssetDatabase.GetAssetPath(visual.Texture);
                var original = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Assert.That(original.LoadImage(File.ReadAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)))), Is.True);
                var uv = visual.UvRects[0];
                var origin = new Vector2((float)point.X / settings.WorldUnitsPerUnityUnit, (float)point.Y / settings.WorldUnitsPerUnityUnit);
                var lower = camera.WorldToScreenPoint(origin - Vector2.Scale(visual.Pivot, visual.WorldSize));
                var upper = camera.WorldToScreenPoint(origin + Vector2.Scale(Vector2.one - visual.Pivot, visual.WorldSize));
                var matched = 0; var eligible = 0;
                for (var y = Mathf.Max(0, Mathf.CeilToInt(lower.y)); y < Mathf.Min(capture.height, Mathf.FloorToInt(upper.y)); y++)
                for (var x = Mathf.Max(0, Mathf.CeilToInt(lower.x)); x < Mathf.Min(capture.width, Mathf.FloorToInt(upper.x)); x++)
                {
                    var local = new Vector2((x + 0.5f - lower.x) / (upper.x - lower.x), (y + 0.5f - lower.y) / (upper.y - lower.y));
                    var expected = original.GetPixelBilinear(uv.x + local.x * uv.width, uv.y + local.y * uv.height);
                    if (expected.a < (background == null ? 0.99f : 0.15f)) continue;
                    var underlying = background == null ? Color.clear : background.GetPixel(x, y);
                    if (background != null) expected = Color.Lerp(underlying, expected, expected.a);
                    eligible++;
                    var actual = capture.GetPixel(x, y);
                    if (background != null && Mathf.Abs(actual.r - underlying.r) + Mathf.Abs(actual.g - underlying.g) + Mathf.Abs(actual.b - underlying.b) < 0.03f) continue;
                    if (Mathf.Abs(actual.r - expected.r) + Mathf.Abs(actual.g - expected.g) + Mathf.Abs(actual.b - expected.b) < 0.35f) matched++;
                }
                Object.Destroy(original);
                Assert.That(matched, Is.GreaterThan(2), message + ": actual GPU pixels must match authored opaque texels above XP; eligible=" + eligible + ", matched=" + matched + ", role=" + visual.RoleId + ", uv=" + uv + ", worldSize=" + visual.WorldSize);
            }
            for (var i = 0; i < enemyPoints.Length; i++) AssertAuthoredPixel(ArtCatalog.Load().Resolve("enemy", enemyIds[i], "idle"), enemyPoints[i], kinds[i].ToString());
            AssertAuthoredPixel(ArtCatalog.Load().Resolve("hero", firstPlayable.HeroId, "idle"), lord, "Hero");
            Object.Destroy(owner); yield return null;
            owner = new GameObject("World without XP control"); world = owner.AddComponent<WorldRenderer>();
            world.Initialize(camera, new(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
            var withoutExperience = new RunFrame(frame.Tick, frame.Status, frame.Season, frame.SeasonTicksRemaining, frame.DurationTicks, frame.TickRate, frame.MapWidth, frame.MapHeight, frame.Lord,
                frame.Estate, frame.Experience, frame.RequiredExperience, frame.Level, frame.EstateExtent, frame.Counts,
                frame.Enemies, frame.Farms, frame.Buildings, frame.People, frame.Loot, frame.Remains, frame.Equipment, System.Array.AsReadOnly(System.Array.Empty<PresentationEvent>()));
            for (var index = 0; index < 3; index++) { world.Present(withoutExperience, withoutExperience, firstPlayable, 1, 0, safe); yield return null; }
            previous = RenderTexture.active; RenderTexture.active = target;
            var background = new Texture2D(360, 720, TextureFormat.RGB24, false); background.ReadPixels(new Rect(0, 0, 360, 720), 0, 0); background.Apply(); RenderTexture.active = previous;
            AssertAuthoredPixel(ArtCatalog.Load().Resolve("feedback", "kill-experience", "default"), unobstructedExperience, "Unobstructed translucent XP", background);
            Object.Destroy(background);
#endif
            camera.targetTexture = null; Object.Destroy(owner); Object.Destroy(cameraObject); Object.Destroy(capture); target.Release(); Object.Destroy(target);
            yield return null;
        }
    }
}
