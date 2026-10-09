#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        [UnityTest, Timeout(300000)]
        public IEnumerator ActualWaveSnapshotDrawsAuthoredPixels()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Requires wave-1a export; historical suites run separately.");
            ArtCatalog.ProfileName=CanonicalContent.ProfileName;
            var catalog=CanonicalContent.CreateCatalog();
            var replayPath=System.Environment.GetEnvironmentVariable("WAVE_QA_REPLAY");
            var requestedTick=System.Environment.GetEnvironmentVariable("WAVE_QA_TICK");
            var provenance="source=default 600-command component scenario\n";
            var captureName="snapshot-component-qa";
            InteractiveSession session;
            if(!string.IsNullOrWhiteSpace(replayPath))
            {
                Assert.That(int.TryParse(requestedTick,out var targetTick)&&targetTick>0,Is.True,"WAVE_QA_REPLAY requires a positive WAVE_QA_TICK.");
                replayPath=Path.GetFullPath(replayPath);
                var bytes=File.ReadAllBytes(replayPath);ReplayDocument replay;
                using(var input=new MemoryStream(bytes,false))replay=ReplayCodec.Read(input);
                Assert.That(replay.Header.Options.DataHash,Is.EqualTo(CanonicalContent.DataHash),"Replay must use the exported canonical content.");
                Assert.That(replay.Header.Options.Run.Scenario,Is.EqualTo("normal"));
                Assert.That(replay.Commands.Any(c=>c.Kind==ReplayCommandKind.GrantLevel||c.Kind==ReplayCommandKind.SetInvulnerable||c.Kind==ReplayCommandKind.SetSpawnPermille),Is.False,"Normal-input QA rejects debug commands, including later commands outside the captured prefix.");
                Assert.That(targetTick,Is.LessThanOrEqualTo(replay.End.Tick),"Requested tick must exist in the recorded run.");
                session=new InteractiveSession(catalog,replay.Header.Options);
                var checkpoints=replay.Checkpoints.ToDictionary(c=>c.AppliedCommands);
                var checkedCheckpoints=0;
                void VerifyCheckpoint()
                {
                    if(!checkpoints.TryGetValue(session.NextSequence,out var checkpoint))return;
                    Assert.That(session.View.CaptureFrame().Tick,Is.EqualTo(checkpoint.Tick));
                    Assert.That(session.ComputeStateHash(),Is.EqualTo(checkpoint.StateHash),"Recorded checkpoint "+checkpoint.AppliedCommands);
                    checkedCheckpoints++;
                }
                VerifyCheckpoint();
                foreach(var command in replay.Commands)
                {
                    if(session.View.CaptureFrame().Tick>=targetTick)break;
                    session.Apply(command);VerifyCheckpoint();
                    if(session.NextSequence%300==0)yield return null;
                }
                Assert.That(session.View.CaptureFrame().Tick,Is.EqualTo(targetTick));
                string replayHash;using(var sha=SHA256.Create())replayHash=System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
                provenance=$"source=recorded normal input prefix\nreplay={replayPath}\nreplaySha256={replayHash}\nrecordedEndTick={replay.End.Tick}\nrecordedEndKind={replay.End.Kind}\nappliedCommands={session.NextSequence}\nverifiedCheckpoints={checkedCheckpoints}\nseed={replay.Header.Options.Run.Seed}\ndataHash={CanonicalContent.DataHash}\nrendererCommit={BuildIdentity.Commit}\nrendererSourceHash={BuildIdentity.SourceHash}\nrendererSourceDirty={BuildIdentity.SourceDirty}\n";
                captureName+="-replay-"+targetTick;
            }
            else
            {
                Assert.That(string.IsNullOrWhiteSpace(requestedTick),Is.True,"WAVE_QA_TICK requires WAVE_QA_REPLAY.");
                session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
                for(var i=0;i<600&&session.View.Status!=RunStatus.Completed;i++)
                {
                    var frame=session.View.CaptureFrame();
                    var command=frame.Status==RunStatus.AwaitingCard
                        ?new ReplayCommand(session.NextSequence,frame.Tick,ReplayCommandKind.ChooseCard,default,session.View.CaptureCards().Cards.OrderBy(WaveCaptureInput.CardRank).First())
                        :new ReplayCommand(session.NextSequence,frame.Tick,ReplayCommandKind.Advance,new PlayerInput(1000,0));
                    session.Apply(command);
                }
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
                File.WriteAllBytes(Path.Combine(output,captureName+".png"),image.EncodeToPNG());
                File.WriteAllText(Path.Combine(output,captureName+".txt"),$"{provenance}profile={CanonicalContent.ProfileName}\ntick={current.Tick}\nstateHash={session.ComputeStateHash()}\nclassification=accelerated component QA; not normal-play footage\n");
            }
            finally{if(image!=null)Object.Destroy(image);Object.Destroy(owner);Object.Destroy(cameraOwner);texture.Release();Object.Destroy(texture);}
        }
    }
}
#endif
