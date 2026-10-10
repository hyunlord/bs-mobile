using UnityEngine;
using SowSiege.Core;

namespace Game.View
{
    public readonly struct ActorFacingPose
    {
        public readonly float MirrorBlend, LeanDegrees;
        public float SignedWidth => MirrorBlend < .5f ? 1 : -1;
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
        public ActorFacingPose Sample(float time)=>new ActorFacingPose(MirrorAt(time),0);
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
            return new ActorPose(Vector2.zero, Vector2.one, 0);
        }

        public static ActorPose SampleEnemy(WaveEnemyKind kind, float time, bool walking)
        {
            var offset = Vector2.zero;
            switch (kind)
            {
                case WaveEnemyKind.SeedThief:
                    if (walking) offset.x = Mathf.Sin(time * 47) * GameVisualTokens.MiteScuttle;
                    break;
                case WaveEnemyKind.RipeGrazer:
                    if (walking) offset.y = Mathf.Sin(time * 9) * GameVisualTokens.GrazerUndulation;
                    break;
                case WaveEnemyKind.Ranged:
                    offset.y = GameVisualTokens.WaspHoverHeight + Mathf.Sin(time * 5) * GameVisualTokens.WaspHoverDrift;
                    break;
                case WaveEnemyKind.Charger:
                    if (walking) offset.y = -GameVisualTokens.ChargerLowPosture;
                    break;
            }
            return new ActorPose(offset, Vector2.one, 0);
        }
    }
}
