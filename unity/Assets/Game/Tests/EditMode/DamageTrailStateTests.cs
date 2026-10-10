using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.EditMode
{
    public sealed class DamageTrailStateTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void FeedbackIsOptInNonInteractiveAndBehindHud(bool enabled)
        {
            var owner=new GameObject("Damage feedback UI",typeof(RectTransform));
            var font=FontProvider.Create(System.Array.Empty<string>());
            try
            {
                var ui=owner.AddComponent<UiShell>();ui.Initialize(font);
                var hud=new UiHud(ui,null,null,enabled);
                var edge=ui.Content.Find("Damage screen edges");
                Assert.That(edge!=null,Is.EqualTo(enabled));
                if(!enabled)return;
                Assert.That(edge.GetSiblingIndex(),Is.LessThan(ui.Content.Find("HUD").GetSiblingIndex()));
                Assert.That(edge.GetComponent<Graphic>().raycastTarget,Is.False);
                var trail=ui.Content.Find("HUD/Vitals backing/Health/Damage trail");
                Assert.That(trail,Is.Not.Null);Assert.That(trail.GetComponent<Image>().color,Is.EqualTo(Color.white));
                Assert.That(trail.GetComponent<Image>().raycastTarget,Is.False);
                Assert.That(trail.GetSiblingIndex(),Is.LessThan(trail.parent.Find("Fill").GetSiblingIndex()));
            }
            finally{Object.DestroyImmediate(owner);Object.DestroyImmediate(font);}
        }

        [Test]
        public void LostHealthIsExactThenHeldAndSmoothlyDecayed()
        {
            var state=new DamageTrailState();state.Present(100,100,0,0);state.Present(70,100,1,.016f);
            Assert.That(state.HealthFraction,Is.EqualTo(.7f).Within(.00001));
            Assert.That(state.TrailFraction-state.HealthFraction,Is.EqualTo(.3f).Within(.00001));
            Assert.That(state.EdgeOpacity,Is.GreaterThan(0));
            state.Present(70,100,2,.05f);Assert.That(state.TrailFraction,Is.EqualTo(1));
            state.Present(70,100,3,.15f);Assert.That(state.TrailFraction,Is.InRange(.7f,.999f));
            state.Present(70,100,4,3);Assert.That(state.TrailFraction,Is.EqualTo(.7f));Assert.That(state.EdgeOpacity,Is.Zero);
        }
        [Test]
        public void FramePartitionDoesNotChangePostHitDecay()
        {
            var one=new DamageTrailState();var many=new DamageTrailState();
            one.Present(100,100,0,0);many.Present(100,100,0,0);one.Present(60,100,1,0);many.Present(60,100,1,0);
            one.Present(60,100,2,.4f);
            for(var i=0;i<40;i++)many.Present(60,100,i+2,.01f);
            Assert.That(many.TrailFraction,Is.EqualTo(one.TrailFraction).Within(.00001));
            Assert.That(many.EdgeOpacity,Is.EqualTo(one.EdgeOpacity).Within(.00001));
        }
        [Test]
        public void RepeatedHitsAccumulateVisibleLossAndHealingNeverLeavesTrailBelowHealth()
        {
            var state=new DamageTrailState();state.Present(100,100,0,0);state.Present(80,100,1,0);state.Present(50,100,2,.04f);
            Assert.That(state.TrailFraction-state.HealthFraction,Is.EqualTo(.5f));
            state.Present(95,100,3,.04f);Assert.That(state.TrailFraction,Is.GreaterThanOrEqualTo(state.HealthFraction));
            state.Present(100,100,4,.04f);Assert.That(state.TrailFraction,Is.EqualTo(1));
        }
        [Test]
        public void RunRewindAndMaximumChangeResetWithoutInventingDamage()
        {
            var state=new DamageTrailState();state.Present(100,100,30,0);state.Present(40,100,31,0);
            state.Present(25,100,0,0);Assert.That(state.TrailFraction,Is.EqualTo(.25f));Assert.That(state.EdgeOpacity,Is.Zero);
            state.Present(20,200,1,0);Assert.That(state.TrailFraction,Is.EqualTo(.1f));Assert.That(state.EdgeOpacity,Is.Zero);
            state.Present(0,0,2,0);Assert.That(state.TrailFraction,Is.Zero);Assert.That(state.EdgeOpacity,Is.Zero);
        }
    }
}
