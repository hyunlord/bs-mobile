using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class LordPresentationPredictorTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EndlessPresentationCrossesNegativeAndOldPositiveEdges(bool rational)
        {
            var predictor=new LordPresentationPredictor();
            predictor.SetTopology(true);
            var position=new Vector2(-1,13);
            predictor.Initialize(position,new Vector2(12,12),.72f,1f/30,0,rational,.002f);
            for(var frame=1;frame<=120;frame++)
            {
                if(frame%2==0){position.x-=.024f;predictor.Observe(position,frame/2,Vector2.left,rational?.72f:(float?)null);}
                var rendered=predictor.Present(position,Vector2.left,1f/60,frame%2/60f,false);
                Assert.That(rendered.x,Is.LessThan(0));Assert.That(rendered.y,Is.EqualTo(13).Within(.0001f));
            }
            Assert.That(predictor.Position.x,Is.LessThan(-2));
        }

        [Test]
        public void TemporaryArenaConstrainsPredictionAndDisappearsWithoutAWorldWall()
        {
            var predictor=new LordPresentationPredictor();var origin=new Vector2(30,-20);
            predictor.SetTopology(true,origin,1);
            predictor.Initialize(origin+Vector2.right,new Vector2(12,12),.72f,1f/30,0);
            for(var i=0;i<20;i++)Assert.That(Vector2.Distance(origin,predictor.Present(origin+Vector2.right,Vector2.right,1f/60,1f/30,false)),Is.LessThanOrEqualTo(1.00001f));
            predictor.SetTopology(true);
            var outside=predictor.Present(origin+Vector2.right,Vector2.right,1f/60,1f/30,false);
            Assert.That(outside.x,Is.GreaterThan(origin.x+1));
        }

        [Test]
        public void ExperimentalRationalStepsHaveConstantPresentationVelocity()
        {
            var predictor = new LordPresentationPredictor();
            var position = new Vector2(6, 6);
            const float speed = .936f;
            predictor.Initialize(position, new Vector2(100,100), speed, 1f/30, 0, true, .002f);
            var previous = position;
            for (var frame = 1; frame <= 1200; frame++)
            {
                if (frame % 2 == 0)
                {
                    var tick = frame / 2;
                    position.x = (6000 + tick * 312 / 10) / 1000f;
                    predictor.Observe(position, tick, Vector2.right, speed);
                }
                var shown = predictor.Present(position, Vector2.right, 1f/60, frame % 2 / 60f, false);
                if (frame > 120) Assert.That(shown.x - previous.x, Is.EqualTo(speed/60).Within(.00001f));
                Assert.That(predictor.Speed, Is.EqualTo(speed));
                previous = shown;
            }
        }

        [Test]
        public void ExperimentalKnockbackMovesTheAnchorWithoutTeachingItReverseSpeed()
        {
            var predictor = new LordPresentationPredictor();
            var position = new Vector2(6,6);
            const float speed = .936f;
            predictor.Initialize(position, new Vector2(100,100), speed, 1f/30, 0, true, .002f);
            predictor.Present(position, Vector2.right, 1f/60, 1f/60, false);
            position.x += .031f - .072f;
            predictor.Observe(position, 1, Vector2.right, speed);
            var previous = predictor.Present(position, Vector2.right, 1f/60, 0, false);
            Assert.That(predictor.Speed, Is.EqualTo(speed));
            for (var frame = 1; frame <= 240; frame++)
            {
                if (frame % 2 == 0)
                {
                    position.x = (6000 + (1 + frame/2) * 312/10 - 72) / 1000f;
                    predictor.Observe(position, 1 + frame/2, Vector2.right, speed);
                }
                var shown = predictor.Present(position, Vector2.right, 1f/60, frame % 2/60f, false);
                Assert.That(shown.x, Is.GreaterThan(previous.x));
                Assert.That(predictor.Speed, Is.EqualTo(speed));
                previous = shown;
            }
        }

        [Test]
        public void ContactOnlyExperimentRetainsIntegerObliqueLocomotion()
        {
            var predictor = new LordPresentationPredictor();
            var input = new Vector2(.2f,.07f);
            var position = new Vector2(6,6);
            predictor.Initialize(position, new Vector2(100,100), .72f, 1f/30, 0, true, .002f, .001f);
            var previous = position;
            for (var frame = 1; frame <= 720; frame++)
            {
                if (frame % 2 == 0)
                {
                    position = new Vector2((6000 + frame/2*4)/1000f,(6000 + frame/2)/1000f);
                    predictor.Observe(position,frame/2,input,.72f);
                }
                var shown = predictor.Present(position,input,1f/60,frame%2/60f,false);
                if (frame > 120) Assert.That(Vector2.Distance(shown-previous,new Vector2(.002f,.0005f)),Is.LessThan(.000005f));
                previous = shown;
            }
        }

        [Test]
        public void ConstantSlowObliqueInputKeepsTheMeasuredIntegerVelocityBetweenTicks()
        {
            var predictor=new LordPresentationPredictor();
            var input=new Vector2(.2f,.07f);
            var authoritative=new Vector2(6,6);
            predictor.Initialize(authoritative,new Vector2(20,20),.72f,1f/30,0);
            var previous=authoritative;
            var expected=new Vector2(.002f,.0005f);
            for(var frame=1;frame<=720;frame++)
            {
                if(frame%2==0)
                {
                    var tick=frame/2;
                    authoritative=new Vector2((6000+tick*4)/1000f,(6000+tick)/1000f);
                    predictor.Observe(authoritative,tick,input);
                }
                var rendered=predictor.Present(authoritative,input,1f/60,frame%2/60f,false);
                if(frame>120)Assert.That(Vector2.Distance(rendered-previous,expected),Is.LessThan(.000002f),"Constant integer displacement must not alternate correction directions.");
                previous=rendered;
            }
        }

        [Test]
        public void ReleasingMeasuredMotionSettlesMonotonicallyWithoutCrossingTheAuthoritativePosition()
        {
            var predictor=new LordPresentationPredictor();
            var position=new Vector2(5,5);
            predictor.Initialize(position,new Vector2(10,10),.72f,1f/30,0);
            position.x+=.024f;
            predictor.Observe(position,1,Vector2.right);
            var rendered=predictor.Present(position,Vector2.right,1f/60,1f/60,false);
            var previousDistance=Vector2.Distance(rendered,position);
            Assert.That(previousDistance,Is.GreaterThan(0));
            for(var frame=0;frame<120;frame++)
            {
                rendered=predictor.Present(position,Vector2.zero,1f/60,0,false);
                var distance=Vector2.Distance(rendered,position);
                Assert.That(distance,Is.LessThanOrEqualTo(previousDistance+.000001f));
                Assert.That(rendered.x,Is.GreaterThanOrEqualTo(position.x));
                previousDistance=distance;
            }
            Assert.That(rendered,Is.EqualTo(position));
        }

        [Test]
        public void InputMovesBeforeATickAndReleaseSettlesWithoutChangingAuthoritativePosition()
        {
            var predictor = new LordPresentationPredictor();
            var authoritative = new Vector2(5,5);
            predictor.Initialize(authoritative,new Vector2(10,10),3,1f/30,0);
            var immediate = predictor.Present(authoritative,Vector2.right,1f/120,1f/120,false);
            Assert.That(immediate.x,Is.GreaterThan(authoritative.x));
            Assert.That(Vector2.Distance(immediate,authoritative),Is.LessThanOrEqualTo(.1f));
            for(var i=0;i<120;i++)predictor.Present(authoritative,Vector2.zero,1f/60,0,false);
            Assert.That(predictor.Position,Is.EqualTo(authoritative));
            Assert.That(authoritative,Is.EqualTo(new Vector2(5,5)));
        }

        [Test]
        public void ReversalReconcilesWithinOneTickAndPauseResetsToTheRealPosition()
        {
            var predictor = new LordPresentationPredictor();
            var position = new Vector2(5,5);
            predictor.Initialize(position,new Vector2(10,10),3,1f/30,0);
            var before = predictor.Present(position,Vector2.right,1f/60,1f/60,false);
            position += Vector2.right*.1f;
            predictor.Observe(position,1,Vector2.right);
            var after = predictor.Present(position,Vector2.left,1f/60,0,false);
            Assert.That(Vector2.Distance(before,after),Is.LessThanOrEqualTo(.1f));
            Assert.That(Vector2.Distance(position,after),Is.LessThanOrEqualTo(.10001f));
            Assert.That(predictor.Present(position,Vector2.left,1f/60,0,true),Is.EqualTo(position));
            Assert.That(predictor.ResetThisFrame,Is.True);
        }

        [Test]
        public void ActualDisplacementAdaptsSpeedAndThePresentationNeverLeavesTheMap()
        {
            var predictor = new LordPresentationPredictor();
            predictor.Initialize(new Vector2(5,5),new Vector2(10,10),3,1f/30,0);
            predictor.Observe(new Vector2(5.2f,5),1,Vector2.right);
            Assert.That(predictor.Speed,Is.EqualTo(6).Within(.001f));
            var rendered = predictor.Present(new Vector2(9.99f,5),Vector2.right,1f/60,1f/30,false);
            Assert.That(rendered.x,Is.InRange(0,10));
            Assert.That(rendered.y,Is.InRange(0,10));
        }
    }
}
