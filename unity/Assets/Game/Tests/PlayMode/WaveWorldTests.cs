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
                string[] Labels()=>owner.GetComponentsInChildren<TextMesh>().Select(label=>label.text).Distinct().ToArray();
                Present(state);yield return null;var baseline=world.SubmittedInstances;
                var attack=new WaveEvent(1,frame.Tick,"attack",planting.Id,-1,frame.Lord.Position,point,400);
                Present(state with {Events=new[]{attack}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+4),"Two evolved arc bodies and their edges never fabricate a seed-birth cue when planting is capacity-rejected.");
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
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+4),"Only the actual off-hero repair origin gets an anchor sigil and shield alongside its fragment.");
                Assert.That(Labels(),Does.Contain("수리 중"),"The actual building anchor identifies its action without relying on a blue burst.");
                Present(work with {Attacks=new[]{orbit with {Origin=frame.Lord.Position}}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+2),"The regular hero-centered orbit must not imply a building anchor.");
                Assert.That(Labels(),Does.Not.Contain("수리 중"));
                Present(work with {Work=new[]{building with {Complete=true}},Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+2),"Completed work is no longer a repair anchor.");
                Present(work with {Work=System.Array.Empty<WaveWorkView>(),Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+1),"Removed work cannot retain an anchor cue.");
                Present(work);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction),"No active orbit means no anchor cue.");
                Present(work with {Attacks=new[]{orbit}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(construction+4),"A returning real orbit displays its current anchor again.");
                Present(state,1);yield return null;
                var ripe=state with {Work=new[]{plot with {Complete=true}}};
                Present(ripe);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+2),"Only actual ripe grain receives its edge and one nearby readiness ear.");
                Assert.That(Labels(),Does.Contain("익음"));
                Present(ripe with {Work=new[]{plot with {Complete=true},plot with {Id=901,Complete=true,Position=new WorldPoint(point.X+20,point.Y)}}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+4),"Several ripe plots still have only one nearby readiness ear.");
                Present(ripe);yield return null;
                var collected=new WaveEvent(4,frame.Tick,"reward-collected",plot.Source,9901,point,point,10);
                var uptake=ripe with {Events=new[]{collected}};
                Present(uptake);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+3),"A consumed reward ID need not match a living grain ID to show actual uptake.");
                Assert.That(Labels(),Does.Contain("수확 +10 XP"),"Real collected XP is named in the world, not only the HUD bar.");
                Present(uptake);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+3),"Repeated collection event IDs do not duplicate uptake.");
                Present(ripe with {Events=new[]{collected with {Id=5,SubjectId=9902}}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+3),"Several real collections have at most one visible intake.");
                world.AcceptWave(null,null);Present(ripe);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+2),"A reset clears the transient collection cue.");
                Assert.That(Labels(),Does.Not.Contain("수확 +10 XP"));
                Present(ripe with {Events=new[]{collected with {Id=6,Amount=0}}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(baseline+2),"Zero experience cannot create an intake cue.");
                Assert.That(Labels().Any(label=>label.Contains("XP")),Is.False);
                var dead=state with {Work=new[]{plot with {Complete=true,Health=0}}};
                Present(dead);yield return null;var withoutGrain=world.SubmittedInstances;
                Assert.That(withoutGrain,Is.EqualTo(baseline-1),"Death removes readiness immediately and cannot fabricate harvesting.");
                Assert.That(Labels(),Does.Not.Contain("익음"));
                var harvest=new WaveEvent(7,frame.Tick,"harvest-complete",plot.Source,plot.Id,point,point,0);
                Present(dead with {Events=new[]{harvest}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(withoutGrain+1),"Real harvest follows grain death, so its upward ear uses the event position.");
                Present(dead with {Events=new[]{harvest}});yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(withoutGrain+1),"Repeated harvest IDs cannot duplicate the ear.");
                Present(dead,1);yield return null;
                Assert.That(world.SubmittedInstances,Is.EqualTo(withoutGrain),"The actual harvest cue expires.");
                Present(ripe with {Events=new[]{collected with {Id=8}}});yield return null;
                Present(ripe,1);yield return null;
                Assert.That(Labels().Any(label=>label.Contains("XP")),Is.False,"Collected XP text expires independently of later live crops.");
                var pooledLabels=owner.GetComponentsInChildren<TextMesh>(true);
                Assert.That(pooledLabels.Length,Is.LessThanOrEqualTo(6));
                foreach(var label in pooledLabels)
                {
                    var mesh=label.GetComponent<MeshRenderer>();
                    Assert.That(mesh.sortingOrder,Is.Zero,"Match world sprite sorting before using material queue hierarchy.");
                    Assert.That(mesh.sharedMaterial.renderQueue,Is.InRange(3070,3071));
                }
                world.AcceptWave(null,null);
                Assert.That(Labels(),Is.Empty,"Null/profile reset hides all semantic labels immediately.");
            }
            finally{Object.Destroy(owner);Object.Destroy(cameraOwner);texture.Release();Object.Destroy(texture);}
        }
        [UnityTest, Timeout(600000)]
        public IEnumerator RecordedWaveRangeExportsActualTickFrames()
        {
            var rangeStart=System.Environment.GetEnvironmentVariable("WAVE_QA_START_TICK");
            var rangeEnd=System.Environment.GetEnvironmentVariable("WAVE_QA_END_TICK");
            if(rangeStart==null&&rangeEnd==null)Assert.Ignore("Optional recorded-input sequence export.");
            Assert.That(CanonicalContent.ProfileName,Is.EqualTo("wave-1a"));
            Assert.That(int.TryParse(rangeStart,out var start)&&start>0,Is.True,"Positive WAVE_QA_START_TICK required.");
            Assert.That(int.TryParse(rangeEnd,out var end)&&end>=start&&(long)end-start+1<=1800,Is.True,"Inclusive range must contain 1..1800 ticks.");
            var replayPath=System.Environment.GetEnvironmentVariable("WAVE_QA_REPLAY");
            var output=System.Environment.GetEnvironmentVariable("WAVE_QA_SEQUENCE_OUTPUT");
            var expectedRendererCommit=System.Environment.GetEnvironmentVariable("WAVE_QA_RENDERER_COMMIT");
            Assert.That(!string.IsNullOrWhiteSpace(replayPath)&&File.Exists(replayPath),Is.True,"Recorded replay required.");
            Assert.That(!string.IsNullOrWhiteSpace(output),Is.True,"Explicit sequence output directory required.");
            Assert.That(expectedRendererCommit!=null&&expectedRendererCommit.Length==40&&expectedRendererCommit.All(System.Uri.IsHexDigit),Is.True,"Supply expected renderer source commit; external source verification is required.");
            Assert.That(BuildIdentity.Commit,Is.EqualTo(expectedRendererCommit),"Generated build identity must match the externally verified expected commit.");
            output=Path.GetFullPath(output);Directory.CreateDirectory(output);
            Assert.That(Directory.GetFileSystemEntries(output),Is.Empty,"Use an empty directory; do not mix frame sequences.");
            var bytes=File.ReadAllBytes(replayPath);ReplayDocument replay;
            using(var input=new MemoryStream(bytes,false))replay=ReplayCodec.Read(input);
            Assert.That(replay.Header.Options.DataHash,Is.EqualTo(CanonicalContent.DataHash));
            Assert.That(replay.Header.Options.Run.Scenario,Is.EqualTo("normal"));
            Assert.That(replay.Commands.All(c=>c.Kind==ReplayCommandKind.Advance||c.Kind==ReplayCommandKind.ChooseCard||c.Kind==ReplayCommandKind.SetAimMode),Is.True,"Only recorded movement, card choice and aim commands are accepted, including after the requested range.");
            var expectedTick=0;
            for(var index=0;index<replay.Commands.Count;index++)
            {
                var recorded=replay.Commands[index];
                Assert.That(recorded.Sequence,Is.EqualTo((long)index));Assert.That(recorded.Tick,Is.EqualTo(expectedTick));
                if(recorded.Kind==ReplayCommandKind.Advance)expectedTick++;
            }
            Assert.That(expectedTick,Is.EqualTo(replay.End.Tick));Assert.That(replay.End.AppliedCommands,Is.EqualTo((long)replay.Commands.Count));
            Assert.That(end,Is.LessThanOrEqualTo(replay.End.Tick));
            ArtCatalog.ProfileName=CanonicalContent.ProfileName;var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,replay.Header.Options);
            var checkpoints=replay.Checkpoints.ToDictionary(c=>c.AppliedCommands);var checkedCheckpoints=0;
            void VerifyCheckpoint()
            {
                if(!checkpoints.TryGetValue(session.NextSequence,out var cp))return;
                Assert.That(session.View.CaptureFrame().Tick,Is.EqualTo(cp.Tick));
                Assert.That(session.ComputeStateHash(),Is.EqualTo(cp.StateHash),"Recorded checkpoint "+cp.AppliedCommands);checkedCheckpoints++;
            }
            VerifyCheckpoint();
            var owner=new GameObject("Recorded tick sequence QA");var cameraOwner=new GameObject("Recorded tick camera");
            var camera=cameraOwner.AddComponent<Camera>();var texture=new RenderTexture(720,1560,24);camera.targetTexture=texture;
            var image=new Texture2D(720,1560,TextureFormat.RGB24,false);var written=0;var warmStart=System.Math.Max(1,start-120);
            try
            {
                var initial=session.View.CaptureFrame();var config=CanonicalContent.Presentation.Camera;var world=owner.AddComponent<WorldRenderer>();
                world.Initialize(camera,new WorldCameraSettings(config.WorldUnitsPerUnityUnit,config.MinHalfHeight,config.MaxHalfHeight,config.EstatePadding,config.FollowMilliseconds,config.ZoomMilliseconds),catalog.Tuning.DefaultEstate,initial.MapWidth,initial.MapHeight);
                using(var ledger=new StreamWriter(Path.Combine(output,"frames-local.tsv")))
                {
                    ledger.WriteLine("frame\ttick\tappliedCommands\tstateHash");
                    for(var i=0;i<replay.Commands.Count;i++)
                    {
                        var before=session.View.CaptureFrame().Tick;var command=replay.Commands[i];
                        session.Apply(command);VerifyCheckpoint();var frame=session.View.CaptureFrame();
                        Assert.That(frame.Tick,Is.EqualTo(before+(command.Kind==ReplayCommandKind.Advance?1:0)),"Each movement command must advance exactly one real tick.");
                        var wave=frame.Tick>=warmStart&&frame.Tick<=end?((IWaveRunView)session.View).CaptureWaveRuntime():null;
                        if(frame.Tick>=warmStart&&frame.Tick<=end)world.AcceptWave(catalog,wave);
                        var sameTickChoice=i+1<replay.Commands.Count&&replay.Commands[i+1].Tick==frame.Tick&&replay.Commands[i+1].Kind!=ReplayCommandKind.Advance;
                        if(sameTickChoice)continue;
                        if(frame.Tick>=warmStart&&frame.Tick<=end)
                        {
                            world.Present(frame,frame,WavePresentation.Envelope(catalog,frame,session.View.CaptureCards(),wave),1,1f/30,new Rect(0,0,720,1560));
                            yield return null;
                            Assert.That(world.UnsupportedShapeCount,Is.Zero);Assert.That(world.DrawCalls,Is.GreaterThan(0));
                            if(frame.Tick>=start)
                            {
                                Assert.That(frame.Tick,Is.EqualTo(start+written),"Missing or repeated output tick.");
                                var previous=RenderTexture.active;
                                try{RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,720,1560),0,0);image.Apply();}
                                finally{RenderTexture.active=previous;}
                                File.WriteAllBytes(Path.Combine(output,$"frame-{written:D6}.png"),image.EncodeToPNG());
                                ledger.WriteLine($"{written}\t{frame.Tick}\t{session.NextSequence}\t{session.ComputeStateHash()}");written++;
                            }
                        }
                        else if(session.NextSequence%300==0)yield return null;
                    }
                }
                var summary=session.GetSummary();
                var terminalKind=summary.EndReason=="death"?ReplayEndKind.Death:summary.EndReason=="duration"?ReplayEndKind.Duration:ReplayEndKind.Quit;
                Assert.That(session.NextSequence,Is.EqualTo(replay.End.AppliedCommands));
                Assert.That(summary.Tick,Is.EqualTo(replay.End.Tick));Assert.That(summary.StateHash,Is.EqualTo(replay.End.StateHash));
                Assert.That(terminalKind,Is.EqualTo(replay.End.Kind));Assert.That(checkedCheckpoints,Is.EqualTo(checkpoints.Count));
                Assert.That(written,Is.EqualTo(end-start+1),"Replay did not provide the entire requested range.");
                Assert.That(Directory.GetFiles(output,"frame-*.png").Length,Is.EqualTo(written));
                string replayHash;using(var sha=SHA256.Create())replayHash=System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
                File.WriteAllText(Path.Combine(output,"sequence-local.txt"),$"classification=deterministic actual-input GPU replay; 30 Hz simulation ticks; card waits omitted; world only; 720x1560; no audio; not native/live autoplay or performance evidence\nreplay={Path.GetFullPath(replayPath)}\nreplaySha256={replayHash}\nexpectedRendererCommit={expectedRendererCommit}\ngeneratedBuildIdentityCommit={BuildIdentity.Commit}\ngeneratedSourceHash={BuildIdentity.SourceHash}\ngeneratedSourceDirty={BuildIdentity.SourceDirty}\nsourceVerification=external git and prepare verification required; generated metadata alone does not verify current source\ndataHash={CanonicalContent.DataHash}\nstartTick={start}\nendTick={end}\nwarmStartTick={warmStart}\nframes={written}\nverifiedCheckpoints={checkedCheckpoints}\nterminalVerification=PASS\nterminalTick={summary.Tick}\nterminalKind={terminalKind}\nterminalStateHash={summary.StateHash}\nsameTickCommands=applied in recorded order before that tick frame; no extra frames\nrawFramesAndMetadata=local only; selected encoded video is review evidence\n");
            }
            finally{Object.Destroy(image);Object.Destroy(owner);Object.Destroy(cameraOwner);texture.Release();Object.Destroy(texture);}
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
