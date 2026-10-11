using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class HeroGaitStateTests
    {
        [TestCase(60,1f)] [TestCase(120,1f)] [TestCase(60,1.3f)] [TestCase(120,1.3f)]
        public void EveryDirectionKeepsEntireStanceWithoutReleaseOrClipping(int hz,float speed)
        {
            for(var directionIndex=0;directionIndex<8;directionIndex++)
            {
                var angle=directionIndex*Mathf.PI/4;
                var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                var size=new Vector2(direction.x<-.01f?-.79746f:.79746f,.79746f);
                var gait=new HeroGaitState();var previous=gait.Advance(Vector2.zero,size,true);
                for(var frame=1;frame<=hz*3;frame++)
                {
                    var position=direction*(frame*.72f*speed/hz);
                    var sample=gait.Advance(position,size,true);
                    Assert.That(sample.ReleaseReason,Is.EqualTo(HeroGaitReleaseReason.None),$"direction{directionIndex} frame{frame}");
                    var expectedPhase=Mathf.Repeat(HeroGaitState.ProjectedTravel(position)/(Mathf.Abs(size.x)*.5f),1);
                    Assert.That(Mathf.Abs(Mathf.DeltaAngle(sample.Phase*360,expectedPhase*360)),Is.LessThan(.01f));
                    Assert.That(sample.NearPlanted^sample.FarPlanted,Is.True);
                    var near=sample.NearPlanted;
                    var delta=near?sample.NearCorrection:sample.FarCorrection;
                    var contact=near?sample.NearContact:sample.FarContact;
                    if(near&&previous.NearPlanted)Assert.That(contact,Is.EqualTo(previous.NearContact));
                    if(!near&&previous.FarPlanted)Assert.That(contact,Is.EqualTo(previous.FarContact));
                    var sole=HeroGaitState.Sole(sample.FrameIndex,near);
                    Assert.That((HeroGaitState.WorldSole(position,size,sample.FrameIndex,near,delta)-contact).magnitude,Is.LessThan(1e-5));
                    Assert.That((HeroGaitState.InverseWarp(sole+delta,delta)-sole).magnitude,Is.LessThan(1e-5));
                    Assert.That((sole+delta).y,Is.InRange(HeroGaitState.CanvasMinimumY,1));
                    Assert.That((sole+delta).x,Is.InRange(0,1));
                    Assert.That(1-delta.y/(HeroGaitState.Warp.x-HeroGaitState.Warp.y),Is.GreaterThan(.08f));
                    previous=sample;
                }
            }
        }
        [Test]
        public void AutomaticAttacksChangeUpperPoseWithoutResettingWalkingContacts()
        {
            var combat=new HeroGaitState();var walking=new HeroGaitState();var size=Vector2.one*.8f;
            combat.Advance(Vector2.zero,size,true);walking.Advance(Vector2.zero,size,true);
            for(var frame=1;frame<=240;frame++)
            {
                var position=Vector2.right*(frame*.012f);
                var actual=combat.Advance(position,size,true);
                var expected=walking.Advance(position,size,true);
                var attacking=frame%36<14;
                Assert.That(HeroGaitState.UpperFrame(actual.FrameIndex,attacking,false),Is.EqualTo(attacking?13:actual.FrameIndex));
                Assert.That(actual.Phase,Is.EqualTo(expected.Phase));
                Assert.That(actual.NearContact,Is.EqualTo(expected.NearContact));
                Assert.That(actual.FarContact,Is.EqualTo(expected.FarContact));
                Assert.That(actual.ReleaseReason,Is.EqualTo(HeroGaitReleaseReason.None));
            }
        }
        [Test]
        public void DirectionalStrideUsesVisibleGroundProjectionAndPreservesSpeedRatio()
        {
            for(var directionIndex=0;directionIndex<8;directionIndex++)
            {
                var angle=directionIndex*Mathf.PI/4;var direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                var regular=new HeroGaitState();var faster=new HeroGaitState();var size=Vector2.one*.8f;
                regular.Advance(Vector2.zero,size,true);faster.Advance(Vector2.zero,size,true);
                var normal=regular.Advance(direction*.02f,size,true);
                var fast=faster.Advance(direction*.026f,size,true);
                Assert.That(fast.Phase/normal.Phase,Is.EqualTo(1.3f).Within(1e-5));
            }
            Assert.That(HeroGaitState.VerticalCycleTravelPixels*.5f,Is.EqualTo(64));
            Assert.That(HeroGaitState.CycleTravelPixels*.5f,Is.EqualTo(128));
        }
        [TestCase(60,1f,1f)] [TestCase(120,1f,1f)]
        [TestCase(60,1.3f,1f)] [TestCase(120,1.3f,1f)]
        [TestCase(60,1f,-1f)] [TestCase(120,1.3f,-1f)]
        public void PlantedSoleRemainsAtWorldContactAcrossFrames(int hz,float speed,float facing)
        {
            var gait=new HeroGaitState();var size=new Vector2(.85f*facing,.85f);var origin=new Vector2(2,3);
            gait.Advance(origin,size,true);
            var contacts=0;
            for(var frame=1;frame<=hz*3;frame++)
            {
                var position=origin+Vector2.right*(frame*.72f*speed*facing/hz);
                var sample=gait.Advance(position,size,true);
                Assert.That(sample.ReleaseReason,Is.Not.EqualTo(HeroGaitReleaseReason.ReachLimit));
                if(sample.NearPlanted){Assert.That((HeroGaitState.WorldSole(position,size,sample.FrameIndex,true,sample.NearCorrection)-sample.NearContact).magnitude,Is.LessThan(1e-5f));contacts++;}
                if(sample.FarPlanted){Assert.That((HeroGaitState.WorldSole(position,size,sample.FrameIndex,false,sample.FarCorrection)-sample.FarContact).magnitude,Is.LessThan(1e-5f));contacts++;}
            }
            Assert.That(contacts,Is.EqualTo(hz*3));
        }
        [Test]
        public void EqualDistanceHasEqualPhaseRegardlessOfFrameRateOrElapsedTime()
        {
            var slow=new HeroGaitState();var fast=new HeroGaitState();var size=Vector2.one*.85f;
            slow.Advance(Vector2.zero,size,true);fast.Advance(Vector2.zero,size,true);
            HeroGaitSample a=default,b=default;
            for(var i=1;i<=120;i++)a=slow.Advance(new Vector2(i*.24f/120,0),size,true);
            for(var i=1;i<=60;i++)b=fast.Advance(new Vector2(i*.24f/60,0),size,true);
            Assert.That(a.Phase,Is.EqualTo(b.Phase).Within(1e-5));
        }
        [Test]
        public void FasterTravelAdvancesGaitByTheSameRatio()
        {
            var a=new HeroGaitState();var b=new HeroGaitState();var size=Vector2.one*.85f;
            a.Advance(Vector2.zero,size,true);b.Advance(Vector2.zero,size,true);
            var slow=a.Advance(new Vector2(.03f,0),size,true);
            var fast=b.Advance(new Vector2(.039f,0),size,true);
            Assert.That(fast.Phase/slow.Phase,Is.EqualTo(1.3f).Within(1e-5));
        }
        [Test]
        public void StopDisplacementAndDirectionChangeReleasePreviousContact()
        {
            var gait=new HeroGaitState();var size=Vector2.one*.85f;
            gait.Advance(Vector2.zero,size,true);gait.Advance(Vector2.right*.01f,size,true);
            var stop=gait.Advance(Vector2.right*.01f,size,false);
            Assert.That(stop.NearPlanted||stop.FarPlanted,Is.False);Assert.That(stop.FrameIndex,Is.EqualTo(HeroGaitState.IdleFrame));
            gait.Advance(Vector2.right*.02f,size,true);
            var turn=gait.Advance(new Vector2(.02f,.01f),size,true);
            Assert.That(turn.ReleaseReason,Is.EqualTo(HeroGaitReleaseReason.DirectionChanged));
            var knock=gait.Advance(Vector2.one,size,true,true);
            Assert.That(knock.NearPlanted||knock.FarPlanted,Is.False);Assert.That(knock.Phase,Is.Zero);
        }
        [Test]
        public void AnalyticInversePreservesHipAndCancelsFullSoleDisplacement()
        {
            var delta=new Vector2(.035f,-.018f);var hip=new Vector2(.5f,.6f);var sole=new Vector2(.4f,.18f);
            Assert.That(HeroGaitState.InverseWarp(hip,delta),Is.EqualTo(hip));
            Assert.That((HeroGaitState.InverseWarp(sole+delta,delta)-sole).magnitude,Is.LessThan(1e-6));
            var middle=new Vector2(.52f,(HeroGaitState.Warp.x+HeroGaitState.Warp.y)*.5f);
            Assert.That((HeroGaitState.InverseWarp(middle+delta*.5f,delta)-middle).magnitude,Is.LessThan(1e-6));
        }
        [Test]
        public void GaitSamplingAllocatesNoManagedMemoryAfterWarmup()
        {
            var gait=new HeroGaitState();var size=Vector2.one*.8f;
            for(var i=0;i<100;i++)gait.Advance(new Vector2(i*.002f,0),size,true);
            var before=System.GC.GetAllocatedBytesForCurrentThread();
            for(var i=100;i<1100;i++)gait.Advance(new Vector2(i*.002f,0),size,true);
            var allocated=System.GC.GetAllocatedBytesForCurrentThread()-before;
            Assert.That(allocated,Is.Zero);
        }
    }
}
