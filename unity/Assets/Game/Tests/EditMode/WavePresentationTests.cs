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
