using System;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using SowSiege.Core;
using System.Collections.Generic;

namespace Tests.EditMode
{
    public sealed class WaveRenderInterpolationTests
    {
        static WaveRuntimeFrame Frame() => new WaveRuntimeFrame("test","test",0,0,0,false,
            Array.Empty<WaveWorkView>(),Array.Empty<WaveRewardView>(),Array.Empty<WaveProjectileView>(),
            Array.Empty<WaveEnemyView>(),Array.Empty<WaveGroupView>(),Array.Empty<WorldPoint>(),
            Array.Empty<string>(),Array.Empty<string>(),Array.Empty<WaveEvent>(),new Dictionary<string,long>(),
            Array.Empty<WaveAttackView>(),0,Array.Empty<WaveDetourView>(),new WorldPoint(0,0),0);

        [Test] public void CardPauseSettlesTheLastTickWithoutAnEndpointSnapOrResumeRewind()
        {
            var interpolation=new RenderTickInterpolation();
            Assert.That(interpolation.Present(10,.9f,false,1f/120,30),Is.EqualTo(.9f).Within(.001f));
            Assert.That(interpolation.Present(11,0,true,1f/120,30),Is.EqualTo(.15f).Within(.001f));
            Assert.That(interpolation.Present(11,0,true,1f/120,30),Is.EqualTo(.4f).Within(.001f));
            for(var i=0;i<4;i++)interpolation.Present(11,0,true,1f/120,30);
            Assert.That(interpolation.Present(11,0,false,1f/120,30),Is.EqualTo(1));
            Assert.That(interpolation.Present(12,.25f,false,1f/120,30),Is.EqualTo(.25f).Within(.001f));
            interpolation.Reset();
            Assert.That(interpolation.Present(0,0,false,0,30),Is.Zero);
        }

        [Test] public void ExistingGroupsInterpolateWhileNewGroupsAppearAtTheirActualPosition()
        {
            var motion=new WaveRenderInterpolation();
            var group=new WaveGroupView(1,"test",new WorldPoint(0,0),"returning",0,0,0,0,"test");
            motion.Accept(Frame() with {Groups=new[]{group}},0);
            var moved=group with {Position=new WorldPoint(100,0)};
            motion.Accept(Frame() with {Groups=new[]{moved}},1);
            Assert.That(motion.GroupPosition(moved,0,100),Is.EqualTo(Vector2.zero));
            Assert.That(motion.GroupPosition(moved,.5f,100),Is.EqualTo(new Vector2(.5f,0)));
            Assert.That(motion.GroupPosition(moved,1,100),Is.EqualTo(Vector2.right));
            Assert.That(motion.GroupPosition(moved with {Id=2},0,100),Is.EqualTo(Vector2.right));
            motion.Reset();motion.Accept(Frame() with {Groups=new[]{moved}},2);
            Assert.That(motion.GroupPosition(moved,0,100),Is.EqualTo(Vector2.right));
        }

        [Test] public void NewOrbitActivationNeverBlendsFromExpiredFragments()
        {
            var motion=new WaveRenderInterpolation();
            var first=new WaveAttackView("test","orbit",new WorldPoint(0,0),new WorldPoint(100,0),10,30,1);
            motion.Accept(Frame() with {Attacks=new[]{first}},0);
            var oldId=motion.OrbitSampleId(0);
            var second=first with {ActivationId=2,ExpireTick=60,Position=new WorldPoint(0,100)};
            motion.Accept(Frame() with {Attacks=new[]{second}},1);
            Assert.That(motion.OrbitSampleId(0),Is.Not.EqualTo(oldId));
            Assert.That(Vector2.Distance(motion.OrbitPosition(0,0,1,100,new WorldPoint(0,0),Vector2.zero),Vector2.up),Is.LessThan(.001f));
        }

        [Test] public void CardinalDiagonalOrbitRadiusChangesContinuouslyAndSettles()
        {
            var motion=new WaveRenderInterpolation();
            var cardinal=new WaveAttackView("test","orbit",new WorldPoint(0,0),new WorldPoint(700,0),10,300,1);
            motion.Accept(Frame() with {Attacks=new[]{cardinal}},0);
            var diagonal=cardinal with {Position=new WorldPoint(700,700)};
            motion.Accept(Frame() with {Attacks=new[]{diagonal}},1);
            Vector2 Position(float time)=>motion.OrbitPosition(0,1,time,1000,new WorldPoint(0,0),Vector2.zero);
            Assert.That(Position(1).magnitude,Is.EqualTo(.7f).Within(.0001f));
            var start=Position(1);
            Assert.That(Vector2.Distance(start,Position(1.008333f)),Is.LessThan(.06f));
            Assert.That(Position(1.05f).magnitude,Is.InRange(.71f,.98f));
            for(var i=7;i<=60;i++)Position(1+i/120f);
            Assert.That(Position(1.5f).magnitude,Is.EqualTo(Mathf.Sqrt(.98f)).Within(.001f));
            motion.Accept(Frame() with {Attacks=new[]{cardinal}},2);
            Assert.That(Position(2).magnitude,Is.EqualTo(Mathf.Sqrt(.98f)).Within(.001f));
            for(var i=1;i<=60;i++)Position(2+i/120f);
            Assert.That(Position(2.5f).magnitude,Is.EqualTo(.7f).Within(.001f));
        }

        [Test] public void AngleCrossesWrapByTheShortestPath()
        {
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(180, WaveRenderInterpolation.BlendAngle(179, -179, .5f))), Is.LessThan(.001f));
        }

        [Test] public void OrbitSettlesWithinItsBoundAndKeepsTheRadius()
        {
            var orbit = new OrbitRenderMotion(0, 0);
            orbit.SetTarget(45, 1);
            Assert.That(orbit.AngleAt(1), Is.EqualTo(0));
            Assert.That(orbit.AngleAt(1.05f), Is.InRange(1, 44));
            for(var i=7;i<=60;i++)orbit.AngleAt(1+i/120f);
            Assert.That(orbit.AngleAt(1.5f), Is.EqualTo(45).Within(.001f));
            Assert.That((WaveRenderInterpolation.RadialPoint(new Vector2(3, 4), 2, 45) - new Vector2(3, 4)).magnitude, Is.EqualTo(2).Within(.001f));
        }

        [Test] public void LargeOrbitTurnRetainsVelocityAndBoundsEachRenderStep()
        {
            var orbit=new OrbitRenderMotion(0,0,1);
            orbit.SetTarget(180,0);
            Assert.That(orbit.AngularVelocity,Is.Zero);
            var previous=orbit.AngleAt(0);
            for(var i=1;i<=120;i++)
            {
                var angle=orbit.AngleAt(i/120f);
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(previous,angle)),Is.LessThanOrEqualTo(720f/120+.001f));
                Assert.That(Mathf.Abs(orbit.AngularVelocity),Is.LessThanOrEqualTo(720));
                previous=angle;
            }
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(previous,180)),Is.LessThan(.001f));
            orbit.SetTarget(90,1);
            orbit.AngleAt(1.01f);
            var velocity=orbit.AngularVelocity;
            var position=orbit.AngleAt(1.01f);
            orbit.SetTarget(-90,1.01f);
            Assert.That(orbit.AngularVelocity,Is.EqualTo(velocity));
            Assert.That(orbit.AngleAt(1.01f),Is.EqualTo(position));
            Assert.That(orbit.MaximumAngularLag,Is.GreaterThanOrEqualTo(179));
        }

        [Test] public void WarmMotionEvaluationDoesNotAllocate()
        {
            var orbit = new OrbitRenderMotion(0, 0);
            orbit.SetTarget(45, 1);
            var sum = 0f;
            for (var i = 0; i < 100; i++) sum += orbit.AngleAt(1.05f);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) sum += orbit.AngleAt(1.05f);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(sum, Is.GreaterThan(0));
        }

        [Test] public void AttackAndHitPoseReturnToTheSameContinuousWalkPhase()
        {
            var start=ActorMotion.Sample(2,true,0,0);
            var end=ActorMotion.Sample(2,true,1,1);
            Assert.That(Vector2.Distance(start.Offset,end.Offset),Is.LessThan(.0001f));
            Assert.That(Vector2.Distance(start.Scale,end.Scale),Is.LessThan(.0001f));
            Assert.That(Mathf.Abs(start.Degrees-end.Degrees),Is.LessThan(.0001f));
        }

        [Test] public void ActorTurnCrossesAngleWrapWithoutSpinningTheSilhouette()
        {
            Vector2 Direction(float degrees)=>new Vector2(Mathf.Cos(degrees*Mathf.Deg2Rad),Mathf.Sin(degrees*Mathf.Deg2Rad));
            var facing=new ActorFacingMotion(Direction(179),0);
            facing.SetDirection(Direction(-179),1);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(180,facing.AngleAt(1.05f))),Is.LessThan(.001f));
            Assert.That(Mathf.Abs(facing.Sample(1.05f).LeanDegrees),Is.LessThanOrEqualTo(GameVisualTokens.ActorHeadingLean));
            Assert.That(facing.Sample(1.05f).MirrorBlend,Is.EqualTo(1));
        }

        [Test] public void FacingReversalNarrowsOneSilhouetteAndVerticalNoiseDoesNotFlipAgain()
        {
            var facing=new ActorFacingMotion(Vector2.right,0);
            facing.SetDirection(Vector2.left,1);
            Assert.That(facing.Sample(1).MirrorBlend,Is.Zero);
            Assert.That(facing.Sample(1.05f).MirrorBlend,Is.EqualTo(.5f).Within(.001f));
            Assert.That(Mathf.Abs(facing.Sample(1.05f).SignedWidth),Is.EqualTo(.8f).Within(.001f));
            Assert.That(facing.Sample(1.02f).SignedWidth,Is.GreaterThan(.8f));
            Assert.That(facing.Sample(1.08f).SignedWidth,Is.LessThan(-.8f));
            Assert.That(facing.Sample(1.101f).MirrorBlend,Is.EqualTo(1));
            facing.SetDirection(new Vector2(.01f,1),2);
            Assert.That(facing.Sample(2.2f).MirrorBlend,Is.EqualTo(1));
        }
    }
}
