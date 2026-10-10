using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class LandscapeUiReviewTests
    {
        [Test]
        public void CaptureIsOptInAndRequiresExplicitOutput()
        {
            Assert.That(LandscapeUiReview.ParseOutput(new[]{"game"}),Is.Null);
            Assert.That(LandscapeUiReview.ParseOutput(new[]{"game","--landscape-ui-output","ignored"}),Is.Null);
            Assert.Throws<System.ArgumentException>(()=>LandscapeUiReview.ParseOutput(new[]{"game","--landscape-ui-review"}));
            Assert.Throws<System.ArgumentException>(()=>LandscapeUiReview.ParseOutput(new[]{"game","--landscape-ui-review","--landscape-ui-output","--another"}));
            Assert.That(LandscapeUiReview.ParseOutput(new[]{"game","--landscape-ui-review","--landscape-ui-output","/tmp/ui-review"}),Is.EqualTo("/tmp/ui-review"));
        }
        [Test]
        public void MaskIntersectionRejectsAButtonOutsideScrollViewport()
        {
            var screen=new Rect(0,0,1280,800);
            var visible=LandscapeUiReview.Intersection(screen,new Rect(40,80,1200,660));
            Assert.That(LandscapeUiReview.ContainsBounds(visible,new Rect(50,90,200,80)),Is.True);
            Assert.That(LandscapeUiReview.ContainsBounds(visible,new Rect(50,30,200,80)),Is.False);
            var empty=LandscapeUiReview.Intersection(screen,new Rect(1400,900,100,100));
            Assert.That(empty.size,Is.EqualTo(Vector2.zero));
            Assert.That(LandscapeUiReview.ContainsBounds(empty,new Rect(1400,900,1,1)),Is.False);
        }
    }
}
