using System;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        public bool HeroRigPreviewEnabled { get; set; }
        public Action<HeroGaitSample> HeroGaitSampled { get; set; }
        ArtVisual rigIdle, rigWalk, rigAttack, rigHit;
        readonly HeroGaitState heroGait=new HeroGaitState();
        Shader heroRigShader;

        ArtVisual HeroVisual(string heroId,string state,bool walking,float attackProgress,float seconds,out float animationTime)
        {
            animationTime=visualTime;
            if(!HeroRigPreviewEnabled){heroGait.Reset();return Resolve("hero",heroId,state);}
            if(rigIdle==null)
            {
                rigIdle=art.ResolveRole("hero.preview-b.idle");
                rigWalk=art.ResolveRole("hero.preview-b.walk");
                rigAttack=art.ResolveRole("hero.preview-b.attack");
                rigHit=art.ResolveRole("hero.preview-b.hit");
            }
            animationTime=0;
            if(state=="hit"||state=="death")return rigHit;
            if(attackProgress>=0)return rigAttack;
            return walking?rigWalk:rigIdle;
        }

        // The shared batches dictionary supplies BeginFrame, Flush, accounting and Dispose.
        bool DrawHeroRig(ArtVisual visual,Vector2 position,bool walking,float attackProgress,bool hit,bool dead,
            ActorFacingPose facing,Color color,float flash,bool discontinuity)
        {
            if(!HeroRigPreviewEnabled)return false;
            if(heroRigShader==null)
            {
                heroRigShader=Resources.Load<Shader>("HeroRig");
                if(heroRigShader==null||!heroRigShader.isSupported)throw new InvalidOperationException("Layered hero shader is unavailable.");
            }
            var fullSize=visual.WorldSize*(256f/252)*GameVisualTokens.HeroRigHeightScale;
            fullSize.x*=facing.SignedWidth;
            var sample=heroGait.Advance(position,fullSize,walking,discontinuity,hit||dead);
            var frame=hit||dead?14:sample.FrameIndex;
            var upperFrame=HeroGaitState.UpperFrame(frame,attackProgress>=0,hit||dead);
            var key=(GameVisualTokens.LordLayer,visual.Texture);
            if(!batches.TryGetValue(key,out var batch))
            {
                batch=new SpriteBatch(visual.Texture,heroRigShader,renderCamera,GameVisualTokens.LordLayer);
                batches.Add(key,batch);
            }
            batch.SetHeroRigPose(HeroGaitState.FrameRect(frame),HeroGaitState.FrameRect(upperFrame),sample.FarCorrection,sample.NearCorrection,HeroGaitState.Warp);
            batch.Add(position,fullSize,HeroGaitState.Pivot,new Rect(0,0,1,1),0,color,flash);
            HeroGaitSampled?.Invoke(sample);
            if(sample.NearPlanted)RenderedEntitySample?.Invoke("hero-foot-near",0,HeroGaitState.WorldSole(position,fullSize,sample.FrameIndex,true,sample.NearCorrection));
            if(sample.FarPlanted)RenderedEntitySample?.Invoke("hero-foot-far",0,HeroGaitState.WorldSole(position,fullSize,sample.FrameIndex,false,sample.FarCorrection));
            return true;
        }
    }
}
