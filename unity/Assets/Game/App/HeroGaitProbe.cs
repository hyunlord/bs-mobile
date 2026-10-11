using System;
using System.IO;
using Game.View;
using UnityEngine;

namespace Game.App
{
    // Opt-in capture evidence only; normal play creates neither this component nor its buffer.
    public sealed class HeroGaitProbe : MonoBehaviour
    {
        const int Capacity=20000;
        readonly Row[] rows=new Row[Capacity];
        struct Row
        {
            public int Frame,Tick,Pose,NearStance,FarStance;
            public double Wall,Dt;
            public float Phase,Speed;
            public HeroGaitReleaseReason Release;
            public Vector2 Lord,Camera,NearContact,FarContact,NearCorrection,FarCorrection,NearFoot,FarFoot;
            public bool NearPlanted,FarPlanted,NearSeen,FarSeen,Paused;
        }
        WorldRenderer world;
        RunCoordinator run;
        string folder;
        int count,dropped,nearStance,farStance;
        bool ended,previousNear,previousFar;
        Vector2 lord;

        public static bool Requested(string[] args)
        {
            var index=Array.IndexOf(args,"--hero-gait-probe");
            if(index<0)return false;
            if(Array.LastIndexOf(args,"--hero-gait-probe")!=index||Array.IndexOf(args,"--autoplay-capture")<0||Array.IndexOf(args,"--capture-output")<0)
                throw new ArgumentException("Hero gait probe requires one explicit flag, --autoplay-capture and --capture-output.");
            return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            var args=Environment.GetCommandLineArgs();
            if(!Requested(args))return;
            var output=AutoplayCapture.ParseOutput(args,Application.persistentDataPath);
            var owner=new GameObject("Capture hero gait probe");DontDestroyOnLoad(owner);
            owner.AddComponent<HeroGaitProbe>().folder=output;
        }
        void Update()
        {
            if(ended||world!=null)return;
            if(run==null)run=FindFirstObjectByType<RunCoordinator>();
            if(run==null||run.CurrentFrame==null)return;
            world=FindFirstObjectByType<WorldRenderer>();
            if(world==null)return;
            world.HeroGaitSampled+=ObserveGait;
            world.RenderedEntitySample+=ObserveEntity;
        }
        void ObserveGait(HeroGaitSample sample)
        {
            if(ended)return;
            if(count==Capacity){dropped++;return;}
            if(sample.NearPlanted&&(!previousNear||sample.ReleaseReason!=HeroGaitReleaseReason.None))nearStance++;
            if(sample.FarPlanted&&(!previousFar||sample.ReleaseReason!=HeroGaitReleaseReason.None))farStance++;
            previousNear=sample.NearPlanted;previousFar=sample.FarPlanted;
            var wall=Time.realtimeSinceStartupAsDouble;
            var dt=count==0?0:wall-rows[count-1].Wall;
            var speed=dt>0?Vector2.Distance(lord,rows[count-1].Lord)/(float)dt:0;
            rows[count++]=new Row {
                Frame=Time.frameCount,Tick=run.CurrentFrame.Tick,Pose=sample.FrameIndex,Wall=wall,Dt=dt,
                Phase=sample.Phase,Speed=speed,Lord=lord,Release=sample.ReleaseReason,
                NearStance=sample.NearPlanted?nearStance:0,FarStance=sample.FarPlanted?farStance:0,
                NearPlanted=sample.NearPlanted,FarPlanted=sample.FarPlanted,
                NearContact=sample.NearContact,FarContact=sample.FarContact,
                NearCorrection=sample.NearCorrection,FarCorrection=sample.FarCorrection,Paused=run.CaptureClockPaused
            };
        }
        void ObserveEntity(string kind,int id,Vector2 position)
        {
            if(kind=="lord"){lord=position;return;}
            if(count==0||rows[count-1].Frame!=Time.frameCount)return;
            ref var row=ref rows[count-1];
            if(kind=="hero-foot-near"){row.NearFoot=position;row.NearSeen=true;}
            else if(kind=="hero-foot-far"){row.FarFoot=position;row.FarSeen=true;}
        }
        void LateUpdate()
        {
            if(count==0||rows[count-1].Frame!=Time.frameCount)return;
            var camera=Camera.main;
            if(camera!=null)rows[count-1].Camera=camera.transform.position;
        }
        void OnApplicationQuit()=>Finish();
        void OnDestroy()=>Finish();
        void Finish()
        {
            if(ended)return;ended=true;
            if(world!=null){world.HeroGaitSampled-=ObserveGait;world.RenderedEntitySample-=ObserveEntity;}
            if(string.IsNullOrEmpty(folder))return;
            Directory.CreateDirectory(folder);
            var nearId=0;var farId=0;var nearOrigin=Vector2.zero;var farOrigin=Vector2.zero;
            double maximumSlip=0,maximumContactError=0;
            var missing=0;var reachReleases=0;
            var nearMax=new double[nearStance+1];var farMax=new double[farStance+1];
            var nearSamples=new int[nearStance+1];var farSamples=new int[farStance+1];
            using(var file=new StreamWriter(Path.Combine(folder,"hero-gait.csv")))
            {
                file.WriteLine("frame,tick,wallSeconds,dtSeconds,pose,phase,release,paused,speedWorldPerSecond,lordX,lordY,cameraX,cameraY,nearStance,nearPlanted,nearSeen,nearContactX,nearContactY,nearFootX,nearFootY,nearCorrectionX,nearCorrectionY,nearSlipWorld,farStance,farPlanted,farSeen,farContactX,farContactY,farFootX,farFootY,farCorrectionX,farCorrectionY,farSlipWorld");
                for(var i=0;i<count;i++)
                {
                    var row=rows[i];double nearSlip=0,farSlip=0;
                    if(row.Release==HeroGaitReleaseReason.ReachLimit)reachReleases++;
                    Accumulate(row.NearPlanted,row.NearSeen,row.NearStance,row.NearFoot,row.NearContact,ref nearId,ref nearOrigin,ref nearSlip,ref maximumContactError,ref missing);
                    Accumulate(row.FarPlanted,row.FarSeen,row.FarStance,row.FarFoot,row.FarContact,ref farId,ref farOrigin,ref farSlip,ref maximumContactError,ref missing);
                    maximumSlip=Math.Max(maximumSlip,Math.Max(nearSlip,farSlip));
                    if(row.NearPlanted&&row.NearSeen){nearMax[row.NearStance]=Math.Max(nearMax[row.NearStance],nearSlip);nearSamples[row.NearStance]++;}
                    if(row.FarPlanted&&row.FarSeen){farMax[row.FarStance]=Math.Max(farMax[row.FarStance],farSlip);farSamples[row.FarStance]++;}
                    file.WriteLine(FormattableString.Invariant($"{row.Frame},{row.Tick},{row.Wall:F9},{row.Dt:F9},{row.Pose},{row.Phase:R},{row.Release},{row.Paused},{row.Speed:R},{row.Lord.x:R},{row.Lord.y:R},{row.Camera.x:R},{row.Camera.y:R},{row.NearStance},{row.NearPlanted},{row.NearSeen},{row.NearContact.x:R},{row.NearContact.y:R},{row.NearFoot.x:R},{row.NearFoot.y:R},{row.NearCorrection.x:R},{row.NearCorrection.y:R},{nearSlip:R},{row.FarStance},{row.FarPlanted},{row.FarSeen},{row.FarContact.x:R},{row.FarContact.y:R},{row.FarFoot.x:R},{row.FarFoot.y:R},{row.FarCorrection.x:R},{row.FarCorrection.y:R},{farSlip:R}"));
                }
            }
            using(var stances=new StreamWriter(Path.Combine(folder,"hero-gait-stances.csv")))
            {
                stances.WriteLine("foot,stance,samples,maximumSlipWorld");
                for(var i=1;i<nearMax.Length;i++)stances.WriteLine(FormattableString.Invariant($"near,{i},{nearSamples[i]},{nearMax[i]:R}"));
                for(var i=1;i<farMax.Length;i++)stances.WriteLine(FormattableString.Invariant($"far,{i},{farSamples[i]},{farMax[i]:R}"));
            }
            File.WriteAllText(Path.Combine(folder,"hero-gait-summary.txt"),FormattableString.Invariant($"scope=native presentation submitted coordinates; not independent GPU pixel tracking\nframes={count}\ndropped={dropped}\nmissingPlantedFootSamples={missing}\nnearStances={nearStance}\nfarStances={farStance}\nreachLimitReleases={reachReleases}\nmaximumWithinStanceSlipWorld={maximumSlip:R}\nmaximumContactErrorWorld={maximumContactError:R}\nstanceBoundary=foot planted transition or explicit release; different contacts never compared\nclock=Time.realtimeSinceStartupAsDouble at actual presentation callback\n"));
        }
        static void Accumulate(bool planted,bool seen,int stance,Vector2 foot,Vector2 contact,ref int lastStance,ref Vector2 origin,ref double slip,ref double contactError,ref int missing)
        {
            if(!planted)return;
            if(!seen){missing++;return;}
            if(lastStance!=stance){lastStance=stance;origin=foot;}
            slip=Vector2.Distance(origin,foot);
            contactError=Math.Max(contactError,Vector2.Distance(contact,foot));
        }
    }
}
