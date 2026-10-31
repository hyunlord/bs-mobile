using System;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed class ImpactAudioTests
    {
        GameObject owner;
        WaveAudio audio;
        AudioSource[] voices;

        [SetUp]
        public void SetUp()
        {
            owner = new GameObject("Impact audio fixture");
            if (Object.FindFirstObjectByType<AudioListener>() == null) owner.AddComponent<AudioListener>();
            audio = owner.AddComponent<WaveAudio>();
            audio.Initialize();
            audio.SetVolume(1);
            voices = owner.GetComponents<AudioSource>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(owner);

        [Test]
        public void PitchIsStableBoundedAndVariesWithoutASequence()
        {
            var minimum = 2f;
            var maximum = 0f;
            for (long id = -2048; id <= 2048; id++)
            {
                var pitch = FirstPlayableAudio.HitPitch(id);
                Assert.That(pitch, Is.InRange(.95f, 1.05f));
                Assert.That(FirstPlayableAudio.HitPitch(id), Is.EqualTo(pitch));
                minimum = Math.Min(minimum, pitch);
                maximum = Math.Max(maximum, pitch);
            }
            Assert.That(maximum - minimum, Is.GreaterThan(.09f));
        }

        [Test]
        public void ReusedHitVoiceResetsPitchForDistinctDeathCue()
        {
            audio.PlayHit(27);
            var hitVoice = VoiceWithClip("hit");
            Assert.That(hitVoice.pitch, Is.EqualTo(FirstPlayableAudio.HitPitch(27)).Within(.00001f));
            hitVoice.Stop();
            audio.PlayKill();
            Assert.That(VoiceWithClip("kill"), Is.SameAs(hitVoice));
            Assert.That(hitVoice.pitch, Is.EqualTo(1));
            Assert.That(owner.GetComponents<AudioSource>().Length, Is.EqualTo(6));
        }

        [Test]
        public void ComparisonToggleRestoresBaselinePitchAndMutePauseSkipPlayback()
        {
            audio.SetImpactFeedback(false);
            audio.PlayHit(27);
            Assert.That(VoiceWithClip("hit").pitch, Is.EqualTo(1));
            audio.ResetRun();
            audio.SetImpactFeedback(true);
            audio.PlayHit(27);
            Assert.That(VoiceWithClip("hit").pitch, Is.EqualTo(FirstPlayableAudio.HitPitch(27)).Within(.00001f));
            audio.ResetRun();
            foreach (var voice in voices) voice.clip = null;
            audio.SetVolume(0);
            audio.PlayHit(28);
            audio.PlayKill();
            foreach (var voice in voices) Assert.That(voice.clip, Is.Null);
            audio.SetVolume(1);
            audio.SetPaused(true);
            audio.PlayHit(29);
            audio.PlayKill();
            foreach (var voice in voices) Assert.That(voice.clip, Is.Null);
        }

        AudioSource VoiceWithClip(string name)
        {
            foreach (var voice in voices)
                if (voice.clip != null && voice.clip.name == name) return voice;
            Assert.Fail("Expected an assigned " + name + " cue.");
            return null;
        }
    }
}
