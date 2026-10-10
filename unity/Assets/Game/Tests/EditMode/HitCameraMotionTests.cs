using System;
using System.Reflection;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class HitCameraMotionTests
    {
        [TestCase(640,2f)]
        [TestCase(1080,5.2f)]
        [TestCase(1818,5.2f)]
        [TestCase(2560,8f)]
        public void RepeatedDamageKeepsCameraStationaryAndRetainsLocalHeroFlash(int height,float halfHeight)
        {
            var owner=new GameObject("Damage camera regression");
            try
            {
                var renderer=owner.AddComponent<WorldRenderer>();
                var camera=owner.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=halfHeight;
                camera.pixelRect=new Rect(0,0,height*.6f,height);
                var follow=new RunCamera(camera,new WorldCameraSettings(1000,2400,6000,600,120,800));
                var lord=new Vector2(4,7);follow.Present(lord,2000,0);
                var original=camera.transform.position;
                var onEvent=typeof(WorldRenderer).GetMethod("OnEvent",BindingFlags.Instance|BindingFlags.NonPublic);
                var clock=typeof(WorldRenderer).GetField("visualTime",BindingFlags.Instance|BindingFlags.NonPublic);
                var flash=typeof(WorldRenderer).GetField("heroHitUntil",BindingFlags.Instance|BindingFlags.NonPublic);
                for(var i=0;i<600;i++)
                {
                    var time=i/60f;clock.SetValue(renderer,time);
                    if(i%4==0)
                    {
                        var hit=new PresentationEvent(i,i,PresentationKind.LordHit,"test",new WorldPoint(4000,7000),new WorldPoint(0,0),"hit",0,1,Array.Empty<WorldPoint>(),Array.Empty<int>());
                        onEvent.Invoke(renderer,new object[]{hit});
                        Assert.That((float)flash.GetValue(renderer),Is.GreaterThan(time),"Damage must still trigger the local hero flash.");
                    }
                    renderer.ShakeEnabled=i%2==0;
                    follow.Present(lord,2000,1f/60);follow.SetVisualOffset(renderer.CameraShakeOffset);
                    Assert.That(renderer.CameraShakeOffset,Is.EqualTo(Vector2.zero));
                    Assert.That(camera.transform.position,Is.EqualTo(original));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
}
