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
        [UnityTest]
        public IEnumerator NewAndRepeatedSnapshotsRemoveStaleEnemyPresentation()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName=CanonicalContent.ProfileName;
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var empty=session.View.CaptureFrame() with {Enemies=System.Array.Empty<EnemyView>()};
            var wave=((IWaveRunView)session.View).CaptureWaveRuntime() with {Enemies=System.Array.Empty<WaveEnemyView>()};
            var actor=new EnemyView(700,catalog.WaveRuntime.BossId,empty.Lord.Position,100,100);
            var occupied=empty with {Enemies=new[]{actor}};
            var marked=wave with {Enemies=new[]{new WaveEnemyView(700,"approach",actor.Position,actor.Position,100,true,false,0)}};
            var owner=new GameObject("Wave snapshot transition QA");var cameraOwner=new GameObject("Wave snapshot transition camera");
            var camera=cameraOwner.AddComponent<Camera>();var texture=new RenderTexture(720,1280,24);camera.targetTexture=texture;
            try
            {
                var world=owner.AddComponent<WorldRenderer>();var config=CanonicalContent.Presentation.Camera;
                world.Initialize(camera,new WorldCameraSettings(config.WorldUnitsPerUnityUnit,config.MinHalfHeight,config.MaxHalfHeight,config.EstatePadding,config.FollowMilliseconds,config.ZoomMilliseconds),catalog.Tuning.DefaultEstate,empty.MapWidth,empty.MapHeight);
                void Present(RunFrame frame,WaveRuntimeFrame state)
                {
                    world.AcceptWave(catalog,state);
                    world.Present(frame,frame,WavePresentation.Envelope(catalog,frame,session.View.CaptureCards(),state),1,0,new Rect(0,0,720,1280));
                }
                Present(empty,wave);yield return null;var baseline=world.SubmittedInstances;
                Present(occupied,marked);yield return null;var populated=world.SubmittedInstances;
                Assert.That(populated,Is.EqualTo(baseline+2),"One actual enemy and its wet marker are submitted.");
                Present(occupied,marked);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(populated),"Repeated snapshot does not duplicate presentation.");
                Present(empty with {Tick=empty.Tick+1},marked);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline),"No enemy or wet marker survives an actor removed from the new frame.");
                Present(occupied,wave);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+1),"A new wave snapshot without the status removes its wet marker.");
                world.AcceptWave(null,null);
                Present(occupied,marked);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(populated),"Profile reset rebuilds presentation from the current snapshots.");
            }
            finally{Object.Destroy(owner);Object.Destroy(cameraOwner);texture.Release();Object.Destroy(texture);}
        }
        [UnityTest]
        public IEnumerator EvolutionCuesRequireActualBirthAndRelocatedRepairAnchor()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Requires wave-1a export.");
            ArtCatalog.ProfileName=CanonicalContent.ProfileName;
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var frame=session.View.CaptureFrame();var source=((IWaveRunView)session.View).CaptureWaveRuntime();
            var planting=catalog.WaveRuntime.Evolutions.Values.Single(e=>e.Kind==WaveEvolutionKind.PlantingArc);
            var repair=catalog.WaveRuntime.Evolutions.Values.Single(e=>e.Kind==WaveEvolutionKind.RepairOrbit);
            var point=new WorldPoint(frame.Lord.Position.X+400,frame.Lord.Position.Y);
            var plot=new WaveWorkView(900,planting.InputIds[1],"grain",point,0,100,false,100,-1,false,false,false,false,false);
            var state=source with {Work=new[]{plot},Events=System.Array.Empty<WaveEvent>()};
            var owner=new GameObject("Evolution cue contract QA");var cameraOwner=new GameObject("Evolution cue camera");
            var camera=cameraOwner.AddComponent<Camera>();var texture=new RenderTexture(720,1280,24);camera.targetTexture=texture;
            try
            {
                var world=owner.AddComponent<WorldRenderer>();var config=CanonicalContent.Presentation.Camera;
                world.Initialize(camera,new WorldCameraSettings(config.WorldUnitsPerUnityUnit,config.MinHalfHeight,config.MaxHalfHeight,config.EstatePadding,config.FollowMilliseconds,config.ZoomMilliseconds),catalog.Tuning.DefaultEstate,frame.MapWidth,frame.MapHeight);
                void Present(WaveRuntimeFrame snapshot,float delta=0)
                {
                    world.AcceptWave(catalog,snapshot);
                    world.Present(frame,frame,WavePresentation.Envelope(catalog,frame,session.View.CaptureCards(),snapshot),1,delta,new Rect(0,0,720,1280));
                }
                Present(state);yield return null;var baseline=world.SubmittedInstances;
                var attack=new WaveEvent(1,frame.Tick,"attack",planting.Id,-1,frame.Lord.Position,point,400);
                Present(state with {Events=new[]{attack}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+1),"An evolved arc alone never fabricates a seed-birth cue when planting is capacity-rejected.");
                Present(state,1);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline));
                var birth=new WaveEvent(2,frame.Tick,"work-created",plot.Source,999,point,point,0);
                Present(state with {Events=new[]{birth}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline),"Missing live grain cannot be highlighted as a new seed.");
                var planted=state with {Events=new[]{birth with {Id=3,SubjectId=plot.Id}}};
                Present(planted);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+1));
                Present(planted);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+1),"Repeated frames cannot duplicate a birth pulse.");
                Present(state with {Work=new[]{plot with {Health=0}}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline-1),"An admitted birth pulse disappears when its actual grain dies.");
                Present(state,1);yield return null;
                var building=plot with {Id=901,Source=repair.InputIds[1],Kind="building"};
                var work=state with {Work=new[]{building}};
                Present(work);yield return null;var construction=world.SubmittedInstances;
                var orbit=new WaveAttackView(repair.Id,"orbit",point,new WorldPoint(point.X+300,point.Y),100,frame.Tick+100);
                Present(work with {Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+2),"Only the actual off-hero repair origin gets one anchor badge alongside its fragment.");
                Present(work with {Attacks=new[]{orbit with {Origin=frame.Lord.Position}}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+1),"The regular hero-centered orbit must not imply a building anchor.");
                Present(work with {Work=new[]{building with {Complete=true}},Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+1),"Completed work is no longer a repair anchor.");
                Present(work with {Work=System.Array.Empty<WaveWorkView>(),Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction),"Removed work cannot retain an anchor cue.");
                Present(work);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction),"No active orbit means no anchor cue.");
                Present(work with {Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+2),"A returning real orbit displays its current anchor again.");
            }
            finally{Object.Destroy(owner);Object.Destroy(cameraOwner);texture.Release();Object.Destroy(texture);}
        }
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
