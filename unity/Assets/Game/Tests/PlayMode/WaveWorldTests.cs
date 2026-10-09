#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;
namespace Tests.PlayMode
{
    public sealed class WaveWorldTests
    {
        [UnityTest]
        public IEnumerator ActualWaveSnapshotDrawsAuthoredPixels()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Requires wave-1a export; historical suites run separately.");
            ArtCatalog.ProfileName=CanonicalContent.ProfileName;
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            for(var i=0;i<600&&session.View.Status!=RunStatus.Completed;i++)
            {
                var frame=session.View.CaptureFrame();
                var command=frame.Status==RunStatus.AwaitingCard
                    ?new ReplayCommand(session.NextSequence,frame.Tick,ReplayCommandKind.ChooseCard,default,session.View.CaptureCards().Cards.OrderBy(WaveCaptureInput.CardRank).First())
                    :new ReplayCommand(session.NextSequence,frame.Tick,ReplayCommandKind.Advance,new PlayerInput(1000,0));
                session.Apply(command);
            }
            var current=session.View.CaptureFrame();var wave=((IWaveRunView)session.View).CaptureWaveRuntime();
            var envelope=WavePresentation.Envelope(catalog,current,session.View.CaptureCards(),wave);
            var owner=new GameObject("Wave real snapshot QA");var cameraOwner=new GameObject("Wave QA camera");
            var camera=cameraOwner.AddComponent<Camera>();var texture=new RenderTexture(720,1280,24);camera.targetTexture=texture;
            Texture2D image=null;
            try
            {
                var config=CanonicalContent.Presentation.Camera;var world=owner.AddComponent<WorldRenderer>();
                world.Initialize(camera,new WorldCameraSettings(config.WorldUnitsPerUnityUnit,config.MinHalfHeight,config.MaxHalfHeight,config.EstatePadding,config.FollowMilliseconds,config.ZoomMilliseconds),catalog.Tuning.DefaultEstate,current.MapWidth,current.MapHeight);
                world.AcceptWave(catalog,wave);
                for(var i=0;i<3;i++){world.Present(current,current,envelope,1,1f/30,new Rect(0,0,720,1280));yield return null;}
                Assert.That(world.UnsupportedShapeCount,Is.Zero);Assert.That(world.DrawCalls,Is.GreaterThan(0));
                var previous=RenderTexture.active;
                try{RenderTexture.active=texture;image=new Texture2D(720,1280,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,720,1280),0,0);image.Apply();}
                finally{RenderTexture.active=previous;}
                Assert.That(image.GetPixels32().Select(p=>(p.r>>4)*256+(p.g>>4)*16+(p.b>>4)).Distinct().Count(),Is.GreaterThan(20));
                var output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../artifacts/wave1a/editor"));Directory.CreateDirectory(output);
                File.WriteAllBytes(Path.Combine(output,"snapshot-component-qa.png"),image.EncodeToPNG());
                File.WriteAllText(Path.Combine(output,"snapshot-component-qa.txt"),$"profile={CanonicalContent.ProfileName}\ntick={current.Tick}\nstateHash={session.ComputeStateHash()}\nclassification=accelerated component QA; not normal-play footage\n");
            }
            finally{if(image!=null)Object.Destroy(image);Object.Destroy(owner);Object.Destroy(cameraOwner);texture.Release();Object.Destroy(texture);}
        }
    }
}
#endif
