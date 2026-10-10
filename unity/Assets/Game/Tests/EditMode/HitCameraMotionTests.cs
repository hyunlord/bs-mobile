using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class HitCameraMotionTests
    {
        [TestCase(640,2f)]
        [TestCase(1080,5.2f)]
        [TestCase(1818,5.2f)]
        [TestCase(2560,8f)]
        public void ActualScreenImpulseIsBoundedAtEveryZoomAndResolution(int height,float halfHeight)
        {
            var motion=new HitCameraMotion(); motion.Trigger(1);
            for(var i=0;i<=120;i++)
            {
                var offset=motion.Sample(1+i/120f,halfHeight,height);
                var normalizedPixels=offset.magnitude*height/(2*halfHeight)*1080/height;
                Assert.That(normalizedPixels,Is.LessThanOrEqualTo(.250001f));
                Assert.That(offset.x,Is.Zero);
                Assert.That(offset.y,Is.LessThanOrEqualTo(0));
            }
        }
        [Test]
        public void RepeatedHitsDoNotRestartAnActiveImpulseOrJumpItsPosition()
        {
            var motion=new HitCameraMotion(); motion.Trigger(1);
            Assert.That(motion.Sample(1,5,1080),Is.EqualTo(Vector2.zero));
            var before=motion.Sample(1.07f,5,1080);
            motion.Trigger(1.07f);
            Assert.That(motion.Sample(1.07f,5,1080),Is.EqualTo(before));
            Assert.That(motion.Sample(1.18f,5,1080).magnitude,Is.LessThan(.000001f));
            motion.Trigger(2);
            Assert.That(motion.Sample(2,5,1080),Is.EqualTo(Vector2.zero));
        }
        [Test]
        public void FifteenFrameActualScreenResidualStaysBelowHalfAPixelWithRepeatedHits()
        {
            var motion=new HitCameraMotion(); var y=new float[600];
            for(var i=0;i<y.Length;i++)
            {
                var time=i/60f;if(i%4==0)motion.Trigger(time);
                y[i]=motion.Sample(time,5.2f,1818).y*1080/(2*5.2f);
            }
            var squares=0f;var count=0;
            for(var i=7;i<y.Length-7;i++)
            {
                var mean=0f;for(var j=i-7;j<=i+7;j++)mean+=y[j]/15;
                squares+=(y[i]-mean)*(y[i]-mean);count++;
            }
            Assert.That(Mathf.Sqrt(squares/count),Is.LessThan(.5f));
        }
    }
}
