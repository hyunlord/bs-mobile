using System;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        public bool HeroRigPreviewEnabled { get; set; }
        public Action<HeroGaitSample> HeroGaitSampled { get; set; }
        // Opt-in comparison only: zero preserves the approved existing B presentation.
        public int HeroMotionPreviewMode { get; set; }
        public bool HeroMotionEvolved { get; set; }
        public HeroLiveRig HeroMotionLiveMetrics => heroLiveRig;
        HeroLiveRig heroLiveRig;
        bool heroKnightEnabled;
        float motionDeathStarted = -1;
        Texture2D heroMotionUpper;
        HeroLiveClip motionSwingClip, motionEvolvedClip, motionHitClip;
        float motionAttackStarted = -100, motionHitStarted = -100;
        float motionObservedAttackUntil, motionObservedHitUntil;
        bool motionAttackEvolved;
        readonly HeroLevelPoseTimeline heroLevelPose = new HeroLevelPoseTimeline();
        public bool HeroLevelPosePending => heroLevelPose.Pending;
        public float RenderedHeroLevelPoseProgress { get; private set; } = -1;

        public void PrepareHeroKnight()
        {
            if (heroKnightEnabled && heroLiveRig != null) return;
            DisposeHeroMotion();
            HeroMotionPreviewMode = 0;
            var motion = HeroLiveRigData.Load("HeroKnight");
            motionSwingClip = motion.Clip("swing");
            motionEvolvedClip = motion.Clip("evolved-swing");
            motionHitClip = motion.Clip("hit");
            heroLiveRig = new HeroLiveRig(null, "HeroKnight");
            heroKnightEnabled = true;
        }

        public void PrepareHeroMotionPreview(int mode)
        {
            if (mode < 0 || mode > 2) throw new ArgumentOutOfRangeException(nameof(mode));
            if (heroKnightEnabled)
            {
                DisposeHeroMotion();
                heroKnightEnabled = false;
                motionSwingClip = motionEvolvedClip = motionHitClip = null;
            }
            HeroMotionPreviewMode = mode;
            if (mode != 0 && motionSwingClip == null)
            {
                var motion = HeroLiveRigData.Load();
                motionSwingClip = motion.Clip("swing");
                motionEvolvedClip = motion.Clip("evolved-swing");
                motionHitClip = motion.Clip("hit");
            }
            if (mode == 1)
            {
                heroMotionUpper = Resources.Load<Texture2D>("HeroMotion/baked-actions");
                if (heroMotionUpper == null) throw new InvalidOperationException("Baked hero comparison atlas is unavailable.");
            }
            if (mode == 2 && heroLiveRig == null) heroLiveRig = new HeroLiveRig(null);
            if (mode != 2) heroLiveRig?.Hide();
        }
        public void NotifyHeroMotionAttack(bool evolved)
        {
            motionAttackStarted = visualTime;
            motionAttackEvolved = evolved;
            motionObservedAttackUntil = heroAttackUntil;
        }
        public void DisposeHeroMotion()
        {
            heroLiveRig?.Dispose(); heroLiveRig = null;
        }
        ArtVisual rigIdle, rigWalk, rigAttack, rigHit;
        readonly HeroGaitState heroGait=new HeroGaitState();
        Shader heroRigShader;

        ArtVisual HeroVisual(string heroId,string state,bool walking,float attackProgress,float seconds,out float animationTime)
        {
            animationTime=visualTime;
            if(heroConceptVisual!=null)return heroConceptVisual;
            if(!heroKnightEnabled && !HeroRigPreviewEnabled && HeroMotionPreviewMode == 0){heroGait.Reset();heroLiveRig?.Hide();return Resolve("hero",heroId,state);}
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
            ActorFacingPose facing,Color color,float flash,bool discontinuity,float seconds)
        {
            RenderedHeroLevelPoseProgress = -1;
            if(heroConceptVisual!=null){heroLiveRig?.Hide();return false;}
            if(!heroKnightEnabled && !HeroRigPreviewEnabled && HeroMotionPreviewMode == 0){heroLiveRig?.Hide();return false;}
            if(heroRigShader==null)
            {
                heroRigShader=Resources.Load<Shader>("HeroRig");
                if(heroRigShader==null||!heroRigShader.isSupported)throw new InvalidOperationException("Layered hero shader is unavailable.");
            }
            var fullSize=visual.WorldSize*(256f/252)*GameVisualTokens.HeroRigHeightScale;
            fullSize.x*=facing.SignedWidth;
            var comparing = HeroMotionPreviewMode != 0 || heroKnightEnabled;
            var sample=heroGait.Advance(position,fullSize,walking,discontinuity,!comparing&&(hit||dead));
            var frame=hit||dead?14:sample.FrameIndex;
            var upperFrame=HeroGaitState.UpperFrame(frame,attackProgress>=0,hit||dead);
            Texture2D actionTexture = null;
            if (comparing)
            {
                if (motionSwingClip == null) PrepareHeroMotionPreview(HeroMotionPreviewMode);
                if (wave == null && heroAttackUntil != motionObservedAttackUntil && attackProgress >= 0)
                {
                    motionObservedAttackUntil = heroAttackUntil;
                    motionAttackStarted = visualTime;
                    motionAttackEvolved = HeroMotionEvolved;
                }
                if (heroHitUntil != motionObservedHitUntil && hit)
                {
                    motionObservedHitUntil = heroHitUntil;
                    motionHitStarted = visualTime;
                }
                var attackClip = motionAttackEvolved ? motionEvolvedClip : motionSwingClip;
                var attackTime = (visualTime-motionAttackStarted)/attackClip.durationSeconds;
                var hitTime = (visualTime-motionHitStarted)/motionHitClip.durationSeconds;
                if (attackTime > 1 || attackTime < 0) attackTime = -1;
                if (hitTime > 1 || hitTime < 0) hitTime = -1;
                frame = sample.FrameIndex;
                if (HeroMotionPreviewMode == 2 || heroKnightEnabled)
                {
                    if (heroLiveRig == null) heroLiveRig = new HeroLiveRig(null, heroKnightEnabled ? "HeroKnight" : "HeroMotion");
                    if (!dead) motionDeathStarted = -1;
                    else if (motionDeathStarted < 0) motionDeathStarted = visualTime;
                    var deathTime = heroKnightEnabled && dead ? Mathf.Clamp01((visualTime - motionDeathStarted) / .55f) : -1;
                    if (dead) heroLevelPose.Reset();
                    var levelTime = heroKnightEnabled ? heroLevelPose.Sample(seconds, PresentationPaused, dead || hitTime >= 0 || attackTime >= 0) : -1;
                    RenderedHeroLevelPoseProgress = levelTime;
                    heroLiveRig.Present(position,fullSize,sample,walking,attackTime,motionAttackEvolved,hitTime,color,deathTime,levelTime);
                    HeroGaitSampled?.Invoke(sample);
                    return true;
                }
                heroLiveRig?.Hide();
                upperFrame = frame;
                if (hitTime >= 0 || attackTime >= 0)
                {
                    if (heroMotionUpper == null) heroMotionUpper = Resources.Load<Texture2D>("HeroMotion/baked-actions");
                    if (heroMotionUpper == null) throw new InvalidOperationException("Baked hero comparison atlas is unavailable.");
                    actionTexture = heroMotionUpper;
                    var clip = hitTime >= 0 ? motionHitClip : attackClip;
                    var progress = hitTime >= 0 ? hitTime : attackTime;
                    upperFrame = clip.firstFrame + Mathf.RoundToInt(progress * (clip.frameCount - 1));
                }
            }
            else heroLiveRig?.Hide();
            var key=(GameVisualTokens.LordLayer,visual.Texture);
            if(!batches.TryGetValue(key,out var batch))
            {
                batch=new SpriteBatch(visual.Texture,heroRigShader,renderCamera,GameVisualTokens.LordLayer);
                batches.Add(key,batch);
            }
            batch.SetHeroRigPose(HeroGaitState.FrameRect(frame),HeroGaitState.FrameRect(upperFrame),sample.FarCorrection,sample.NearCorrection,HeroGaitState.Warp,actionTexture);
            batch.Add(position,fullSize,HeroGaitState.Pivot,new Rect(0,0,1,1),0,color,flash);
            HeroGaitSampled?.Invoke(sample);
            if(sample.NearPlanted)RenderedEntitySample?.Invoke("hero-foot-near",0,HeroGaitState.WorldSole(position,fullSize,sample.FrameIndex,true,sample.NearCorrection));
            if(sample.FarPlanted)RenderedEntitySample?.Invoke("hero-foot-far",0,HeroGaitState.WorldSole(position,fullSize,sample.FrameIndex,false,sample.FarCorrection));
            return true;
        }
    }
}
