using System;
using System.Linq;
using NUnit.Framework;
using Game.App;
using Game.App.Generated;
using Game.Editor;
using SowSiege.Core;
namespace Game.Tests
{
    public sealed class WavePresentationTests
    {
        [Test]
        public void GrowthCopyDescribesEveryChangedDirectionWithoutWorldUnits()
        {
            var before=new WaveGearLevel(10,1205,30,600,1,60,100);
            var after=new WaveGearLevel(12,1240,25,700,2,75,120);
            var text=UiRunPanels.WaveGrowthDescription(before,after);
            foreach(var phrase in new[]{"더 강하게 타격","더 넓게 공격","한 번에 더 많이 타격","더 자주 공격","공격이 더 빠르게 이동","더 세게 밀치기","공격이 더 오래 유지"})
                Assert.That(text,Does.Contain(phrase));
            Assert.That(text.Any(char.IsDigit),Is.False,"Internal world-unit values never become card copy.");
            Assert.That(UiRunPanels.WaveGrowthDescription(before,before),Is.EqualTo("성장표 상한"));
            var reduced=UiRunPanels.WaveGrowthDescription(after,before);
            Assert.That(reduced,Does.Contain("더 좁게 공격"));
            Assert.That(reduced,Does.Contain("공격 사이의 대기 증가"));
        }
        [Test]
        public void EnemyArtShrinksWithoutChangingAspectOrBossSize()
        {
            var authored=new UnityEngine.Vector2(1.2f,1);
            var scaled=Game.View.WorldRenderer.WaveEnemySize(authored,WaveEnemyKind.Shield);
            Assert.That(scaled.x,Is.LessThanOrEqualTo(Game.View.GameVisualTokens.WaveEnemyMaxSize));
            Assert.That(scaled.x/scaled.y,Is.EqualTo(authored.x/authored.y).Within(.0001f));
            Assert.That(Game.View.WorldRenderer.WaveEnemySize(authored,WaveEnemyKind.FloodBoss),Is.EqualTo(authored));
            var small=new UnityEngine.Vector2(.3f,.4f);
            Assert.That(Game.View.WorldRenderer.WaveEnemySize(small,WaveEnemyKind.Pursuer),Is.EqualTo(small));
        }
        [Test]
        public void BossRecoveryCueRequiresActualChargeTransitionOncePerRun()
        {
            var cue=new Game.View.WaveBossPhaseCue();
            Assert.That(cue.Accept(7,"recovery"),Is.False);
            Assert.That(cue.Accept(7,"charge"),Is.False);
            Assert.That(cue.Accept(8,"recovery"),Is.False,"Different boss identity cannot consume a transition.");
            Assert.That(cue.Accept(7,"recovery"),Is.True);
            Assert.That(cue.Accept(7,"recovery"),Is.False,"Paused and repeated snapshots must not replay the cue.");
            cue.Accept(7,"charge");cue.Reset();
            Assert.That(cue.Accept(7,"recovery"),Is.False,"New run must not inherit the previous charge.");
        }
        [TestCase("tell-water",0,"water-windup")]
        [TestCase("water",0,"water-surge")]
        [TestCase("tell-charge",2,"charge-windup")]
        [TestCase("charge",0,"charge")]
        [TestCase("recovery",0,"stuck")]
        [TestCase("recovery",1,"recover")]
        [TestCase("recovery",2,"recover")]
        public void BossPhasePoseSurvivesHitFlashAndDistinguishesRecovery(string phase,int bossPhase,string expected)
        {
            var state=new WaveEnemyView(1,phase,default,default,100,false,false,bossPhase);
            Assert.That(Game.View.WorldRenderer.WaveBossState(state,"hit"),Is.EqualTo(expected));
            Assert.That(Game.View.WorldRenderer.WaveBossState(state,"idle"),Is.EqualTo(expected));
            Assert.That(Game.View.WorldRenderer.WaveBossState(state,"death"),Is.EqualTo("death"));
        }
        [Test]
        public void MissingWaveFrameCannotBePresentedAsLegacyState()
        {
            Assert.Throws<InvalidOperationException>(() => WavePresentation.Envelope(null, null, null, null));
        }
        [Test]
        public void CaptureChoiceUsesOnlyOffersAndExpandsPreferredBuildBeforeUpgrades()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Wave catalog required.");
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var frame=session.View.CaptureFrame() with {Level=2,Equipment=new[]{new EquipmentView("core:iron_blade",1)}};
            CardOfferView Offer(params string[] ids)=>new CardOfferView(ids,null,0,0,0);
            WaveCaptureInput.ConfigurePriority("core:iron_blade,core:ward_orbit,core:seed_bag,core:sowing_sworddance");
            try
            {
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:iron_blade","core:ward_orbit"),frame,catalog),Is.EqualTo("core:ward_orbit"));
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:ward_orbit","core:sowing_sworddance"),frame,catalog),Is.EqualTo("core:sowing_sworddance"));
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:iron_blade"),frame,catalog),Is.EqualTo("core:iron_blade"),"Never invent an unavailable preferred card.");
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:ward_orbit","core:seed_bag"),frame,catalog,"meta:grain"),Is.EqualTo("core:seed_bag"));
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:ward_orbit"),frame,catalog,"meta:grain"),Is.EqualTo("core:ward_orbit"),"Target preference must still be offered.");
            }
            finally{WaveCaptureInput.ConfigurePriority(null);}
        }
        [Test]
        public void CaptureEvolutionPhaseSelectsOnlyActualOffersAndKeepsDefaultPolicy()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Wave catalog required.");
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var hash=session.ComputeStateHash();
            var frame=session.View.CaptureFrame() with {Tick=17999,Level=3,Equipment=new[]{new EquipmentView("core:iron_blade",1)}};
            CardOfferView Offer(params string[] ids)=>new CardOfferView(ids,null,0,0,0);
            WaveCaptureInput.ConfigurePriority("core:sowing_sworddance,core:ward_orbit");
            try
            {
                var mixed=Offer("core:sowing_sworddance","core:ward_orbit");
                Assert.That(WaveCaptureInput.ChooseCard(mixed,frame,catalog,evolutionAfterTick:18000),Is.EqualTo("core:ward_orbit"));
                Assert.That(WaveCaptureInput.ChooseCard(mixed,frame with {Tick=18000},catalog,evolutionAfterTick:18000),Is.EqualTo("core:sowing_sworddance"));
                Assert.That(WaveCaptureInput.ChooseCard(mixed,frame,catalog),Is.EqualTo("core:sowing_sworddance"),"Omitted phase preserves the native and historical policy.");
                Assert.That(WaveCaptureInput.ChooseCard(mixed,frame,catalog,evolutionAfterTick:0),Is.EqualTo(WaveCaptureInput.ChooseCard(mixed,frame,catalog)));
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:sheltered_sowing","core:iron_blade"),frame,catalog,evolutionAfterTick:18000),Is.EqualTo("core:iron_blade"),"Fallback must also prefer actual non-evolution offers.");
                var evolutions=Offer("core:warded_masonry","core:sowing_sworddance");
                Assert.That(WaveCaptureInput.ChooseCard(evolutions,frame,catalog,evolutionAfterTick:18000),Is.EqualTo(WaveCaptureInput.ChooseCard(evolutions,frame,catalog)),"An all-evolution offer cannot be skipped or replaced.");
                Assert.That(WaveCaptureInput.ChooseCard(Offer("core:sheltered_sowing"),frame,catalog,evolutionAfterTick:18000),Is.EqualTo("core:sheltered_sowing"),"An unpreferred evolution is still the only actual choice.");
                Assert.Throws<ArgumentOutOfRangeException>(()=>WaveCaptureInput.ChooseCard(mixed,frame,catalog,evolutionAfterTick:-1));
                Assert.That(session.ComputeStateHash(),Is.EqualTo(hash),"Card preferences never mutate gameplay state.");
            }
            finally{WaveCaptureInput.ConfigurePriority(null);}
        }
        [Test]
        public void EvasiveCaptureChangesOnlyBoundedMovementTargetsAndIsOptIn()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Wave catalog required.");
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var hash=session.ComputeStateHash();var original=session.View.CaptureFrame();
            var frame=original with {Lord=original.Lord with {Position=new WorldPoint(2000,2000)},Enemies=new[]{new EnemyView(7,catalog.Enemies.Keys.First(),new WorldPoint(2300,2000),100,100)}};
            var wave=((IWaveRunView)session.View).CaptureWaveRuntime() with {Timber=0};
            var target=new WorldPoint(2600,2000);
            try
            {
                WaveCaptureInput.ConfigureMovement(null);
                Assert.That(WaveCaptureInput.Target(frame,wave,target,catalog),Is.EqualTo(target));
                WaveCaptureInput.ConfigureMovement("evasive");
                var result=WaveCaptureInput.Target(frame,wave,target,catalog);
                Assert.That(result,Is.Not.EqualTo(target));
                Assert.That(result.X,Is.InRange(0,frame.MapWidth));Assert.That(result.Y,Is.InRange(0,frame.MapHeight));
                Assert.That((long)(result.X-2000)*(result.X-2000)+(long)(result.Y-2000)*(result.Y-2000),Is.LessThanOrEqualTo(450L*450));
                Assert.That(WaveCaptureInput.Target(frame,wave,target,catalog),Is.EqualTo(result),"Same observations select the same ordinary target.");
                Assert.That(WaveCaptureInput.Target(frame with {Enemies=Array.Empty<EnemyView>()},wave,target,catalog),Is.EqualTo(target),"Safe growth direction is retained.");
                var telegraph=wave with {Enemies=new[]{new WaveEnemyView(7,"tell-charge",new WorldPoint(0,2000),new WorldPoint(4000,2000),100,false,false,0)}};
                Assert.That(WaveCaptureInput.Target(frame,telegraph,target,catalog).Y,Is.Not.EqualTo(2000),"Observed charge lane should favor a lateral ordinary input.");
                Assert.That(session.ComputeStateHash(),Is.EqualTo(hash),"Movement selector must not mutate simulation state.");
                Assert.Throws<ArgumentException>(()=>WaveCaptureInput.ConfigureMovement("invulnerable"));
            }
            finally{WaveCaptureInput.ConfigureMovement(null);}
        }
        [Test]
        public void GoldenMinuteCompositionUsesOnlyNormalLateMovementAroundActualBuildings()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Wave catalog required.");
            var catalog=CanonicalContent.CreateCatalog();
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var hash=session.ComputeStateHash();var original=session.View.CaptureFrame();
            var frame=original with {Tick=52*original.TickRate,Enemies=Array.Empty<EnemyView>()};
            var building=new WaveWorkView(91,"core:carpenter_hammer","building",new WorldPoint(4000,5000),100,100,true,100,0,false,false,false,false,false);
            var wave=((IWaveRunView)session.View).CaptureWaveRuntime() with {Timber=0,Rewards=Array.Empty<WaveRewardView>(),Work=new[]{building,building with {Id=92,Position=new WorldPoint(6000,5000)},building with {Id=93,Position=new WorldPoint(0,0),Complete=false},building with {Id=94,Position=new WorldPoint(0,0),Health=0}}};
            var fallback=new WorldPoint(3000,2000);
            try
            {
                WaveCaptureInput.ConfigureMovement(null);
                var ordinary=WaveCaptureInput.Target(frame,wave,fallback,catalog);
                Assert.That(WaveCaptureInput.Target(frame,wave,fallback,catalog,goldenMinute:false),Is.EqualTo(ordinary));
                Assert.That(WaveCaptureInput.Target(frame,wave,fallback,catalog,goldenMinute:true),Is.EqualTo(new WorldPoint(6200,5900)));
                var earlier=frame with {Tick=52*frame.TickRate-1};
                Assert.That(WaveCaptureInput.Target(earlier,wave,fallback,catalog,goldenMinute:true),Is.EqualTo(WaveCaptureInput.Target(earlier,wave,fallback,catalog)));
                var later=frame with {Tick=60*frame.TickRate};
                Assert.That(WaveCaptureInput.Target(later,wave,fallback,catalog,goldenMinute:true),Is.EqualTo(WaveCaptureInput.Target(later,wave,fallback,catalog)));
                var empty=wave with {Work=Array.Empty<WaveWorkView>()};
                Assert.That(WaveCaptureInput.Target(frame,empty,fallback,catalog,goldenMinute:true),Is.EqualTo(fallback));
                Assert.That(session.ComputeStateHash(),Is.EqualTo(hash),"Selecting a normal input target cannot mutate Core.");
            }
            finally{WaveCaptureInput.ConfigureMovement(null);}
        }
        [Test]
        public void WaveProfileFeedsSharedWidgetsFromItsOwnState()
        {
            if(CanonicalContent.ProfileName!="wave-1a")Assert.Ignore("Run this profile integration with UNITY_CONTENT_PROFILE=wave-1a.");
            FoundationBuild.VerifyProfile();
            var catalog=CanonicalContent.CreateCatalog();
            Assert.That(catalog.FirstPlayable,Is.Null);
            Assert.That(catalog.Runtime,Is.Null);
            var session=new InteractiveSession(catalog,new InteractiveOptions(new RunOptions(30000,catalog.Tuning.DefaultHero,catalog.Tuning.DefaultEstate,"mixed",ManualCards:true),AimMode.Movement,CanonicalContent.DataHash));
            var wave=((IWaveRunView)session.View).CaptureWaveRuntime();
            var envelope=WavePresentation.Envelope(catalog,session.View.CaptureFrame(),session.View.CaptureCards(),wave);
            Assert.That(envelope.HeroId,Is.EqualTo(catalog.Tuning.DefaultHero));
            Assert.That(envelope.Items.Keys,Is.EquivalentTo(wave.Items));
            Assert.That(envelope.Evolutions.Select(e=>e.Id),Is.EquivalentTo(catalog.WaveRuntime.Evolutions.Keys));
            Assert.That(envelope.Attacks,Is.Empty,"The legacy envelope must not duplicate wave attacks.");
            Assert.That(envelope.MapEvents,Is.Empty,"Historical events must not leak into wave profile.");
        }
    }
}
