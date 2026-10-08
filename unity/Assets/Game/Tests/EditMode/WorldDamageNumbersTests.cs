using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class WorldDamageNumbersTests
    {
        [Test]
        public void ClusteredHitsKeepHonestAmountsAndSeparateReadableBounds()
        {
            var owner = new GameObject("Damage number regression");
            using (var numbers = new WorldDamageNumbers(owner.transform))
            {
                numbers.Add(Vector2.zero, 10);
                numbers.Add(new Vector2(0.01f, 0), 38);
                numbers.Advance(0.05f, true);
                numbers.Add(Vector2.zero, 42);
                var labels = owner.GetComponentsInChildren<TextMesh>();
                Assert.That(labels.Select(label => label.text), Is.EquivalentTo(new[] { "10", "38", "42" }));
                AssertSeparated(labels);
                numbers.Advance(0.1f, true);
                AssertSeparated(labels);
                numbers.Advance(0, false);
                Assert.That(owner.GetComponentsInChildren<TextMesh>(), Is.Empty);
            }
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void DenseBurstStaysPooledAndDoesNotOverlapVisibleAmounts()
        {
            var owner = new GameObject("Damage number capacity regression");
            using (var numbers = new WorldDamageNumbers(owner.transform))
            {
                for (var i = 0; i < WorldDamageNumbers.Capacity * 3; i++) numbers.Add(Vector2.zero, 100 + i);
                Assert.That(owner.GetComponentsInChildren<TextMesh>(true).Length, Is.LessThanOrEqualTo(WorldDamageNumbers.Capacity));
                var labels = owner.GetComponentsInChildren<TextMesh>();
                Assert.That(labels.Length, Is.GreaterThan(0));
                AssertSeparated(labels);
                foreach (var label in labels) Assert.That(long.Parse(label.text), Is.InRange(100L, 100L + WorldDamageNumbers.Capacity * 3 - 1));
            }
            Object.DestroyImmediate(owner);
        }

        private static void AssertSeparated(TextMesh[] labels)
        {
            for (var i = 0; i < labels.Length; i++)
                for (var j = i + 1; j < labels.Length; j++)
                {
                    var a = labels[i].GetComponent<MeshRenderer>().bounds;
                    var b = labels[j].GetComponent<MeshRenderer>().bounds;
                    Assert.That(a.size.x, Is.GreaterThan(0), "Font must produce measurable visible glyphs.");
                    var overlaps = a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y;
                    Assert.That(overlaps, Is.False, $"Damage {labels[i].text} and {labels[j].text} overlap into an ambiguous number.");
                }
        }
    }
}
