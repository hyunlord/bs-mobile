using System;
using SowSiege.Core;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif

namespace Game.View
{
    public sealed class FirstPlayableAudio : MonoBehaviour
    {
        const float EffectsGain = .7f;
        const float MusicGain = .3f;
        const float HapticInterval = 1f;
        enum Cue { Hit, Kill, Harvest, Level, Evolution, Hurt, Boss }
        static readonly string[] ClipNames = { "hit", "kill", "harvest", "level", "evolution", "hurt", "boss" };
        static readonly float[] Cooldowns = { .09f, .14f, .12f, 0f, 0f, .4f, 0f };
        readonly AudioSource[] voices = new AudioSource[7];
        readonly float[] nextCueTime = new float[7];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly ProfilerMarker AudioResourceLoadMarker = new ProfilerMarker("SowSiege.View.AudioResourceLoad");
        static readonly ProfilerMarker AudioSourceCreateMarker = new ProfilerMarker("SowSiege.View.AudioSourceCreate");
        static readonly ProfilerMarker AudioPlaybackMarker = new ProfilerMarker("SowSiege.View.AudioPlayback");
        static readonly ProfilerMarker AudioFirstPlaybackMarker = new ProfilerMarker("SowSiege.View.AudioFirstPlayback");
        readonly bool[] playedOnce = new bool[7];
        bool musicPlayedOnce;
#endif
        ContentCatalog catalog;
        ShortHaptics haptics;
        AudioSource music;
        long lastEventId = -1;
        int lastLevel = -1;
        float nextHapticTime;
#if UNITY_EDITOR
        public float EditorHapticDeadline => nextHapticTime;
#endif
        float musicVolume = .45f;
        float effectsVolume = .75f;
        bool vibration = true;
        bool paused;
        bool initialized;
        bool impactFeedback = true;

        public void SetImpactFeedback(bool enabled) => impactFeedback = enabled;

        // Presentation-only variation: no simulation RNG or mutable random sequence.
        public static float HitPitch(long eventId)
        {
            unchecked
            {
                var value = (ulong)eventId + 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return .95f + (value & 0xFFFFUL) * (.1f / 65535f);
            }
        }

        public void Initialize(ContentCatalog content)
        {
            if (initialized) throw new InvalidOperationException("Audio is already initialized.");
            catalog = content ?? throw new ArgumentNullException(nameof(content));
            for (var i = 0; i < voices.Length; i++) voices[i] = CreateSource(ClipNames[i], false);
            music = CreateSource("frontier-loop", true);
            haptics = new ShortHaptics();
            initialized = true;
            SetPreferences(musicVolume, effectsVolume, vibration);
            ResetRun();
        }

        AudioSource CreateSource(string name, bool loop)
        {
            AudioClip clip;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (AudioResourceLoadMarker.Auto()) clip = Resources.Load<AudioClip>("Audio/" + name);
#else
            clip = Resources.Load<AudioClip>("Audio/" + name);
#endif
            if (clip == null) throw new InvalidOperationException("Missing original audio: " + name);
            AudioSource source;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (AudioSourceCreateMarker.Auto()) source = gameObject.AddComponent<AudioSource>();
#else
            source = gameObject.AddComponent<AudioSource>();
#endif
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0;
            source.clip = clip;
            source.priority = loop ? 128 : 64;
            return source;
        }

        public void SetPreferences(float musicLevel, float effectsLevel, bool vibrate)
        {
            if (float.IsNaN(musicLevel) || float.IsInfinity(musicLevel) || float.IsNaN(effectsLevel) || float.IsInfinity(effectsLevel))
                throw new ArgumentException("Audio volume must be finite.");
            musicVolume = Mathf.Clamp01(musicLevel);
            effectsVolume = Mathf.Clamp01(effectsLevel);
            vibration = vibrate;
            if (!initialized) return;
            music.volume = musicVolume * MusicGain;
            for (var i = 0; i < voices.Length; i++) voices[i].volume = effectsVolume * EffectsGain;
        }

        public void ResetRun()
        {
            lastEventId = -1;
            lastLevel = -1;
            nextHapticTime = 0;
            for (var i = 0; i < nextCueTime.Length; i++)
            {
                nextCueTime[i] = 0;
                if (initialized) voices[i].Stop();
            }
            if (!initialized) return;
            music.Stop();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlaySource(music, ref musicPlayedOnce);
#else
            music.Play();
#endif
            if (paused) music.Pause();
        }

        public void SetPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (!initialized) return;
            if (paused) music.Pause(); else music.UnPause();
            for (var i = 0; i < voices.Length; i++)
            {
                if (paused) voices[i].Pause(); else voices[i].UnPause();
            }
        }

        public void AcceptFrame(RunFrame frame, FirstPlayableFrame firstPlayable) => AcceptFeedback(frame, null);

        public void AcceptCommonFeedback(RunFrame frame, Action<string> output) => AcceptFeedback(frame, output ?? throw new ArgumentNullException(nameof(output)));

        void AcceptFeedback(RunFrame frame, Action<string> commonOutput)
        {
            if (!initialized) throw new InvalidOperationException("Initialize audio before accepting frames.");
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            for (var i = 0; i < frame.Events.Count; i++)
            {
                var value = frame.Events[i];
                if (value.Id <= lastEventId) continue;
                lastEventId = value.Id;
                switch (value.Kind)
                {
                    case PresentationKind.Damage: if (value.Amount > 0) Emit(Cue.Hit, commonOutput, value.Id); break;
                    case PresentationKind.EnemyKilled: Emit(Cue.Kill, commonOutput); break;
                    case PresentationKind.HarvestExperience: Emit(Cue.Harvest, commonOutput); break;
                    case PresentationKind.Evolution:
                        if(commonOutput == null) { Play(Cue.Evolution); PlayEvolutionHaptic(); }
                        break;
                    case PresentationKind.LordHit: Emit(Cue.Hurt, commonOutput); Vibrate(); break;
                    case PresentationKind.BossWarning:
                        if (commonOutput == null && catalog.FirstPlayable != null && catalog.FirstPlayable.Enemies.TryGetValue(value.SourceId, out var enemy) && enemy.Rank == "boss")
                        {
                            Play(Cue.Boss); Vibrate();
                        }
                        break;
                }
            }
            if (lastLevel >= 0 && frame.Level > lastLevel) { Emit(Cue.Level, commonOutput); Vibrate(); }
            lastLevel = frame.Level;
        }

        void Emit(Cue cue, Action<string> output, long eventId = 0)
        {
            if (output == null) Play(cue, eventId);
            else output(ClipNames[(int)cue]);
        }

        void Play(Cue cue, long eventId = 0)
        {
            var index = (int)cue;
            var now = Time.unscaledTime;
            if (paused || effectsVolume <= 0 || now < nextCueTime[index]) return;
            nextCueTime[index] = now + Cooldowns[index];
            voices[index].pitch = cue == Cue.Hit && impactFeedback ? HitPitch(eventId) : 1f;
            // Dedicated voices keep level/evolution/boss cues independent from combat floods.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlaySource(voices[index], ref playedOnce[index]);
#else
            voices[index].Play();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static void PlaySource(AudioSource source, ref bool firstObserved)
        {
            if (!firstObserved)
            {
                firstObserved = true;
                using (AudioFirstPlaybackMarker.Auto()) PlaySource(source);
                return;
            }
            PlaySource(source);
        }
        static void PlaySource(AudioSource source)
        {
            using (AudioPlaybackMarker.Auto())
            {
                source.Play();
            }
        }
#endif

        public void PlayEvolutionHaptic() => Vibrate();

        void Vibrate()
        {
            if (!vibration || paused || Time.unscaledTime < nextHapticTime) return;
            nextHapticTime = Time.unscaledTime + HapticInterval;
            haptics?.Play();
        }

        void OnDestroy()
        {
            haptics?.Dispose();
            if (!initialized) return;
            music.Stop();
            for (var i = 0; i < voices.Length; i++) voices[i].Stop();
        }
    }
}
