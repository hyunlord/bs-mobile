using UnityEngine;

namespace Game.View
{
    public enum HeroGaitReleaseReason { None, Initial, Stopped, Action, Discontinuity, DirectionChanged, ReachLimit }

    public readonly struct HeroGaitSample
    {
        public readonly int FrameIndex;
        public readonly float Phase;
        public readonly Vector2 FarCorrection, NearCorrection, FarContact, NearContact;
        public readonly bool FarPlanted, NearPlanted;
        public readonly HeroGaitReleaseReason ReleaseReason;
        public HeroGaitSample(int frame, float phase, Vector2 far, Vector2 near, Vector2 farContact, Vector2 nearContact,
            bool farPlanted, bool nearPlanted, HeroGaitReleaseReason reason)
        {
            FrameIndex=frame; Phase=phase; FarCorrection=far; NearCorrection=near;
            FarContact=farContact; NearContact=nearContact; FarPlanted=farPlanted; NearPlanted=nearPlanted; ReleaseReason=reason;
        }
    }

    // Presentation-only distance clock. No Core state, wall clock or frame allocations.
    public sealed class HeroGaitState
    {
        public const int WalkFrames=12, IdleFrame=12;
        public const float CycleTravelPixels=256, FramePixels=512;
        public const float VerticalCycleTravelPixels=128;
        public const float CanvasMinimumY=-.125f;
        public static readonly Vector2 Pivot=new Vector2(300f/512,92f/512);
        public static readonly Vector4 Warp=new Vector4(1-276f/512,1-416f/512,0,0);
        // Actual opaque sole coordinates from the authored atlas, converted to bottom-left local uv.
        static readonly Vector2[] FarSoles={ new Vector2(235f/512,1-419f/512), new Vector2(258f/512,1-407f/512), new Vector2(280f/512,1-397f/512), new Vector2(300f/512,1-395f/512), new Vector2(321f/512,1-397f/512), new Vector2(343f/512,1-407f/512), new Vector2(364f/512,1-421f/512), new Vector2(343f/512,1-421f/512), new Vector2(321f/512,1-421f/512), new Vector2(300f/512,1-421f/512), new Vector2(278f/512,1-421f/512), new Vector2(258f/512,1-421f/512) };
        static readonly Vector2[] NearSoles={ new Vector2(366f/512,1-421f/512), new Vector2(342f/512,1-421f/512), new Vector2(320f/512,1-421f/512), new Vector2(299f/512,1-421f/512), new Vector2(278f/512,1-421f/512), new Vector2(258f/512,1-421f/512), new Vector2(236f/512,1-421f/512), new Vector2(258f/512,1-407f/512), new Vector2(280f/512,1-397f/512), new Vector2(300f/512,1-393f/512), new Vector2(321f/512,1-397f/512), new Vector2(341f/512,1-407f/512) };
        bool initialized, farPlanted, nearPlanted;
        float phase;
        Vector2 previousPosition, previousDirection, previousSize, farContact, nearContact;

        public void Reset() { initialized=false; farPlanted=nearPlanted=false; phase=0; previousDirection=Vector2.zero; }
        public static Vector2 Sole(int frame,bool near)=>near?NearSoles[frame]:FarSoles[frame];
        public static Vector4 FrameRect(int frame)=>new Vector4((frame%4)*.25f,1-(frame/4+1)*.25f,.25f,.25f);
        public static float ProjectedTravel(Vector2 movement)
        {
            var vertical=movement.y*(CycleTravelPixels/VerticalCycleTravelPixels);
            return Mathf.Sqrt(movement.x*movement.x+vertical*vertical);
        }
        public static int UpperFrame(int gaitFrame,bool attacking,bool hit)=>hit?14:attacking?13:gaitFrame;
        public static Vector2 WorldSole(Vector2 position,Vector2 signedSize,int frame,bool near,Vector2 correction)
            => position+Vector2.Scale(Sole(frame,near)-Pivot+correction,signedSize);

        public HeroGaitSample Advance(Vector2 position,Vector2 signedSize,bool walking,bool discontinuity=false,bool releaseForHit=false)
        {
            var movement=initialized?position-previousPosition:Vector2.zero;
            var reason=initialized?HeroGaitReleaseReason.None:HeroGaitReleaseReason.Initial;
            var stride=Mathf.Abs(signedSize.x)*CycleTravelPixels/FramePixels;
            var moving=walking&&movement.sqrMagnitude>1e-12f;
            if(!walking)reason=HeroGaitReleaseReason.Stopped;
            else if(releaseForHit)reason=HeroGaitReleaseReason.Action;
            else if(discontinuity||movement.magnitude>stride*.25f)reason=HeroGaitReleaseReason.Discontinuity;
            else if(initialized&&(signedSize!=previousSize||(moving&&previousDirection.sqrMagnitude>0&&Vector2.Dot(movement.normalized,previousDirection)<.985f)))
                reason=HeroGaitReleaseReason.DirectionChanged;
            if(reason!=HeroGaitReleaseReason.None){farPlanted=nearPlanted=false;phase=0;}
            else if(moving)phase=Mathf.Repeat(phase+ProjectedTravel(movement)/stride,1);
            initialized=true;previousPosition=position;previousSize=signedSize;
            if(moving)previousDirection=movement.normalized;
            if(!walking||releaseForHit||reason==HeroGaitReleaseReason.Discontinuity)
            {
                previousDirection=Vector2.zero;
                return new HeroGaitSample(IdleFrame,phase,Vector2.zero,Vector2.zero,farContact,nearContact,false,false,reason);
            }
            var frame=Mathf.Min(WalkFrames-1,Mathf.FloorToInt(phase*WalkFrames));
            var nearStance=frame<6;
            var farStance=!nearStance;
            if(nearStance&&!nearPlanted)nearContact=WorldSole(position,signedSize,frame,true,Vector2.zero);
            if(farStance&&!farPlanted)farContact=WorldSole(position,signedSize,frame,false,Vector2.zero);
            nearPlanted=nearStance;farPlanted=farStance;
            var far=farPlanted?Correction(farContact,position,signedSize,frame,false):Vector2.zero;
            var near=nearPlanted?Correction(nearContact,position,signedSize,frame,true):Vector2.zero;
            // Release instead of inverting or overextending a leg during large vertical/turning motion.
            if(ExceedsReach(far)||ExceedsReach(near))
            {
                farPlanted=nearPlanted=false;far=near=Vector2.zero;reason=HeroGaitReleaseReason.ReachLimit;
            }
            return new HeroGaitSample(frame,phase,far,near,farContact,nearContact,farPlanted,nearPlanted,reason);
        }
        static Vector2 Correction(Vector2 contact,Vector2 position,Vector2 size,int frame,bool near)
            => new Vector2((contact.x-position.x)/size.x,(contact.y-position.y)/size.y)-(Sole(frame,near)-Pivot);
        // A normal half-step is at most128/512=.25UV. The140px ramp keeps
        // its Jacobian positive (>=.0857) in every walking direction.
        static bool ExceedsReach(Vector2 delta)=>Mathf.Abs(delta.x)>.30f||Mathf.Abs(delta.y)>.26f;

        // Same closed-form inverse as HeroRig.shader; soles have weight 1, hip has weight 0.
        public static Vector2 InverseWarp(Vector2 point,Vector2 delta)
        {
            var h=Warp.x;var f=Warp.y;var range=h-f;
            if(point.y>=h)return point;
            if(point.y<=f+delta.y)return point-delta;
            var y=(point.y-h*delta.y/range)/(1-delta.y/range);
            return new Vector2(point.x-(h-y)/range*delta.x,y);
        }
    }
}
