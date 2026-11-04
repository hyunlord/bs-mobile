using System;
using System.Linq;
using Game.App;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests
{
    public sealed class WorldWalletPresentationTests
    {
        [Test]
        public void HeldNamesNeverExposeServerIdentifiersOrInventMissingAmounts()
        {
            var held = new[]
            {
                new BarracksResource { id = "meta:charter", amount = 7 },
                new BarracksResource { id = "slg:gem", amount = 12 },
                new BarracksResource { id = "future:resource", amount = 9 }
            };
            var text = PortraitMetaPanels.WorldHeldSummary(held);
            Assert.That(text, Does.Contain("인장 7").And.Contain("보석 12").And.Contain("미확인 자원 9"));
            Assert.That(text, Does.Not.Contain("meta:").And.Not.Contain("slg:").And.Not.Contain("future:"));
            Assert.That(PortraitMetaPanels.WorldHeldSummary(null), Is.Empty);
            Assert.That(PortraitMetaPanels.WorldHeldSummary(Array.Empty<BarracksResource>()), Is.Empty);
        }

        [TestCase(390, 844)]
        [TestCase(1080, 2520)]
        public void HeaderUsesOnlyResponseAmountsAndKeepsWalletOutsideScroll(int width, int height)
        {
            var owner = new GameObject("World wallet fixture", typeof(RectTransform));
            var font = FontProvider.Create(new[] { PortraitMetaPanels.BarracksGlyphs, PortraitMetaPanels.WorldWalletGlyphs });
            try
            {
                var ui = owner.AddComponent<UiShell>(); ui.Initialize(font);
                ui.SafeRoot.sizeDelta = new Vector2(width, height);
                var root = UiShell.Rect("World wallet root", ui.Content); UiShell.Stretch(root);
                var resources = new[]
                {
                    new BarracksResource { id = "meta:grain", amount = 83 },
                    new BarracksResource { id = "slg:gem", amount = 2900 },
                    new BarracksResource { id = "meta:charter", amount = 7 }
                };
                PortraitMetaPanels.WorldWalletHeader(ui, root, resources);
                var labels = root.GetComponentsInChildren<Text>();
                Assert.That(labels.Single(t => t.name == "Barracks.Wallet:meta:grain").text, Is.EqualTo("곡물 83"));
                Assert.That(labels.Single(t => t.name == "Barracks.Wallet:meta:timber").text, Is.EqualTo("목재 —"));
                Assert.That(labels.Single(t => t.name == "Barracks.Wallet:slg:gem").text, Is.EqualTo("보석 2900"));
                Assert.That(labels.Single(t => t.name == "World.Wallet:meta:charter").text, Is.EqualTo("인장 7"));
                Assert.That(labels.All(t => !t.text.Contains("meta:") && !t.text.Contains("slg:")), Is.True);
                Assert.That(root.GetComponentsInChildren<ScrollRect>(), Is.Empty);
                Assert.That(root.GetComponentsInChildren<Image>().Count(i => i.sprite != null), Is.GreaterThanOrEqualTo(5));
                var second = UiShell.Rect("World wallet unknown", ui.Content);
                PortraitMetaPanels.WorldWalletHeader(ui, second, (BarracksResource[])null);
                var unknown = second.GetComponentsInChildren<Text>().Where(t => t.name.Contains("Wallet:")).ToArray();
                Assert.That(unknown.Length, Is.EqualTo(5));
                Assert.That(unknown.All(t => t.text.EndsWith("—", StringComparison.Ordinal)), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                UnityEngine.Object.DestroyImmediate(font);
            }
        }
    }
}
