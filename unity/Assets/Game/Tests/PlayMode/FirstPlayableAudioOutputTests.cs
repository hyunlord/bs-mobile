using System;
using System.Collections;
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
    // Actual listener history windows with synthetic canonical events; not continuous recording or listening.
    public sealed class FirstPlayableAudioOutputTests
    {
        const double AudibleRms = .0001;
        const double SilentRms = .00001;
        readonly float[] buffer = new float[1024];
        GameObject owner, listenerOwner;
        FirstPlayableAudio audio;
        AudioSource music;
        AudioSource[] otherSources;
        bool[] previousMutes;
        bool previousPause;
        float previousVolume;
        ContentCatalog catalog;
        RunFrame baseline;
        FirstPlayableFrame snapshot;

        sealed class Window
        {
            public int Samples;
            public double MaxRms;
            public float Peak;
        }

        [SetUp]
        public void SetUp()
        {
            otherSources = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            previousMutes = otherSources.Select(source => source.mute).ToArray();
            foreach (var source in otherSources) source.mute = true;
            previousPause = AudioListener.pause; previousVolume = AudioListener.volume;
            AudioListener.pause = false; AudioListener.volume = 1;
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)
                .Where(listener => listener.enabled && listener.gameObject.activeInHierarchy).ToArray();
            Assert.That(listeners.Length, Is.LessThanOrEqualTo(1), "Multiple active listeners invalidate the master-output fixture.");
            if (listeners.Length == 0)
            {
                listenerOwner = new GameObject("Master output fixture listener"); listenerOwner.AddComponent<AudioListener>();
            }
            catalog = CanonicalContent.CreateCatalog();
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, CanonicalContent.DataHash));
            baseline = session.View.CaptureFrame(); snapshot = session.View.CaptureFirstPlayable();
            // First call primes Unity's history buffer before any fixture source plays.
            AudioListener.GetOutputData(buffer, 0);
            owner = new GameObject("Master output fixture"); audio = owner.AddComponent<FirstPlayableAudio>();
            audio.SetPreferences(0, 0, false); audio.Initialize(catalog);
            music = owner.GetComponents<AudioSource>().Single(source => source.loop);
        }

        [TearDown]
        public void TearDown()
        {
            if (owner != null) Object.DestroyImmediate(owner);
            if (listenerOwner != null) Object.DestroyImmediate(listenerOwner);
            if (otherSources != null)
                for (var i = 0; i < otherSources.Length; i++) if (otherSources[i] != null) otherSources[i].mute = previousMutes[i];
            AudioListener.pause = previousPause; AudioListener.volume = previousVolume;
        }

        [UnityTest]
        public IEnumerator SevenSyntheticCueRoutesReachMasterAndMutedOutputDrainsToSilence()
        {
            yield return WaitForDsp(.15);
            var silent = new Window(); yield return Measure(.2, silent); AssertSilent(silent, "initial volume0");
            var kinds = new[] { PresentationKind.Damage, PresentationKind.EnemyKilled, PresentationKind.HarvestExperience,
                PresentationKind.Evolution, PresentationKind.LordHit, PresentationKind.BossWarning };
            var names = new[] { "hit", "kill", "harvest", "evolution", "hurt", "boss", "level" };
            var boss = catalog.FirstPlayable.Enemies.Single(pair => pair.Value.Rank == "boss").Key;
            for (var i = 0; i < names.Length; i++)
            {
                audio.SetPreferences(0, 1, false); audio.ResetRun(); audio.AcceptFrame(baseline, snapshot);
                yield return WaitForDsp(.15);
                var frame = i == kinds.Length
                    ? baseline with { Level = baseline.Level + 1 }
                    : baseline with { Events = new[] { Event(i, kinds[i], boss) } };
                audio.AcceptFrame(frame, snapshot);
                var heard = new Window(); yield return Measure(.3, heard);
                AssertAudible(heard, names[i]);
                audio.SetPreferences(0, 0, false);
                yield return WaitForDsp(.2);
                var muted = new Window(); yield return Measure(.15, muted); AssertSilent(muted, names[i] + " after mute");
            }
            // All dedicated voices plus music at maximum preferences: actual sampled master peak.
            audio.SetPreferences(1, 1, false); audio.ResetRun(); audio.AcceptFrame(baseline, snapshot);
            audio.AcceptFrame(baseline with { Level = baseline.Level + 1,
                Events = kinds.Select((kind, i) => Event(i, kind, boss)).ToArray() }, snapshot);
            var mix = new Window(); yield return Measure(.6, mix); AssertAudible(mix, "full eight-source mix");
            Assert.That(mix.Peak, Is.LessThan(1f), "Actual sampled master mix clipped.");
        }

        [UnityTest]
        public IEnumerator MusicMasterHistoryIsAudibleAcrossTwoObservedSixteenSecondLoopWraps()
        {
            yield return WaitForDsp(.15);
            audio.SetPreferences(1, 0, false); audio.ResetRun();
            Assert.That(music.clip.length, Is.EqualTo(16f).Within(.001f));
            var started = AudioSettings.dspTime; var deadline = Time.realtimeSinceStartupAsDouble + 45;
            var lastSample = music.timeSamples; var wraps = 0; var nextRead = started;
            var entire = new Window(); var beforeFirst = new Window(); var afterFirst = new Window(); var afterSecond = new Window();
            while (AudioSettings.dspTime - started < music.clip.length * 2 + .6)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Master capture environment unavailable: DSP did not finish two music cycles.");
                var elapsed = AudioSettings.dspTime - started;
                if (AudioSettings.dspTime >= nextRead)
                {
                    Read(entire); nextRead = AudioSettings.dspTime + .25;
                    var position = music.timeSamples;
                    if (position < lastSample) wraps++;
                    lastSample = position;
                    if (elapsed > 15 && elapsed < 16) Read(beforeFirst);
                    if (elapsed > 16 && elapsed < 17) Read(afterFirst);
                    if (elapsed > 32) Read(afterSecond);
                    Assert.That(music.isPlaying, Is.True, "Music stopped instead of looping.");
                }
                yield return null;
            }
            Assert.That(wraps, Is.GreaterThanOrEqualTo(2), "Two actual source-position wraps were not observed.");
            AssertAudible(entire, "music two cycles, sampled4Hz");
            AssertAudible(beforeFirst, "before first loop boundary"); AssertAudible(afterFirst, "after first loop boundary");
            AssertAudible(afterSecond, "after second loop boundary");
            audio.SetPreferences(0, 0, false); yield return WaitForDsp(.2);
            var muted = new Window(); yield return Measure(.2, muted); AssertSilent(muted, "music mute after two loops");
            TestContext.WriteLine("Observed source-position wraps=" + wraps + "; master history sampled4Hz, not continuous PCM or a listening verdict.");
        }

        IEnumerator WaitForDsp(double seconds)
        {
            var start = AudioSettings.dspTime; var deadline = Time.realtimeSinceStartupAsDouble + seconds + 3;
            while (AudioSettings.dspTime - start < seconds)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Master capture environment unavailable: AudioSettings.dspTime did not advance.");
                yield return null;
            }
        }

        IEnumerator Measure(double seconds, Window window)
        {
            var start = AudioSettings.dspTime; var deadline = Time.realtimeSinceStartupAsDouble + seconds + 3;
            do
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), "Master capture environment unavailable: DSP stalled while sampling.");
                Read(window); yield return null;
            } while (AudioSettings.dspTime - start < seconds);
        }

        void Read(Window window)
        {
            AudioListener.GetOutputData(buffer, 0); double square = 0; var finite = true;
            foreach (var value in buffer)
            {
                finite &= !float.IsNaN(value) && !float.IsInfinity(value);
                window.Peak = Mathf.Max(window.Peak, Mathf.Abs(value)); square += value * value;
            }
            Assert.That(finite, Is.True, "Non-finite actual master PCM.");
            window.MaxRms = Math.Max(window.MaxRms, Math.Sqrt(square / buffer.Length)); window.Samples++;
            Assert.That(window.Peak, Is.LessThan(1f), "Actual sampled master peak reached clipping.");
        }

        static void AssertAudible(Window window, string label)
        {
            TestContext.WriteLine(label + ": windows=" + window.Samples + ", maxRms=" + window.MaxRms + ", peak=" + window.Peak);
            Assert.That(window.Samples, Is.GreaterThan(0));
            Assert.That(window.MaxRms, Is.GreaterThan(AudibleRms), label + ": no positive master signal; investigate routing/device/batch audio, do not count as mute success.");
        }
        static void AssertSilent(Window window, string label)
        {
            TestContext.WriteLine(label + ": windows=" + window.Samples + ", maxRms=" + window.MaxRms + ", peak=" + window.Peak);
            Assert.That(window.Samples, Is.GreaterThan(0)); Assert.That(window.MaxRms, Is.LessThan(SilentRms), label + ": master history did not drain to silence.");
        }
        static PresentationEvent Event(long id, PresentationKind kind, string source) =>
            new(id, 0, kind, source, new(0, 0), new(0, 0), "", 0, 1, Array.Empty<WorldPoint>(), Array.Empty<int>());
    }
}
