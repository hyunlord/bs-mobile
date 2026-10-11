using Game.View;
using NUnit.Framework;
using UnityEngine;
namespace Tests.EditMode
{
    public sealed class PortraitViewportTests
    {
        [Test]
        public void DesktopCentersPortraitAndPhoneKeepsItsActualAspect()
        {
            var desktop=PortraitViewport.Calculate(new Vector2(1920,1080),new Rect(0,0,1920,1080),false);
            Assert.That(desktop.Pixels.height,Is.EqualTo(1080));
            Assert.That(desktop.Pixels.width/desktop.Pixels.height,Is.EqualTo(390f/844).Within(.00001));
            Assert.That(desktop.Pixels.center,Is.EqualTo(new Vector2(960,540)));
            var phone=PortraitViewport.Calculate(new Vector2(1080,2520),new Rect(0,80,1080,2300),true);
            Assert.That(phone.Pixels,Is.EqualTo(new Rect(0,0,1080,2520)));
            Assert.That(phone.SafePixels,Is.EqualTo(new Rect(0,80,1080,2300)));
            Assert.That(phone.Pixels.width/phone.Scale,Is.EqualTo(390).Within(.001));
            Assert.That(phone.Pixels.height/phone.Scale,Is.GreaterThan(844));
        }
        [Test]
        public void DesktopSafeAreaIsIntersectionAndThirteenIconsFit()
        {
            var viewport=PortraitViewport.Calculate(new Vector2(390,844),new Rect(0,34,390,763),false);
            Assert.That(viewport.SafePixels.height,Is.EqualTo(763));
            Assert.That((763-PortraitTokens.HudHeight)/763,Is.GreaterThanOrEqualTo(.9f));
            Assert.That(13*PortraitTokens.Equipment+12*1.5f+32,Is.LessThanOrEqualTo(390));
            Assert.That(PortraitTokens.Touch,Is.GreaterThanOrEqualTo(44));
        }
        [Test]
        public void BaseViewportSurvivesEndlessFieldResizing()
        {
            var owner=new GameObject("Portrait camera test");var target=new RenderTexture(1920,1080,0);
            try
            {
                var camera=owner.AddComponent<Camera>();camera.targetTexture=target;
                var follow=new RunCamera(camera,new WorldCameraSettings(1000,2400,6000,600,120,800)){EndlessField=true};
                follow.SetMapBounds(10000,10000);
                var rect=new Rect(710,0,500,1080);follow.SetBaseViewport(rect);follow.Present(Vector2.one,2000,0);
                Assert.That(camera.pixelRect.x,Is.EqualTo(710).Within(.01));
                Assert.That(camera.pixelRect.width,Is.EqualTo(500).Within(.01));
                var next=new Rect(760,0,400,1080);follow.SetBaseViewport(next);follow.Present(Vector2.one,2000,.016f);
                Assert.That(camera.pixelRect.width,Is.EqualTo(400).Within(.01));
                follow.EndlessField=false;follow.Present(Vector2.one,2000,0);
                Assert.That(camera.pixelRect.width,Is.EqualTo(400).Within(.01));
            }
            finally{Object.DestroyImmediate(owner);Object.DestroyImmediate(target);}
        }
    }
}
