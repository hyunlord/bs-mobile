using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif
namespace Game.View
{
    public sealed class WaveAudio : MonoBehaviour
    {
        [Serializable] sealed class Manifest { public Cue[] cues; }
        [Serializable] sealed class Cue { public string id, resourcePath; public int priority; public float cooldownSeconds; }
        readonly Dictionary<string,Cue> cues = new Dictionary<string,Cue>(StringComparer.Ordinal);
        readonly Dictionary<string,AudioClip> clips = new Dictionary<string,AudioClip>(StringComparer.Ordinal);
        readonly Dictionary<string,float> next = new Dictionary<string,float>(StringComparer.Ordinal);
        readonly AudioSource[] voices = new AudioSource[6];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly ProfilerMarker AudioResourceLoadMarker = new ProfilerMarker("SowSiege.View.AudioResourceLoad");
        static readonly ProfilerMarker AudioSourceCreateMarker = new ProfilerMarker("SowSiege.View.AudioSourceCreate");
        static readonly ProfilerMarker AudioPlaybackMarker = new ProfilerMarker("SowSiege.View.AudioPlayback");
        static readonly ProfilerMarker AudioFirstPlaybackMarker = new ProfilerMarker("SowSiege.View.AudioFirstPlayback");
        readonly HashSet<string> playedOnce = new HashSet<string>(StringComparer.Ordinal);
#endif
        readonly int[] priorities = new int[6];
        float volume;
        bool paused;
        public void Initialize()
        {
            var registry = Resources.Load<ArtRegistry>("Wave1aArt");
            if (registry == null || registry.audioManifest == null) throw new InvalidOperationException("Wave audio manifest missing.");
            var manifest = JsonUtility.FromJson<Manifest>(registry.audioManifest.text);
            foreach(var cue in manifest.cues)
            {
                cues.Add(cue.id,cue);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var clip = LoadClip(cue.resourcePath);
#else
                var clip = Resources.Load<AudioClip>(cue.resourcePath);
#endif
                if(clip == null) throw new InvalidOperationException("Wave cue missing: "+cue.id);
                clips.Add(cue.id,clip);
            }
            foreach(var name in new[]{"hit","kill","harvest","level","hurt"})
            {
                var id="common-"+name;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var clip=LoadClip("Audio/"+name);
#else
                var clip=Resources.Load<AudioClip>("Audio/"+name);
#endif
                if(clip==null)throw new InvalidOperationException("Common feedback missing: "+name);
                cues.Add(id,new Cue { id=id, priority=name=="hurt"?30:name=="level"?60:140, cooldownSeconds=name=="hurt"?.4f:name=="level"?0:.14f });
                clips.Add(id,clip);
            }
            for(var i=0;i<voices.Length;i++)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                voices[i]=CreateVoice();
#else
                voices[i]=gameObject.AddComponent<AudioSource>();
#endif
                voices[i].playOnAwake=false;voices[i].spatialBlend=0;
            }
        }
        public void SetVolume(float value) { volume=Mathf.Clamp01(value)*.7f;foreach(var voice in voices) if(voice!=null)voice.volume=volume; }
        public void SetPaused(bool value) { if(paused==value)return;paused=value;foreach(var voice in voices)if(voice!=null){if(value)voice.Pause();else voice.UnPause();} }
        public void ResetRun() { next.Clear();foreach(var voice in voices)voice.Stop(); }
        public void PlayCommon(string id) => Play("common-"+id);
        public void Play(string id)
        {
            if(paused||volume<=0)return;
            if(!cues.TryGetValue(id,out var cue))throw new InvalidOperationException("Unknown wave cue: "+id);
            if(next.TryGetValue(id,out var when)&&Time.unscaledTime<when)return;
            var first=cue.priority<=55?0:2;var slot=-1;
            for(var i=first;i<voices.Length;i++)if(!voices[i].isPlaying){slot=i;break;}
            if(slot<0)for(var i=first;i<voices.Length;i++)if(priorities[i]>cue.priority&&(slot<0||priorities[i]>priorities[slot]))slot=i;
            if(slot<0)return;
            next[id]=Time.unscaledTime+cue.cooldownSeconds;priorities[slot]=cue.priority;
            var voice=voices[slot];var clip=clips[id];
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (playedOnce.Add(id)) using (AudioFirstPlaybackMarker.Auto()) PlayVoice(voice, clip, volume, cue.priority);
            else PlayVoice(voice, clip, volume, cue.priority);
#else
            voice.Stop();voice.clip=clip;voice.volume=volume;voice.priority=cue.priority;voice.Play();
#endif
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        AudioClip LoadClip(string resourcePath)
        {
            using (AudioResourceLoadMarker.Auto()) return Resources.Load<AudioClip>(resourcePath);
        }
        AudioSource CreateVoice()
        {
            using (AudioSourceCreateMarker.Auto()) return gameObject.AddComponent<AudioSource>();
        }
        static void PlayVoice(AudioSource voice, AudioClip clip, float gain, int priority)
        {
            using (AudioPlaybackMarker.Auto())
            {
                voice.Stop();voice.clip=clip;voice.volume=gain;voice.priority=priority;voice.Play();
            }
        }
#endif
    }
}
