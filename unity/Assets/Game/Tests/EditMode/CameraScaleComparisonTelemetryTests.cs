using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class CameraScaleComparisonTelemetryTests
    {
        static readonly Rect Viewport = new Rect(-5, -5, 10, 10);
        static void Tick(CameraScaleComparisonTelemetry observer, int tick, float x, bool measured = true, bool foreground = true)
        {
            Assert.That(observer.BeginTick(tick, Viewport, Vector2.zero, .5f, measured, foreground), Is.True);
            observer.ObserveEnemy(7, new Rect(x - .5f, -.5f, 1, 1), new Vector2(x, 0), .5f);
            observer.EndTick();
        }
        [Test]
        public void WarmupOutsideEntryCountsContactWithoutLeftCensoring()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 8, 30);
            Tick(observer, 0, 8, false); Tick(observer, 1, 4, false); Tick(observer, 2, 1);
            var row = observer.Sample(0);
            Assert.That(row.contacts, Is.EqualTo(1)); Assert.That(row.recentEntrantContacts, Is.EqualTo(1));
            Assert.That(row.unknownHistoryContacts, Is.Zero);
        }
        [Test]
        public void InitialInsideContactIsUnknownNotAnOffscreenAttack()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 8);
            Tick(observer, 0, 0);
            Assert.That(observer.Sample(0).contacts, Is.Zero);
            Assert.That(observer.Sample(0).unknownHistoryContacts, Is.EqualTo(1));
            Assert.That(observer.Sample(0).recentEntrantContacts, Is.Zero);
        }
        [Test]
        public void OngoingContactCountsOnceAndDuplicateTicksAreIgnored()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 8);
            Tick(observer, 0, 8, false); Tick(observer, 1, 1); Tick(observer, 2, 0);
            Assert.That(observer.Sample(0).contacts, Is.EqualTo(1)); Assert.That(observer.Sample(1).contacts, Is.Zero);
            Assert.That(observer.BeginTick(2, Viewport, Vector2.zero, .5f, true, true), Is.False);
            Assert.That(observer.Count, Is.EqualTo(2));
        }
        [TestCase(30, 1)]
        [TestCase(31, 0)]
        public void OneSecondBoundaryUsesCoreTicks(int delay, int expected)
        {
            var observer = new CameraScaleComparisonTelemetry(8, 64, 30);
            Tick(observer, 0, 8, false); Tick(observer, 1, 4, false);
            for (var tick = 2; tick <= delay; tick++) Tick(observer, tick, 4, false);
            Tick(observer, delay + 1, 1);
            Assert.That(observer.Sample(0).contacts, Is.EqualTo(1));
            Assert.That(observer.Sample(0).recentEntrantContacts, Is.EqualTo(expected));
        }
        [Test]
        public void RenderedBoundsCountEvenWhenCenterIsOutside()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 8);
            Tick(observer, 0, 5.4f);
            Assert.That(observer.Sample(0).visible, Is.EqualTo(1));
        }
        [Test]
        public void MissingTickInvalidatesContinuityAndRestartsContactHistory()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 8);
            Tick(observer, 0, 8, false); Tick(observer, 2, 0);
            Assert.That(observer.DiscontinuousTicks, Is.EqualTo(1));
            Assert.That(observer.Sample(0).unknownHistoryContacts, Is.EqualTo(1));
            Assert.That(observer.Sample(0).contacts, Is.Zero);
        }
        [Test]
        public void CapacityOverflowIsExplicitAndForegroundDoesNotEraseHistory()
        {
            var observer = new CameraScaleComparisonTelemetry(1, 1);
            Tick(observer, 0, 8, false, false); Tick(observer, 1, 0, true, false); Tick(observer, 2, 0);
            Assert.That(observer.Sample(0).foreground, Is.False);
            Assert.That(observer.Sample(0).recentEntrantContacts, Is.EqualTo(1));
            Assert.That(observer.CapacityFailures, Is.EqualTo(1));
        }
        [Test]
        public void ActualDamageIsReportedSeparatelyWithoutAttribution()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 8);
            observer.BeginTick(0, Viewport, Vector2.zero, .5f, true, true);
            observer.EndTick(3);
            Assert.That(observer.Sample(0).damageEvents, Is.EqualTo(3));
            Assert.That(observer.Sample(0).contacts, Is.Zero);
        }
        [Test]
        public void WarmObserverDoesNotAllocatePerTick()
        {
            var observer = new CameraScaleComparisonTelemetry(8, 512);
            Tick(observer, 0, 8);
            var bounds = new Rect(0, 0, 1, 1);
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var tick = 1; tick < 500; tick++)
            {
                observer.BeginTick(tick, Viewport, Vector2.zero, .5f, true, true);
                observer.ObserveEnemy(7, bounds, Vector2.zero, .5f);
                observer.EndTick();
            }
            var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
