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
