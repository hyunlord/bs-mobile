using System.Linq;
using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class WorldDamageNumbersTests
    {
        [Test]
        public void EnemyCapSuppressesOnlyNewLabelsAndKeepsExactExistingTargetAggregation()
        {
            var owner = new GameObject("Damage number visible cap");
            using (var numbers = new WorldDamageNumbers(owner.transform))
            {
                for (var i = 0; i < WorldDamageNumbers.EnemyVisibleLimit; i++)
                    numbers.AddAggregated(i + 1, Vector2.right * i * 2, 24);
                numbers.AddAggregated(99, Vector2.up * 2, 7);
                numbers.AddAggregated(1, Vector2.zero, 13);
                Assert.That(numbers.CaptureVisibleAmountsForTesting().Length, Is.EqualTo(6));
                Assert.That(numbers.CaptureVisibleAmountsForTesting().Count(value => value == "37"), Is.EqualTo(1));
                Assert.That(numbers.SuppressedVisualLabels, Is.EqualTo(1), "This counts omitted display labels, not lost damage or Core events.");
                numbers.Advance(GameVisualTokens.DamageNumberSeconds, true);
                numbers.AddAggregated(99, Vector2.zero, 9);
                Assert.That(numbers.CaptureVisibleAmountsForTesting(), Is.EqualTo(new[] { "9" }), "A suppressed earlier hit must not contaminate a later displayed amount.");
            }
            Object.DestroyImmediate(owner);
        }

        [Test]
        public void HeroNumbersHaveIndependentCapacityAndDrawAboveSmallerEnemyNumbers()
        {
            var enemyOwner = new GameObject("Enemy numbers");
            var heroOwner = new GameObject("Hero numbers");
            using (var enemies = new WorldDamageNumbers(enemyOwner.transform))
            using (var hero = new WorldDamageNumbers(heroOwner.transform, visibleLimit: 3, characterSize: .04f, sortingOffset: 2))
            {
                for (var i = 0; i < 20; i++) enemies.Add(Vector2.right * i * 2, 24);
                hero.Add(Vector2.zero, 8, GameVisualTokens.Hostile);
                Assert.That(hero.CaptureVisibleAmountsForTesting(), Is.EqualTo(new[] { "8" }));
                var enemyLabel = enemyOwner.GetComponentsInChildren<TextMesh>()[0];
                var heroLabel = heroOwner.GetComponentsInChildren<TextMesh>()[0];
                Assert.That(heroLabel.characterSize, Is.GreaterThan(enemyLabel.characterSize));
                Assert.That(heroLabel.GetComponent<MeshRenderer>().sortingOrder, Is.GreaterThan(enemyLabel.GetComponent<MeshRenderer>().sortingOrder));
            }
            Object.DestroyImmediate(enemyOwner); Object.DestroyImmediate(heroOwner);
        }
        [Test]
        public void SameEnemyAggregatesWithinWindowButDifferentEnemiesAndLaterHitsStaySeparate()
        {
            var owner = new GameObject("Damage aggregation regression");
            using (var numbers = new WorldDamageNumbers(owner.transform))
            {
                numbers.AddAggregated(7, Vector2.zero, 9);
                numbers.Advance(.05f, true);
                numbers.AddAggregated(7, Vector2.zero, 12);
                numbers.AddAggregated(8, Vector2.one, 20);
                Assert.That(owner.GetComponentsInChildren<TextMesh>().Select(label => label.text), Is.EquivalentTo(new[] { "21", "20" }));
                AssertSeparated(owner.GetComponentsInChildren<TextMesh>());
                numbers.Advance(GameVisualTokens.DamageAggregateSeconds, true);
                numbers.AddAggregated(7, Vector2.zero, 3);
                Assert.That(owner.GetComponentsInChildren<TextMesh>().Select(label => label.text), Is.EquivalentTo(new[] { "21", "20", "3" }));
                numbers.Advance(0, false);
                numbers.AddAggregated(7, Vector2.zero, 4);
                Assert.That(owner.GetComponentsInChildren<TextMesh>().Single().text, Is.EqualTo("4"));
            }
            Object.DestroyImmediate(owner);
        }

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
        public void LargeAmountsUseExactPooledChunksAndOverflowStartsAnotherExactLabel()
        {
            var owner = new GameObject("Large pooled damage regression");
            using (var numbers = new WorldDamageNumbers(owner.transform))
            {
                numbers.AddAggregated(1, Vector2.zero, 16383);
                numbers.AddAggregated(1, Vector2.zero, 2);
                Assert.That(numbers.CaptureVisibleAmountsForTesting(), Is.EqualTo(new[] { "16385" }));
                numbers.Advance(0, false);
                numbers.AddAggregated(2, Vector2.zero, long.MaxValue);
                numbers.AddAggregated(2, new Vector2(0, 2), 1);
                Assert.That(numbers.CaptureVisibleAmountsForTesting(), Is.EquivalentTo(new[] { "9223372036854775807", "1" }));
                numbers.Advance(0, false);
                numbers.Add(Vector2.zero, 100000001);
                Assert.That(numbers.CaptureVisibleAmountsForTesting(), Is.EqualTo(new[] { "100000001" }));
                numbers.Advance(0, false);
                Assert.That(numbers.CaptureVisibleAmountsForTesting(), Is.Empty);
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
                var pooled = owner.GetComponentsInChildren<TextMesh>(true);
                Assert.That(pooled.Count(label => label.transform.parent == owner.transform), Is.EqualTo(WorldDamageNumbers.Capacity));
                Assert.That(pooled.Length, Is.EqualTo(WorldDamageNumbers.Capacity * 5), "Continuation digits are precreated independently of the smaller display cap.");
                var labels = owner.GetComponentsInChildren<TextMesh>();
                Assert.That(labels.Length, Is.GreaterThan(0));
                Assert.That(labels.Length, Is.LessThanOrEqualTo(WorldDamageNumbers.EnemyVisibleLimit));
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
