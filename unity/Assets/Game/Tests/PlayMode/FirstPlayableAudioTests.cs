using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.App.Generated;
using Game.View;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    // Synthetic presentation-event routing tests, not normal-run or audible-output acceptance evidence.
    public sealed class FirstPlayableAudioTests
    {
        GameObject owner;
        GameObject listenerOwner;
        FirstPlayableAudio audio;
        AudioSource[] sources;
        ContentCatalog catalog;
        RunFrame baseline;
        FirstPlayableFrame snapshot;

        [SetUp]
        public void SetUp()
        {
            if (Object.FindFirstObjectByType<AudioListener>() == null)
            {
                listenerOwner = new GameObject("Audio test listener"); listenerOwner.AddComponent<AudioListener>();
            }
            catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            baseline = session.View.CaptureFrame(); snapshot = session.View.CaptureFirstPlayable();
            catalog = catalog with
            {
                FirstPlayable = new FirstPlayableDefinition(1,
                    new Dictionary<string, FirstPlayableWeaponDefinition>(),
                    new Dictionary<string, FirstPlayableEnemyDefinition>
                    {
                        ["fixture:audio_boss"] = new FirstPlayableEnemyDefinition("boss", 0, 0, 1),
                        ["fixture:audio_elite"] = new FirstPlayableEnemyDefinition("elite", 0, 0, 1)
                    },
                    Array.Empty<MapEventDefinition>(), new Dictionary<string, EvolutionRequirement[]>(), 1, 1, 1)
            };
            owner = new GameObject("Presentation audio test"); audio = owner.AddComponent<FirstPlayableAudio>();
            audio.SetPreferences(0.45f, 0.75f, false); audio.Initialize(catalog);
            sources = owner.GetComponents<AudioSource>();
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (listenerOwner != null) Object.DestroyImmediate(listenerOwner);
        }

        [UnityTest]
        public IEnumerator ResourcesHaveSevenDistinctEffectClipsAndOnePlayingMusicLoop()
        {
            yield return null;
            Assert.That(sources.Length, Is.EqualTo(8));
            Assert.That(sources.Select(s => s.clip).Distinct().Count(), Is.EqualTo(8));
            CollectionAssert.AreEquivalent(new[] { "hit", "kill", "harvest", "level", "evolution", "hurt", "boss", "frontier-loop" }, sources.Select(s => s.clip.name));
            foreach (var source in sources)
            {
                Assert.That(source.clip.samples, Is.GreaterThan(0));
                Assert.That(source.loop, Is.EqualTo(source.clip.name == "frontier-loop"));
                Assert.That(source.spatialBlend, Is.Zero);
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.isPlaying, Is.EqualTo(source.loop));
            }
        }

        [UnityTest]
        public IEnumerator ActualVoicesRouteEventsAndLevelWithoutConfusingEliteWarnings()
        {
            yield return null;
            var boss = catalog.FirstPlayable.Enemies.Single(p => p.Value.Rank == "boss").Key;
            var elite = catalog.FirstPlayable.Enemies.First(p => p.Value.Rank == "elite").Key;
            var kinds = new[] { PresentationKind.Damage, PresentationKind.EnemyKilled, PresentationKind.HarvestExperience,
                PresentationKind.Evolution, PresentationKind.LordHit, PresentationKind.BossWarning };
            var clips = new[] { "hit", "kill", "harvest", "evolution", "hurt", "boss" };
            for (var i = 0; i < kinds.Length; i++)
            {
                audio.ResetRun(); audio.AcceptFrame(baseline, snapshot);
                audio.AcceptFrame(baseline with { Events = new[] { Event(0, kinds[i], boss) } }, snapshot);
                AssertEffects(clips[i]);
            }
            audio.ResetRun(); audio.AcceptFrame(baseline, snapshot);
            audio.AcceptFrame(baseline with { Events = new[] { Event(0, PresentationKind.BossWarning, elite), Event(1, PresentationKind.Damage, "", 0) } }, snapshot);
            AssertEffects();
            audio.AcceptFrame(baseline with { Level = baseline.Level + 1, Events = new[] { Event(2, PresentationKind.Evolution) } }, snapshot);
            AssertEffects("level", "evolution");
        }

        [UnityTest]
        public IEnumerator AcceptedEventsAndLevelAreDeduplicatedUntilRunReset()
        {
            yield return null;
            audio.AcceptFrame(baseline, snapshot);
            var frame = baseline with { Level = baseline.Level + 1, Events = new[] { Event(7, PresentationKind.Evolution) } };
            audio.AcceptFrame(frame, snapshot); AssertEffects("level", "evolution");
            StopEffects();
            audio.AcceptFrame(frame, snapshot); AssertEffects();
            audio.AcceptFrame(frame with { Events = new[] { Event(6, PresentationKind.Evolution) } }, snapshot); AssertEffects();
            audio.AcceptFrame(frame with { Events = new[] { Event(8, PresentationKind.Evolution) } }, snapshot); AssertEffects("evolution");
            audio.ResetRun(); AssertEffects();
            audio.AcceptFrame(baseline, snapshot); audio.AcceptFrame(frame, snapshot);
            AssertEffects("level", "evolution");
        }

        [UnityTest]
        public IEnumerator VolumesMuteAndPauseControlActualSourcesIndependently()
        {
            yield return null;
            audio.SetPreferences(0.8f, 0.2f, false);
            Assert.That(Source("frontier-loop").volume, Is.EqualTo(0.24f).Within(0.0001f));
            foreach (var source in sources.Where(s => !s.loop)) Assert.That(source.volume, Is.EqualTo(0.14f).Within(0.0001f));
            audio.AcceptFrame(baseline, snapshot);
            audio.AcceptFrame(baseline with { Events = new[] { Event(0, PresentationKind.Evolution) } }, snapshot);
            AssertEffects("evolution");
            audio.SetPaused(true);
            Assert.That(sources.All(s => !s.isPlaying), Is.True);
            yield return null;
            Assert.That(sources.All(s => !s.isPlaying), Is.True);
            audio.SetPaused(false);
            Assert.That(Source("frontier-loop").isPlaying, Is.True); AssertEffects("evolution");
            StopEffects(); audio.SetPreferences(0, 0, false);
            Assert.That(sources.All(s => s.volume == 0), Is.True);
            audio.AcceptFrame(baseline with { Events = new[] { Event(1, PresentationKind.Evolution) } }, snapshot); AssertEffects();
            audio.SetPreferences(0.8f, 0.2f, false);
            audio.AcceptFrame(baseline with { Events = new[] { Event(1, PresentationKind.Evolution) } }, snapshot); AssertEffects();
            audio.AcceptFrame(baseline with { Events = new[] { Event(2, PresentationKind.Evolution) } }, snapshot); AssertEffects("evolution");
            audio.SetPaused(true); audio.ResetRun();
            Assert.That(sources.All(s => !s.isPlaying), Is.True);
            audio.SetPaused(false); Assert.That(Source("frontier-loop").isPlaying, Is.True); AssertEffects();
        }

        AudioSource Source(string name) => sources.Single(s => s.clip.name == name);

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator HapticRequestsCoverLevelEvolutionAndLordHitWithoutInitialLevelPulse()
        {
            yield return null;
            audio.SetPreferences(0, 0, true);
            audio.AcceptFrame(baseline, snapshot);
            Assert.That(HapticDeadline(), Is.Zero);
            audio.AcceptFrame(baseline with { Level = baseline.Level + 1 }, snapshot);
            Assert.That(HapticDeadline(), Is.GreaterThan(Time.unscaledTime));
            foreach (var kind in new[] { PresentationKind.Evolution, PresentationKind.LordHit })
            {
                audio.ResetRun(); audio.AcceptFrame(baseline, snapshot);
                audio.AcceptFrame(baseline with { Events = new[] { Event(1, kind) } }, snapshot);
                Assert.That(HapticDeadline(), Is.GreaterThan(Time.unscaledTime));
            }
        }

        [UnityTest]
        public IEnumerator HapticPreferencePauseAndCooldownApplyToWaveEvolutionEntryPoint()
        {
            yield return null;
            audio.PlayEvolutionHaptic(); Assert.That(HapticDeadline(), Is.Zero);
            audio.SetPreferences(0, 0, true); audio.SetPaused(true);
            audio.PlayEvolutionHaptic(); Assert.That(HapticDeadline(), Is.Zero);
            audio.SetPaused(false); audio.PlayEvolutionHaptic();
            var deadline = HapticDeadline();
            Assert.That(deadline, Is.GreaterThan(Time.unscaledTime));
            audio.PlayEvolutionHaptic(); Assert.That(HapticDeadline(), Is.EqualTo(deadline));
            audio.ResetRun(); Assert.That(HapticDeadline(), Is.Zero);
        }

        [UnityTest]
        public IEnumerator CommonFeedbackLeavesEvolutionToWaveOwnerAndDeduplicatesLevelAndHit()
        {
            yield return null;
            audio.SetPreferences(0, 0, true);
            audio.AcceptCommonFeedback(baseline, IgnoreCue);
            audio.AcceptCommonFeedback(baseline with { Events = new[] { Event(1, PresentationKind.Evolution) } }, IgnoreCue);
            Assert.That(HapticDeadline(), Is.Zero);
            var next = baseline with { Level = baseline.Level + 1, Events = new[] { Event(2, PresentationKind.LordHit) } };
            audio.AcceptCommonFeedback(next, IgnoreCue);
            var deadline = HapticDeadline(); Assert.That(deadline, Is.GreaterThan(Time.unscaledTime));
            audio.AcceptCommonFeedback(next, IgnoreCue);
            Assert.That(HapticDeadline(), Is.EqualTo(deadline));
        }

        static void IgnoreCue(string cue) { }
        float HapticDeadline() => audio.EditorHapticDeadline;
#endif
        void StopEffects() { foreach (var source in sources) if (!source.loop) source.Stop(); }
        void AssertEffects(params string[] playing) => CollectionAssert.AreEquivalent(playing, sources.Where(s => !s.loop && s.isPlaying).Select(s => s.clip.name));
        static PresentationEvent Event(long id, PresentationKind kind, string source = "", long amount = 1) =>
            new(id, 0, kind, source, new(0, 0), new(0, 0), "", 0, amount, Array.Empty<WorldPoint>(), Array.Empty<int>());
    }
}
