using Game.View;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class HeroLevelPoseTimelineTests
    {
        [Test]
        public void CardModalLongerThanPoseDurationDoesNotConsumeQueuedPose()
        {
            var timeline = new HeroLevelPoseTimeline(); timeline.Queue();
            for (var frame = 0; frame < 180; frame++) Assert.That(timeline.Sample(1f / 60, true, false), Is.EqualTo(-1));
            Assert.That(timeline.Pending, Is.True);
            Assert.That(timeline.Sample(.1f, false, false), Is.Zero);
            Assert.That(timeline.Sample(.1f, false, false), Is.EqualTo(1f / 6).Within(.00001f));
        }
        [Test]
        public void AttackAndHitPriorityCannotConsumePendingOrStartedLevelPose()
        {
            var timeline = new HeroLevelPoseTimeline(); timeline.Queue();
            Assert.That(timeline.Sample(2, false, true), Is.EqualTo(-1));
            Assert.That(timeline.Sample(.2f, false, false), Is.Zero);
            Assert.That(timeline.Sample(3, false, true), Is.EqualTo(-1));
            Assert.That(timeline.Sample(.2f, false, false), Is.EqualTo(1f / 3).Within(.00001f));
            Assert.That(timeline.Sample(.2f, false, false), Is.EqualTo(2f / 3).Within(.00001f));
            Assert.That(timeline.Pending, Is.False);
            Assert.That(timeline.Sample(.1f, false, false), Is.EqualTo(-1));
        }
        [Test]
        public void ReplaySeekOrDeathResetRemovesStaleQueuedPose()
        {
            var timeline = new HeroLevelPoseTimeline(); timeline.Queue(); timeline.Reset();
            Assert.That(timeline.Pending, Is.False);
            Assert.That(timeline.Sample(.1f, false, false), Is.EqualTo(-1));
        }
    }
}
