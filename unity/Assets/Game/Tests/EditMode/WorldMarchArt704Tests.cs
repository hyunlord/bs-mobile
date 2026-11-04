using System.Linq;
using Game.App;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class WorldMarchArt704Tests
    {
        [TestCase("outbound", "march-moving", "진행률 미확인")]
        [TestCase("gathering", "march-gathering", "진행률 미확인")]
        [TestCase("returning", "march-returning", "진행률 미확인")]
        [TestCase("returned", "march-returning", "귀환 완료")]
        public void OnlyServerConfirmedReturnClaimsCompletion(string state, string role, string label)
        {
            Assert.That(PortraitMetaPanels.MarchStateArt(state), Is.EqualTo(role));
            Assert.That(PortraitMetaPanels.MarchProgressLabel(state), Is.EqualTo(label));
        }

        [Test] public void UnsupportedCombatDoesNotBecomeAConfirmedMarchState()
        {
            Assert.That(ChatMarchArt.Sprite("march-combat"), Is.Not.Null, "Approved art stays registered for its future server contract.");
            Assert.That(PortraitMetaPanels.MarchStateArt("combat"), Is.Null);
            Assert.That(PortraitMetaPanels.MarchProgressLabel("combat"), Is.EqualTo("진행률 미확인"));
            Assert.That(PortraitMetaPanels.MarchTroopArt("slg:skirmish"), Is.Null);
        }

        [TestCase(354f)]
        [TestCase(284f)]
        public void ResourceTargetUsesOneSlicedCardAndNoInventedGradeOrStock(float width)
        {
            var owner = new GameObject("World target art test", typeof(RectTransform));
            var font = FontProvider.Create(new[] { PortraitMetaPanels.Glyphs, PortraitMetaPanels.WorldMapGlyphs });
            try
            {
                var ui = owner.AddComponent<UiShell>(); ui.Initialize(font, true);
                var body = new GameObject("Body", typeof(RectTransform)).GetComponent<RectTransform>(); body.SetParent(owner.transform, false);
                var chunk = new WorldMapChunk
                {
                    Tiles = new[] { new WorldMapTile { X=1, Y=2, Terrain="plain", Passable=true } },
                    Targets = new[] { new WorldMapTarget { Id="resource:test", Kind="resource", ContentId="meta:grain", Generation=9, X=1, Y=2 } }
                };
                var end = PortraitMetaPanels.WorldTargetPanel(ui, body, chunk, 1, 2, width, 0);
                var images = body.GetComponentsInChildren<Image>();
                var card = images.Single(image => image.name == "MarchArt.target-card");
                Assert.That(card.type, Is.EqualTo(Image.Type.Sliced));
                Assert.That(images.Count(image => image.name == "MarchArt.target-header-resource"), Is.EqualTo(1));
                Assert.That(images.Any(image => image.name.Contains("target-grade")), Is.False);
                Assert.That(end, Is.GreaterThan(card.rectTransform.anchoredPosition.y * -1 + card.rectTransform.sizeDelta.y));
                var labels = string.Join("\n", body.GetComponentsInChildren<Text>().Select(value => value.text));
                Assert.That(labels, Does.Contain("재고 미확인"));
                Assert.That(labels, Does.Not.Contain("등급").And.Not.Contain("재고 0"));
            }
            finally { Object.DestroyImmediate(owner); Object.DestroyImmediate(font); }
        }
    }
}
