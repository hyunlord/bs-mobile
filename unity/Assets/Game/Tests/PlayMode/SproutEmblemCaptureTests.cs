#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class SproutEmblemCaptureTests
    {
        static readonly string[] Roles = {
            "fx.ward_orbit.shield", "icon.core.levy_banner.default", "icon.core.heirloom_service.default",
            "icon.core.rally_boundary.default", "icon.core.rally_kitchen.default", "icon.core.meal_oath.default",
            "icon.core.seed_crown.default", "person.vassal.idle", "icon.core.ruin_keystone.default",
            "icon.core.wind_seed_flag.default", "building.core.carpenter_hammer.complete",
            "building.core.carpenter_hammer.damaged", "building.core.carpenter_hammer.ruin",
            "icon.core.crop_guard_signet.default", "wave1a.wave.harvest-flag.default" };
        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        static string Output => Path.Combine(Root, "artifacts/sprout-405/actual-captures");
        static bool Enabled => Environment.GetEnvironmentVariable("SPROUT_EMBLEM_CAPTURE") == "1";

        [UnityTest]
        public IEnumerator CatalogRolesRenderBeforeAndAfterWithoutGameplayConsumers()
        {
            var registries = new[] { Resources.Load<ArtRegistry>("FirstPlayableArt"), Resources.Load<ArtRegistry>("Wave1aArt") };
            Assert.That(registries.All(r => r != null && r.manifest != null), Is.True);
            var current = registries.Select(r => ArtCatalog.FromJson(r.manifest.text, r.atlases)).ToArray();
            try
            {
                foreach (var role in Roles) Assert.That(current.Any(c => c.TryResolveRole(role, out _)), Is.True, role);
                if (!Enabled) yield break; // Normal suite validates coverage without optional local baseline files.
                Directory.CreateDirectory(Output);
                foreach (var atlas in new[] { "wave1a-weapon-fx.png", "equipment.png", "charters-evolutions.png", "actors.png", "items-a.png", "buildings.png", "items-b.png", "wave1a-growth.png" })
                    Assert.That(File.Exists(Path.Combine(Root, "artifacts/sprout-405/before", atlas)), Is.True, "Missing baseline atlas: " + atlas);
                var owned = new List<Texture2D>(); var previous = new List<ArtCatalog>();
                try
                {
                    foreach (var registry in registries)
                    {
                        var manifest = ArtCatalog.Parse(registry.manifest.text); var textures = registry.atlases.ToArray();
                        for (var i = 0; i < textures.Length; i++)
                        {
                            var file = Path.Combine(Root, "artifacts/sprout-405/before", Path.GetFileName(manifest.atlases[i].path));
                            if (!File.Exists(file)) continue;
                            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false); owned.Add(texture);
                            Assert.That(texture.LoadImage(File.ReadAllBytes(file)), Is.True, file); texture.filterMode = textures[i].filterMode;
                            textures[i] = texture;
                        }
                        previous.Add(ArtCatalog.FromJson(registry.manifest.text, textures));
                    }
                    yield return Gallery(previous.ToArray(), "before");
                    yield return Gallery(current, "after");
                    File.WriteAllLines(Path.Combine(Output, "gallery-role-order.txt"), new[] {
                        "5 columns x 3 rows, left-to-right then top-to-bottom. Actual Unity SpriteRenderer role fixture, not gameplay.",
                        "Harvest flag has no gameplay consumer. No gameplay consumer was added.",
                        "Baseline atlas injection uses current role metadata; unchanged metadata is a prerequisite." }.Concat(Roles));
                }
                finally { foreach (var catalog in previous) Release(catalog, registries); foreach (var texture in owned) Object.DestroyImmediate(texture); }
            }
            finally { foreach (var catalog in current) Release(catalog, registries); }
        }

        static void Release(ArtCatalog catalog, ArtRegistry[] registries)
        {
            foreach (var role in registries.SelectMany(r => ArtCatalog.Parse(r.manifest.text).roles).Select(r => r.id).Distinct())
                if (catalog.TryResolveRole(role, out var visual) && visual.Sprite != null) Object.DestroyImmediate(visual.Sprite);
        }

        static IEnumerator Gallery(ArtCatalog[] catalogs, string name)
        {
            var owner = new GameObject("Sprout role fixture"); var camera = owner.AddComponent<Camera>();
            var target = new RenderTexture(1000, 600, 24); camera.targetTexture = target;
            camera.orthographic = true; camera.orthographicSize = 3; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.22f, .27f, .21f); camera.cullingMask = 1 << 30;
            try
            {
                for (var i = 0; i < Roles.Length; i++)
                {
                    var visual = catalogs.First(c => c.TryResolveRole(Roles[i], out _)).ResolveRole(Roles[i]);
                    var item = new GameObject(Roles[i]); item.transform.SetParent(owner.transform, false); item.layer = 30;
                    var renderer = item.AddComponent<SpriteRenderer>(); renderer.sprite = visual.Sprite;
                    var scale = 1.65f / Mathf.Max(visual.Sprite.bounds.size.x, visual.Sprite.bounds.size.y);
                    item.transform.localScale = Vector3.one * scale;
                    var center = new Vector3(-4 + i % 5 * 2, 2 - i / 5 * 2, 10);
                    item.transform.localPosition = center - visual.Sprite.bounds.center * scale;
                }
                var path = Path.Combine(Output, "roles-15-" + name + "-fixture.png");
                yield return PlayModeCaptureGuard.WaitForShaders(path); yield return null; yield return null;
                camera.Render(); yield return null; yield return PlayModeCaptureGuard.WaitForShaders(path);
                camera.Render(); Save(target, path);
            }
            finally { camera.targetTexture = null; Object.DestroyImmediate(owner); target.Release(); Object.DestroyImmediate(target); }
        }

        [UnityTest]
        public IEnumerator FirstPlayableWorldShowsThreeHouseStatesAndVassal()
        {
            if (CanonicalContent.ProfileName != "first-playable") { Assert.That(CanonicalContent.ProfileName, Is.Not.Empty); yield break; }
            var oldProfile = ArtCatalog.ProfileName; ArtCatalog.ProfileName = "first-playable";
            try
            {
                var catalog = CanonicalContent.CreateCatalog();
                var session = new InteractiveSession(catalog, new InteractiveOptions(new RunOptions(30002, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
                var original = session.View.CaptureFrame(); var lord = original.Lord.Position;
                var states = new[] { "complete", "damaged", "ruin" };
                var art = ArtCatalog.Load(); foreach (var state in states) Assert.That(art.Resolve("building", "core:carpenter_hammer", state), Is.Not.Null);
                Assert.That(art.Resolve("person", "vassal", "idle"), Is.Not.Null);
                if (!Enabled) yield break;
                Directory.CreateDirectory(Output);
                var frame = original with {
                    Buildings = states.Select((s, i) => new BuildingView(8000 + i, "core:carpenter_hammer", new WorldPoint(lord.X + (i - 1) * 1500, lord.Y + 850), true, s == "complete" ? 100 : s == "damaged" ? 40 : 0)).ToArray(),
                    People = new[] { new PersonView(8010, "core:muster_horn", new WorldPoint(lord.X + 700, lord.Y - 300), "vassal", 1, 100) } };
                var snapshot = session.View.CaptureFirstPlayable() with {
                    BuildingProgress = states.Select((s, i) => new BuildingProgressView(8000 + i, s, 100, 100)).ToArray(),
                    People = new[] { new PersonActivityView(8010, "idle") } };
                var owner = new GameObject("Sprout actual world fixture"); var cameraOwner = new GameObject("Sprout world camera");
                var camera = cameraOwner.AddComponent<Camera>(); var target = new RenderTexture(900, 1600, 24); camera.targetTexture = target;
                try
                {
                    Assert.That(Object.FindObjectsByType<WorldRenderer>(FindObjectsSortMode.None), Is.Empty);
                    var world = owner.AddComponent<WorldRenderer>(); var settings = CanonicalContent.Presentation.Camera;
                    world.Initialize(camera, new WorldCameraSettings(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), catalog.Tuning.DefaultEstate, frame.MapWidth, frame.MapHeight);
                    var path = Path.Combine(Output, "first-playable-house-states-vassal-fixture.png");
                    yield return PlayModeCaptureGuard.WaitForShaders(path);
                    for (var i = 0; i < 5; i++) { world.Present(frame, frame, snapshot, 1, 1f / 60, new Rect(0, 0, 900, 1600)); yield return null; }
                    yield return PlayModeCaptureGuard.WaitForShaders(path);
                    world.Present(frame, frame, snapshot, 1, 0, new Rect(0, 0, 900, 1600)); yield return null;
                    Assert.That(world.SubmittedInstances, Is.GreaterThan(3)); Save(target, path);
                    File.WriteAllText(path + ".txt", "Actual WorldRenderer; presentation-state fixture, not normal play. Left-to-right house states complete/damaged/ruin; vassal idle. Canonical profile=" + CanonicalContent.ProfileName);
                }
                finally { camera.targetTexture = null; Object.DestroyImmediate(owner); Object.DestroyImmediate(cameraOwner); target.Release(); Object.DestroyImmediate(target); }
            }
            finally { ArtCatalog.ProfileName = oldProfile; }
        }

        static void Save(RenderTexture target, string path)
        {
            var active = RenderTexture.active; var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try
            {
                RenderTexture.active = target; var probe = PlayModeCaptureGuard.Begin(path);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                Assert.That(image.GetPixels32().Select(c => (c.r >> 4) * 256 + (c.g >> 4) * 16 + (c.b >> 4)).Distinct().Count(), Is.GreaterThan(20));
                PlayModeCaptureGuard.Check(image, probe, path); File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally { RenderTexture.active = active; Object.DestroyImmediate(image); }
        }
    }
}
#endif
