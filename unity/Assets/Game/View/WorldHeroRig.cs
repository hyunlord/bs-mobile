using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        public bool HeroRigPreviewEnabled { get; set; }
        ArtVisual rigIdle, rigWalk, rigAttack, rigHit;
        float rigWalkTime;

        ArtVisual HeroVisual(string heroId,string state,bool walking,float attackProgress,float seconds,out float animationTime)
        {
            animationTime=visualTime;
            if(!HeroRigPreviewEnabled)return Resolve("hero",heroId,state);
            if(rigIdle==null)
            {
                rigIdle=art.ResolveRole("hero.preview-b.idle");
                rigWalk=art.ResolveRole("hero.preview-b.walk");
                rigAttack=art.ResolveRole("hero.preview-b.attack");
                rigHit=art.ResolveRole("hero.preview-b.hit");
            }
            if(walking)rigWalkTime+=Mathf.Max(0,seconds);else rigWalkTime=0;
            animationTime=rigWalkTime;
            if(state=="hit"||state=="death")return rigHit;
            if(attackProgress>=0)return rigAttack;
            return walking?rigWalk:rigIdle;
        }
    }
}
