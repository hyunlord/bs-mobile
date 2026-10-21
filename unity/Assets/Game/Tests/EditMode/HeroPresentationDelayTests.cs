using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class HeroPresentationDelayTests
    {
        [Test]
        public void OrdinaryPredictionAndInterpolationAreUntouched()
        {
            var delay = new HeroPresentationDelay();
            Assert.That(delay.Resolve(Vector2.zero, Vector2.right, false, .016f), Is.EqualTo((Vector2?)Vector2.right));
            Assert.That(delay.Resolve(Vector2.zero, null, false, .016f), Is.Null);
        }

        [Test]
        public void HitStopStartsAtLastRenderedPoseAndMovesOnlyWithDelayedClock()
        {
            var delay = new HeroPresentationDelay();
            delay.CommitRendered(new Vector2(10, 3));
            Assert.That(delay.Resolve(new Vector2(9, 3), new Vector2(11, 3), true, .016f), Is.EqualTo((Vector2?)new Vector2(10, 3)));
            Assert.That(delay.Resolve(new Vector2(9, 3), new Vector2(12, 3), true, .016f), Is.EqualTo((Vector2?)new Vector2(10, 3)));
            Assert.That(delay.Resolve(new Vector2(9.25f, 3), new Vector2(13, 3), true, .016f), Is.EqualTo((Vector2?)new Vector2(10.25f, 3)));
        }

        [Test]
        public void RecoveryStartsContinuouslyThenReturnsToLivePrediction()
        {
            var delay = new HeroPresentationDelay();
            delay.CommitRendered(Vector2.right * 10);
            delay.Resolve(Vector2.right * 9, Vector2.right * 11, true, .016f);
            Assert.That(delay.Resolve(Vector2.right * 11, Vector2.right * 12, false, .016f), Is.EqualTo((Vector2?)(Vector2.right * 10)));
            var middle = delay.Resolve(Vector2.right * 11, Vector2.right * 12, false, .04f).Value;
            Assert.That(middle.x, Is.EqualTo(11).Within(.0001f));
            Assert.That(delay.Resolve(Vector2.right * 11, Vector2.right * 12, false, .04f), Is.EqualTo((Vector2?)(Vector2.right * 12)));
            Assert.That(delay.Resolve(Vector2.right * 12, null, false, .016f), Is.Null);
        }

        [Test]
        public void FirstSampleAndNewWorldHaveNoStaleOffset()
        {
            var delay = new HeroPresentationDelay();
            Assert.That(delay.Resolve(Vector2.zero, Vector2.right, true, .016f), Is.EqualTo((Vector2?)Vector2.right));
            delay.CommitRendered(Vector2.right * 100);
            delay.Reset();
            Assert.That(delay.Resolve(Vector2.zero, null, true, .016f), Is.EqualTo((Vector2?)Vector2.zero));
        }
    }
}
