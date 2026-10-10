using UnityEngine;

namespace Game.View
{
    public readonly struct ActorFacingPose
    {
        public readonly float MirrorBlend, LeanDegrees;
        public ActorFacingPose(float mirrorBlend,float leanDegrees) { MirrorBlend=mirrorBlend;LeanDegrees=leanDegrees; }
    }

    public struct ActorFacingMotion
    {
        float fromAngle,targetAngle,angleStarted,fromMirror,targetMirror,mirrorStarted;
        public ActorFacingMotion(Vector2 direction,float time)
        {
            fromAngle=targetAngle=direction.sqrMagnitude>.000001f?Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg:0;
            fromMirror=targetMirror=direction.x<0?1:0;angleStarted=mirrorStarted=time;
        }
        public float AngleAt(float time)=>Mathf.LerpAngle(fromAngle,targetAngle,Mathf.Clamp01((time-angleStarted)/GameVisualTokens.ActorTurnSeconds));
        float MirrorAt(float time)=>Mathf.Lerp(fromMirror,targetMirror,Mathf.SmoothStep(0,1,Mathf.Clamp01((time-mirrorStarted)/GameVisualTokens.ActorTurnSeconds)));
        public void SetDirection(Vector2 direction,float time)
        {
            if(direction.sqrMagnitude<.000001f)return;
            var angle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
            if(Mathf.Abs(Mathf.DeltaAngle(targetAngle,angle))>.01f){fromAngle=AngleAt(time);targetAngle=angle;angleStarted=time;}
            var mirror=direction.x>direction.magnitude*.15f?0:direction.x<-direction.magnitude*.15f?1:targetMirror;
            if(mirror!=targetMirror){fromMirror=MirrorAt(time);targetMirror=mirror;mirrorStarted=time;}
        }
        public ActorFacingPose Sample(float time)=>new ActorFacingPose(MirrorAt(time),Mathf.Sin(AngleAt(time)*Mathf.Deg2Rad)*GameVisualTokens.ActorHeadingLean);
    }

    public readonly struct ActorPose
    {
        public readonly Vector2 Offset, Scale;
        public readonly float Degrees;
        public ActorPose(Vector2 offset, Vector2 scale, float degrees) { Offset = offset; Scale = scale; Degrees = degrees; }
    }

    public static class ActorMotion
    {
        public static ActorPose Sample(float time, bool walking, float attackProgress, float hitProgress)
        {
            var phase = time * Mathf.PI * 2 / GameVisualTokens.WalkSeconds;
            var stride = walking ? Mathf.Sin(phase) : 0;
            var lift = walking ? (1 - Mathf.Cos(phase * 2)) * GameVisualTokens.WalkBob * .5f : Mathf.Sin(time * 2) * .004f;
            var attack = attackProgress >= 0 && attackProgress <= 1 ? Mathf.Sin(attackProgress * Mathf.PI) : 0;
            var hit = hitProgress >= 0 && hitProgress <= 1 ? Mathf.Sin(hitProgress * Mathf.PI) : 0;
            var squash = Mathf.Cos(phase * 2) * (walking ? .025f : .004f);
            return new ActorPose(new Vector2(-attack * .025f, lift), new Vector2(1 + squash + hit * .04f, 1 - squash - hit * .04f), stride * GameVisualTokens.WalkLeanDegrees - attack * 7 + hit * 4);
        }
    }
}
