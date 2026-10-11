using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class CaptureArrivalSteeringTests
    {
        [Test]
        public void RecordedNearWaypointNeverCommandsAFullSpeedCrossing()
        {
            var lord=new[]{6016,5992,6016,5992};var target=new[]{6014,6006,5998,5990};
            for(var i=0;i<lord.Length;i++)
            {
                var delta=target[i]-lord[i];
                var direction=CaptureArrivalSteering.Direction(new Vector2(delta,0),24);
                var step=(int)(Mathf.RoundToInt(direction.x*1000)*24L/1000);
                Assert.That(Mathf.Abs(step),Is.LessThanOrEqualTo(Mathf.Abs(delta)));
                Assert.That(Mathf.Abs(direction.x),Is.LessThan(1));
            }
        }
        [Test]
        public void MovingCircleWaypointDoesNotCreateAThreeTickDirectionOscillation()
        {
            var lordX=6016;var positiveCommands=0;
            for(var tick=281;tick<335;tick++)
            {
                var targetX=6000+(int)(System.Math.Cos(tick/180d)*1500);
                var direction=CaptureArrivalSteering.Direction(new Vector2(targetX-lordX,0),24);
                if(direction.x>0)positiveCommands++;
                lordX+=(int)(Mathf.RoundToInt(direction.x*1000)*24L/1000);
            }
            Assert.That(positiveCommands,Is.Zero,"A continuously leftward nearby target must not produce rightward correction commands.");
        }
        [Test]
        public void DistantWaypointsKeepFullStickAndArrivalCanRemainStill()
        {
            Assert.That(CaptureArrivalSteering.Direction(new Vector2(2000,0),24),Is.EqualTo(Vector2.right));
            Assert.That(CaptureArrivalSteering.Direction(Vector2.zero,24),Is.EqualTo(Vector2.zero));
        }
    }
}
