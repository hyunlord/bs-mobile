using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EdgeThreatSelectionTests
    {
        static readonly Rect Viewport = new Rect(-5, -5, 10, 10);
        [Test]
        public void DefaultWorldScaleIsSelectedValueAndComparisonOverrideIsAbsolute()
        {
            var owner = new GameObject("Selected camera scale");
            try
            {
                var world = owner.AddComponent<WorldRenderer>();
                Assert.That(world.ReviewCameraMagnification, Is.EqualTo(1.4f));
                world.ReviewCameraMagnification = 1;
                Assert.That(world.ReviewCameraMagnification, Is.EqualTo(1));
                world.ReviewCameraMagnification = 1.4f;
                Assert.That(world.ReviewCameraMagnification, Is.EqualTo(1.4f));
            }
            finally { Object.DestroyImmediate(owner); }
        }
        [Test]
        public void SixSectorsCapMarkersAndKeepThemInsideInset()
        {
            var selection = new EdgeThreatSelection(); selection.Begin(Viewport, Vector2.zero, .2f);
            for (var i = 0; i < 60; i++)
            {
                var radians = i * Mathf.PI / 30;
                selection.Consider(i, new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 12, EdgeThreatKind.Ordinary, false);
            }
            var count = 0;
            for (var i = 0; i < EdgeThreatSelection.Limit; i++)
                if (selection.TryGet(i, out var marker))
                {
                    count++; Assert.That(Mathf.Abs(marker.Position.x), Is.LessThanOrEqualTo(4.80001f));
                    Assert.That(Mathf.Abs(marker.Position.y), Is.LessThanOrEqualTo(4.80001f));
                }
            Assert.That(count, Is.EqualTo(6));
        }
        [Test]
        public void ImminentTellWinsSectorAndStableIdBreaksEqualDistanceTies()
        {
            var selection = new EdgeThreatSelection(); selection.Begin(Viewport, Vector2.zero, .2f);
            selection.Consider(9, new Vector2(6, 0), EdgeThreatKind.Ordinary, false);
            selection.Consider(8, new Vector2(6, 0), EdgeThreatKind.Ordinary, false);
            Assert.That(selection.TryGet(3, out var marker), Is.True); Assert.That(marker.Id, Is.EqualTo(8));
            selection.Consider(10, new Vector2(7, 0), EdgeThreatKind.Boss, false);
            selection.Consider(11, new Vector2(8, 0), EdgeThreatKind.Charger, true);
            selection.TryGet(3, out marker); Assert.That(marker.Id, Is.EqualTo(11));
        }
        [Test]
        public void DistanceFadesAndVisibleEnemiesDoNotBecomeEdgeArrows()
        {
            var selection = new EdgeThreatSelection(); selection.Begin(Viewport, Vector2.zero, .2f);
            selection.Consider(1, Vector2.zero, EdgeThreatKind.Boss, false);
            for (var i = 0; i < EdgeThreatSelection.Limit; i++) Assert.That(selection.TryGet(i, out _), Is.False);
            selection.Consider(2, new Vector2(6, 0), EdgeThreatKind.Ordinary, false);
            selection.TryGet(3, out var near);
            selection.Begin(Viewport, Vector2.zero, .2f);
            selection.Consider(2, new Vector2(20, 0), EdgeThreatKind.Ordinary, false);
            selection.TryGet(3, out var far);
            Assert.That(near.Opacity, Is.GreaterThan(far.Opacity));
            Assert.That(far.Opacity, Is.GreaterThan(0));
        }
        [TestCase("tell-charge", false, 15, true)]
        [TestCase("tell-charge", false, 16, false)]
        [TestCase("tell-charge", false, 0, false)]
        [TestCase("tell-charge", true, 10, false)]
        [TestCase("charge", false, 10, false)]
        [TestCase("idle", false, 10, false)]
        public void HalfSecondWarningUsesOnlyExistingActiveTell(string phase, bool stopped, int remaining, bool expected)
        {
            Assert.That(EdgeThreatSelection.IsInboundCharge(phase, stopped, 100 + remaining, 100, 30, Viewport,
                new Vector2(8, 0), Vector2.zero, .2f), Is.EqualTo(expected));
            Assert.That(EdgeThreatSelection.IsInboundCharge(phase, stopped, 100 + remaining, 100, 30, Viewport,
                new Vector2(8, 8), new Vector2(8, 10), .2f), Is.False);
        }
        [Test]
        public void ThreatKindsHaveDistinctColorsAndWarmSelectionDoesNotAllocate()
        {
            Assert.That(EdgeThreatSelection.Tint(EdgeThreatKind.Charger), Is.Not.EqualTo(EdgeThreatSelection.Tint(EdgeThreatKind.Elite)));
            Assert.That(EdgeThreatSelection.Tint(EdgeThreatKind.Boss), Is.Not.EqualTo(EdgeThreatSelection.Tint(EdgeThreatKind.Elite)));
            var selection = new EdgeThreatSelection(); selection.Begin(Viewport, Vector2.zero, .2f);
            selection.Consider(1, new Vector2(8, 0), EdgeThreatKind.Charger, true);
            var start = System.GC.GetAllocatedBytesForCurrentThread();
            for (var tick = 0; tick < 100; tick++)
            {
                selection.Begin(Viewport, Vector2.zero, .2f);
                for (var i = 0; i < 800; i++) selection.Consider(i, new Vector2(8 + i * .001f, 0), EdgeThreatKind.Charger, true);
            }
            var allocated = System.GC.GetAllocatedBytesForCurrentThread() - start;
            Assert.That(allocated, Is.Zero);
        }
    }
}
