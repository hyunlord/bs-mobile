using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class FirstPlayableApprovedWalkTests
    {
        [TestCase("bell_thief", 40, .5f, .512422360f)]
        [TestCase("whistle_hunter", 40, .5f, .414596273f)]
        [TestCase("stone_breaker", 45, .5f, .559006211f)]
        [TestCase("rope_catcher", 40, .5f, .503105590f)]
        [TestCase("ember_sapper", 40, .4609375f, .521739130f)]
        public void FirstPlayableBindingLoadsApprovedFullCells(string enemy, int frameMs, float pivotX, float size)
        {
            var registry = Resources.Load<Game.View.ArtRegistry>("FirstPlayableArt");
            var catalog = Game.View.ArtCatalog.FromJson(registry.manifest.text, registry.atlases);
            var walk = catalog.Resolve("enemy", "core:" + enemy, "walk");
            Assert.That(walk.Texture.name, Is.EqualTo("first-playable-enemy-walks-429"));
            Assert.That(walk.UvRects.Count, Is.EqualTo(16));
            Assert.That(walk.FrameMs, Is.EqualTo(frameMs));
            Assert.That(walk.CellPixels, Is.EqualTo(new Vector2(256, 256)));
            Assert.That(walk.Pivot, Is.EqualTo(new Vector2(pivotX, .12109375f)));
            Assert.That(walk.WorldSize.x, Is.EqualTo(size).Within(.000001f));
            Assert.That(walk.CyclePixels, Is.Zero, "approved contract uses frameMs, not an invented stride");
            foreach (var state in new[] { "idle", "hit", "death" })
                Assert.That(catalog.Resolve("enemy", "core:" + enemy, state).Texture, Is.SameAs(walk.Texture),
                    "A state transition must not split the Y-sorted enemy batch.");
        }

        [Test]
        public void HistoricalAndApprovedEnemiesShareOneTextureInEveryState()
        {
            var registry = Resources.Load<Game.View.ArtRegistry>("FirstPlayableArt");
            var catalog = Game.View.ArtCatalog.FromJson(registry.manifest.text, registry.atlases);
            var texture = catalog.Resolve("enemy", "core:stone_breaker", "walk").Texture;
            foreach (var enemy in new[] { "seed_mite", "crop_grazer", "raider", "shield_raider",
                "ram_runner", "locust_cloud", "siege_cart", "winter_hart", "stone_breaker",
                "rope_catcher", "ember_sapper", "bell_thief", "whistle_hunter" })
                foreach (var state in new[] { "idle", "walk", "hit", "death" })
                    Assert.That(catalog.Resolve("enemy", "core:" + enemy, state).Texture,
                        Is.SameAs(texture), enemy + "/" + state);
        }
    }
}
