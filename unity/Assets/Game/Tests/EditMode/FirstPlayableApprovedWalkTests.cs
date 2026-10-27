using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public sealed class FirstPlayableApprovedWalkTests
    {
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
                Assert.That(catalog.Resolve("enemy", "core:" + enemy, state).Texture, Is.Not.SameAs(walk.Texture));
        }
    }
}
