using System;
using SowSiege.Core;
using UnityEngine;

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
        ContentCatalog catalog;
        AudioSource music;
        long lastEventId = -1;
        int lastLevel = -1;
        float nextHapticTime;
        float musicVolume = .45f;
        float effectsVolume = .75f;
        bool vibration = true;
        bool paused;
        bool initialized;

        public void Initialize(ContentCatalog content)
        {
            if (initialized) throw new InvalidOperationException("Audio is already initialized.");
            catalog = content ?? throw new ArgumentNullException(nameof(content));
            for (var i = 0; i < voices.Length; i++) voices[i] = CreateSource(ClipNames[i], false);
            music = CreateSource("frontier-loop", true);
            initialized = true;
            SetPreferences(musicVolume, effectsVolume, vibration);
            ResetRun();
        }

        AudioSource CreateSource(string name, bool loop)
        {
            var clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null) throw new InvalidOperationException("Missing original audio: " + name);
            var source = gameObject.AddComponent<AudioSource>();
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
            music.Play();
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
            void Emit(Cue cue) { if(commonOutput == null) Play(cue); else commonOutput(ClipNames[(int)cue]); }
            if (!initialized) throw new InvalidOperationException("Initialize audio before accepting frames.");
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            for (var i = 0; i < frame.Events.Count; i++)
            {
                var value = frame.Events[i];
                if (value.Id <= lastEventId) continue;
                lastEventId = value.Id;
                switch (value.Kind)
                {
                    case PresentationKind.Damage: if (value.Amount > 0) Emit(Cue.Hit); break;
                    case PresentationKind.EnemyKilled: Emit(Cue.Kill); break;
                    case PresentationKind.HarvestExperience: Emit(Cue.Harvest); break;
                    case PresentationKind.Evolution: if(commonOutput == null) Play(Cue.Evolution); break;
                    case PresentationKind.LordHit: Emit(Cue.Hurt); Vibrate(); break;
                    case PresentationKind.BossWarning:
                        if (commonOutput == null && catalog.FirstPlayable != null && catalog.FirstPlayable.Enemies.TryGetValue(value.SourceId, out var enemy) && enemy.Rank == "boss")
                        {
                            Play(Cue.Boss); Vibrate();
                        }
                        break;
                }
            }
            if (lastLevel >= 0 && frame.Level > lastLevel) Emit(Cue.Level);
            lastLevel = frame.Level;
        }

        void Play(Cue cue)
        {
            var index = (int)cue;
            var now = Time.unscaledTime;
            if (paused || effectsVolume <= 0 || now < nextCueTime[index]) return;
            nextCueTime[index] = now + Cooldowns[index];
            // Dedicated voices keep level/evolution/boss cues independent from combat floods.
            voices[index].Play();
        }

        void Vibrate()
        {
            if (!vibration || paused || Time.unscaledTime < nextHapticTime) return;
            nextHapticTime = Time.unscaledTime + HapticInterval;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        void OnDestroy()
        {
            if (!initialized) return;
            music.Stop();
            for (var i = 0; i < voices.Length; i++) voices[i].Stop();
        }
    }
}
