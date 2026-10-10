using System;
using System.Collections.Generic;
using Game.App.Generated;
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
        public void RepeatedDamageKeepsCameraStationary(int height,float halfHeight)
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
                var catalog=CanonicalContent.CreateCatalog();
                var session=new InteractiveSession(catalog,new(new(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
                var frame=session.View.CaptureFrame();
                var snapshot=new FirstPlayableFrame(Array.Empty<ActiveAttackView>(),Array.Empty<MapEventView>(),
                    Array.Empty<BuildingProgressView>(),Array.Empty<OfferedCardDetail>(),Array.Empty<EvolutionClueView>(),
                    Array.Empty<string>(),new Dictionary<string,int>(),0,0,catalog.Tuning.DefaultHero,
                    Array.Empty<PersonActivityView>(),Array.Empty<BossView>(),false);
                for(var i=0;i<600;i++)
                {
                    if(i%4==0)
                    {
                        var hit=new PresentationEvent(i,i,PresentationKind.LordHit,"test",new WorldPoint(4000,7000),new WorldPoint(0,0),"hit",0,1,Array.Empty<WorldPoint>(),Array.Empty<int>());
                        renderer.AcceptFrame(frame with { Tick=i, Events=new[]{hit} },snapshot);
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
