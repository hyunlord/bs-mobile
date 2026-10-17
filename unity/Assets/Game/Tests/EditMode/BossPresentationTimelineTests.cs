using System;
using System.Collections.Generic;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class BossPresentationTimelineTests
    {
        [Test]
        public void HitStopHoldsTheEntireSampleWhileLiveTicksContinue()
        {
            var timeline = new BossPresentationTimeline(8, _ => { });
            Capture(timeline, 0); Capture(timeline, 1);
            var before = timeline.Present(1, 1f / 60);
            timeline.RequestHitStop(.06f);
            Capture(timeline, 2);
            var held = timeline.Present(1, 1f / 60);
            Assert.That(held.Current.Tick, Is.EqualTo(before.Current.Tick));
            Assert.That(held.Alpha, Is.EqualTo(before.Alpha));
            Assert.That(held.VisualDeltaSeconds, Is.Zero);
            Assert.That(held.Delayed, Is.True);
            timeline.RequestHitStop(.08f);
            for (var i = 0; i < 4; i++) held = timeline.Present(1, 1f / 60);
            Assert.That(held.VisualDeltaSeconds, Is.GreaterThan(0));
        }

        [Test]
        public void ExactTickHoldDoesNotDeliverNextTickEffects()
        {
            var events = new List<int>();
            var timeline = new BossPresentationTimeline(8, slot => events.Add(slot.Frame.Tick));
            Capture(timeline, 0); Capture(timeline, 1);
            timeline.Present(1, 1f / 60);
            timeline.RequestHitStop(.06f);
            Capture(timeline, 2);
            var held = timeline.Present(1, 1f / 60);
            CollectionAssert.AreEqual(new[] { 0, 1 }, events);
            Assert.That(held.Current.Tick, Is.EqualTo(1));
            Assert.That(held.Alpha, Is.EqualTo(1));
        }

        [Test]
        public void DeathSlowConsumesOnlyQuarterSpeedThenCatchesUpWithoutReversing()
        {
            var timeline = new BossPresentationTimeline(8, _ => { });
            Capture(timeline, 0); Capture(timeline, 1); timeline.Present(1, 1f / 60);
            timeline.RequestDeathSlow(.3f);
            Capture(timeline, 2);
            var slow = timeline.Present(1, 1f / 60);
            Assert.That(slow.VisualDeltaSeconds, Is.EqualTo(1f / 240).Within(.00001f));
            var lastTime = Position(slow);
            for (var tick = 3; tick < 25; tick++)
            {
                Capture(timeline, tick);
                for (var i = 0; i < 2; i++)
                {
                    var sample = timeline.Present(i == 0 ? .5f : 1, 1f / 60);
                    Assert.That(Position(sample), Is.GreaterThanOrEqualTo(lastTime - .00001));
                    lastTime = Position(sample);
                }
            }
            Assert.That(lastTime, Is.EqualTo(24).Within(.00001));
            Assert.That(timeline.OverflowRecoveries, Is.Zero);
        }

        [Test]
        public void FractionalSlowSampleDoesNotDeliverItsFutureEndpointEvents()
        {
            var events = new List<int>();
            var timeline = new BossPresentationTimeline(8, slot => events.Add(slot.Frame.Tick));
            Capture(timeline, 0); Capture(timeline, 1); timeline.Present(1, 1f / 60);
            timeline.RequestDeathSlow(.3f); Capture(timeline, 2);
            var sample = timeline.Present(1, 1f / 60);
            Assert.That(sample.Current.Tick, Is.EqualTo(2));
            Assert.That(Position(sample), Is.LessThan(2));
            CollectionAssert.AreEqual(new[] { 0, 1 }, events);
        }

        [Test]
        public void DisabledFeedbackStillAdvancesOrdinaryVisualAnimationTime()
        {
            var timeline = new BossPresentationTimeline(8, _ => { });
            Capture(timeline, 0); Capture(timeline, 1); timeline.Present(1, 1f / 60);
            Capture(timeline, 2); timeline.CancelFeedback();
            var sample = timeline.Present(.5f, 1f / 60);
            Assert.That(sample.VisualDeltaSeconds, Is.GreaterThan(0));
        }

        [Test]
        public void BackwardLiveAlphaCannotReverseAnAlreadyPresentedTick()
        {
            var events = new List<int>();
            var timeline = new BossPresentationTimeline(8, slot => events.Add(slot.Frame.Tick));
            Capture(timeline, 0); Capture(timeline, 1);
            var before = timeline.Present(1, 1f / 60);
            var after = timeline.Present(.5f, 1f / 60);
            Assert.That(Position(after), Is.EqualTo(Position(before)));
            Assert.That(after.Alpha, Is.EqualTo(1));
            Assert.That(after.VisualDeltaSeconds, Is.Zero);
            CollectionAssert.AreEqual(new[] { 0, 1 }, events);
        }

        [Test]
        public void SameTickCommandsAreDeliveredInSequenceExactlyOnce()
        {
            var events = new List<int>();
            var timeline = new BossPresentationTimeline(8, slot => events.Add(slot.Frame.Level));
            Capture(timeline, 0, 1); Capture(timeline, 0, 2); Capture(timeline, 0, 3);
            timeline.Present(1, 0); timeline.Present(1, 0);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, events);
        }

        [Test]
        public void FullHistoryDrainsEventsBeforeReusingBorrowedStorage()
        {
            var events = new List<int>();
            var timeline = new BossPresentationTimeline(8, slot => events.Add(slot.Frame.Level));
            for (var i = 0; i < BossPresentationTimeline.HistoryLength + 1; i++) Capture(timeline, i, i);
            timeline.Present(1, 0);
            Assert.That(events.Count, Is.EqualTo(BossPresentationTimeline.HistoryLength + 1));
            for (var i = 0; i < events.Count; i++) Assert.That(events[i], Is.EqualTo(i));
            Assert.That(timeline.OverflowRecoveries, Is.EqualTo(1));
        }

        [Test]
        public void ExplicitTerminalTailAdvancesDeathFramesWithoutTicksOrRedeliveredEvents()
        {
            var events = new List<int>();
            var timeline = new BossPresentationTimeline(8, slot => events.Add(slot.Frame.Tick));
            Capture(timeline, 13642); Capture(timeline, 13643, health: 0);
            var terminal = timeline.Present(1, 1f / 60);
            var captured = terminal.Current;
            var elapsed = 0f;
            var visited = new HashSet<int>();
            for (var i = 0; i < 60; i++)
            {
                var sample = timeline.Present(1, 1f / 60);
                Assert.That(sample.VisualDeltaSeconds, Is.Zero);
                Assert.That(sample.PresentationDelta(1f / 60), Is.Zero, "Normal presentation remains frozen.");
                elapsed += sample.PresentationDelta(1f / 60, 13643);
                visited.Add(WorldRenderer.ProductionFrame("death", elapsed, 6, 8));
                Assert.That(sample.Current, Is.SameAs(captured));
                Assert.That(sample.Current.Tick, Is.EqualTo(13643));
                Assert.That(sample.Current.Lord.Health, Is.Zero);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3, 4, 5 }, visited);
            CollectionAssert.AreEqual(new[] { 13642, 13643 }, events);
            Assert.That(WorldRenderer.ProductionFrame("death", elapsed, 6, 8), Is.EqualTo(5));
        }

        static double Position(BossPresentationTimeline.Sample sample) =>
            sample.Previous.Tick + (sample.Current.Tick - sample.Previous.Tick) * (double)sample.Alpha;

        static void Capture(BossPresentationTimeline timeline, int tick, int level = 1, int health = 100)
        {
            var frame = new RunFrame(tick, RunStatus.Running, 0, 300, 27000, 30, 12000, 12000,
                new LordView(new WorldPoint(tick, 0), new WorldPoint(1, 0), health, 100), default,
                0, 10, level, 0, new EntityCounts(0, 0, 0, 0, 0),
                Array.Empty<EnemyView>(), Array.Empty<FarmView>(), Array.Empty<BuildingView>(),
                Array.Empty<PersonView>(), Array.Empty<GroundLootView>(), Array.Empty<RemainView>(),
                Array.Empty<EquipmentView>(), Array.Empty<PresentationEvent>());
            var slot = timeline.PrepareCapture();
            timeline.Commit(slot, frame, null, null);
        }
    }
}
