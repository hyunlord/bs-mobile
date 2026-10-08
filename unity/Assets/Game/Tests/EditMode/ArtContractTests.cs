using System;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Game.Tests
{
    public sealed class ArtContractTests
    {
        static ArtManifest Fixture() => new ArtManifest
        {
            schemaVersion = 1,
            licenses = new[] { new ArtLicenseDefinition { id = "generated", origin = "built-in imagegen", source = "test output", prompt = "knight", commercialUse = "OpenAI output terms" } },
            atlases = new[] { new ArtAtlasDefinition { id = "actors", path = "Assets/Art/actors.png", width = 64, height = 64, padding = 2, license = "generated" } },
            roles = new[] { new ArtRoleDefinition { id = "hero", atlas = "actors", frames = new[] { new ArtPixelRect { x = 2, y = 4, width = 20, height = 30 } }, pivot = new Vector2(0.5f, 0), worldSize = new Vector2(0.48f, 0.6f), frameMs = 350, tween = "walk" } },
            bindings = new[] { new ArtBindingDefinition { kind = "hero", contentId = "core:frontier_knight", state = "walk", roleId = "hero" } }
        };
        [Test] public void ResolvesRealIdentityWithTopLeftPixelConversionAndCachedSprite()
        {
            var texture = new Texture2D(64, 64); Sprite sprite = null;
            try
            {
                var catalog = ArtCatalog.FromJson(JsonUtility.ToJson(Fixture()), new[] { texture });
                var art = catalog.Resolve("hero", "core:frontier_knight", "walk"); sprite = art.Sprite;
                Assert.That(art.UvRects[0].x, Is.EqualTo(2f / 64));
                Assert.That(art.UvRects[0].y, Is.EqualTo(30f / 64));
                Assert.That(art.UvRects[0].height, Is.EqualTo(30f / 64));
                Assert.That(art.Pivot, Is.EqualTo(new Vector2(0.5f, 0)));
                Assert.That(catalog.ResolveRole("hero"), Is.SameAs(art));
                Assert.That(catalog.Resolve("hero", "core:frontier_knight", "walk").Sprite, Is.SameAs(sprite));
                Assert.That(Assert.Throws<InvalidOperationException>(() => catalog.Resolve("hero", "core:frontier_knight", "death")).Message, Does.Contain("hero|core:frontier_knight|death"));
            }
            finally { if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        }
        [Test] public void CacheInvalidationReloadsUpdatedRegistryInSameEditorDomain()
        {
            const string temporaryRoot = "Assets/__ArtCatalogCacheTest";
            var registry = Resources.Load<ArtRegistry>("FirstPlayableArt");
            var createdRegistry = registry == null;
            var createdTemporaryFolder = false;
            var originalManifest = registry != null ? registry.manifest : null;
            var originalAtlases = registry != null ? registry.atlases : null;
            var firstTexture = new Texture2D(64, 64);
            var secondTexture = new Texture2D(64, 64);
            var firstManifest = new TextAsset(JsonUtility.ToJson(Fixture()));
            var replacement = Fixture();
            replacement.roles[0].frames[0].x = 12;
            replacement.roles[0].worldSize = new Vector2(0.7f, 0.9f);
            var secondManifest = new TextAsset(JsonUtility.ToJson(replacement));
            Sprite firstSprite = null, secondSprite = null;
            try
            {
                if (createdRegistry)
                {
                    Assert.That(AssetDatabase.IsValidFolder(temporaryRoot), Is.False);
                    AssetDatabase.CreateFolder("Assets", "__ArtCatalogCacheTest");
                    createdTemporaryFolder = true;
                    AssetDatabase.CreateFolder(temporaryRoot, "Resources");
                    registry = ScriptableObject.CreateInstance<ArtRegistry>();
                    AssetDatabase.CreateAsset(registry, temporaryRoot + "/Resources/FirstPlayableArt.asset");
                }
                registry.manifest = firstManifest; registry.atlases = new[] { firstTexture };
                ArtCatalog.InvalidateCache();
                var first = ArtCatalog.Load();
                firstSprite = first.ResolveRole("hero").Sprite;
                registry.manifest = secondManifest; registry.atlases = new[] { secondTexture };
                Assert.That(ArtCatalog.Load(), Is.SameAs(first), "Lookup must stay cached until the authoring boundary invalidates it.");
                ArtCatalog.InvalidateCache();
                var second = ArtCatalog.Load();
                var visual = second.Resolve("hero", "core:frontier_knight", "walk"); secondSprite = visual.Sprite;
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(visual.Texture, Is.SameAs(secondTexture));
                Assert.That(visual.UvRects[0].x, Is.EqualTo(12f / 64));
                Assert.That(visual.WorldSize, Is.EqualTo(new Vector2(0.7f, 0.9f)));
                Assert.That(secondSprite, Is.Not.SameAs(firstSprite));
                Assert.That(ArtCatalog.Load(), Is.SameAs(second));
                Assert.That(firstSprite != null, Is.True, "Existing consumers retain their prior immutable catalog until recreated.");
            }
            finally
            {
                ArtCatalog.InvalidateCache();
                if (registry != null) { registry.manifest = originalManifest; registry.atlases = originalAtlases; }
                if (createdTemporaryFolder) AssetDatabase.DeleteAsset(temporaryRoot);
                if (firstSprite != null) UnityEngine.Object.DestroyImmediate(firstSprite);
                if (secondSprite != null) UnityEngine.Object.DestroyImmediate(secondSprite);
                UnityEngine.Object.DestroyImmediate(firstManifest); UnityEngine.Object.DestroyImmediate(secondManifest);
                UnityEngine.Object.DestroyImmediate(firstTexture); UnityEngine.Object.DestroyImmediate(secondTexture);
            }
        }
        [Test] public void RejectsGameplayFieldsBeforeJsonUtilityCanDiscardThem()
        {
            var json = JsonUtility.ToJson(Fixture()).Replace("\"frameMs\":350", "\"frameMs\":350,\"damage\":42");
            Assert.Throws<InvalidOperationException>(() => ArtCatalog.Parse(json));
        }
        [Test] public void RejectsDuplicatesMissingRoleAndAtlasPadding()
        {
            var m = Fixture(); m.bindings = new[] { m.bindings[0], m.bindings[0] };
            Assert.That(Assert.Throws<InvalidOperationException>(() => ArtCatalog.Validate(m)).Message, Does.Contain("hero|core:frontier_knight|walk"));
            m = Fixture(); m.bindings[0].roleId = "missing";
            Assert.That(Assert.Throws<InvalidOperationException>(() => ArtCatalog.Validate(m)).Message, Does.Contain("missing"));
            m = Fixture(); m.roles[0].frames[0].x = 0;
            Assert.That(Assert.Throws<InvalidOperationException>(() => ArtCatalog.Validate(m)).Message, Does.Contain("padding"));
        }
        [Test] public void RejectsUnresolvedLicense()
        {
            var m = Fixture(); m.atlases[0].license = "unknown";
            Assert.Throws<InvalidOperationException>(() => ArtCatalog.Validate(m));
        }
    }
}
