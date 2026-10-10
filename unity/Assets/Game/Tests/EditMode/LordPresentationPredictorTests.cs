using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class LordPresentationPredictorTests
    {
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
