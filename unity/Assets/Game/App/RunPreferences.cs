using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public enum FirstRunHint { Move = 1, Card = 2, Harvest = 4, Muster = 8 }

    public sealed class RunPreferences
    {
        public const string Prefix = "sowsiege.fp.v1.";
        public static readonly string[] Keys = { "musicVolume", "sfxVolume", "haptics", "shake", "damageNumbers", "aim", "introductionSeen", "seenHints" };
        readonly string prefix;
        public float MusicVolume { get; set; } = .55f;
        public float EffectsVolume { get; set; } = .8f;
        public bool Haptics { get; set; } = true;
        public bool Shake { get; set; } = true;
        public bool DamageNumbers { get; set; } = true;
        public AimMode Aim { get; set; }
        public bool IntroductionSeen { get; set; }
        public int SeenHints { get; private set; }

        RunPreferences(string prefix) { this.prefix = prefix; }
        public static RunPreferences Load(string prefix = Prefix)
        {
            var value = new RunPreferences(prefix);
            value.MusicVolume = Volume(PlayerPrefs.GetFloat(prefix + "musicVolume", .55f), .55f);
            value.EffectsVolume = Volume(PlayerPrefs.GetFloat(prefix + "sfxVolume", .8f), .8f);
            value.Haptics = PlayerPrefs.GetInt(prefix + "haptics", 1) != 0;
            value.Shake = PlayerPrefs.GetInt(prefix + "shake", 1) != 0;
            value.DamageNumbers = PlayerPrefs.GetInt(prefix + "damageNumbers", 1) != 0;
            value.Aim = PlayerPrefs.GetInt(prefix + "aim", 0) == (int)AimMode.NearestEnemy ? AimMode.NearestEnemy : AimMode.Movement;
            value.IntroductionSeen = PlayerPrefs.GetInt(prefix + "introductionSeen", 0) != 0;
            value.SeenHints = PlayerPrefs.GetInt(prefix + "seenHints", 0) & 15;
            return value;
        }

        public bool HasSeen(FirstRunHint hint) => (SeenHints & (int)hint) != 0;
        public void MarkHintSeen(FirstRunHint hint) { SeenHints = (SeenHints | (int)hint) & 15; Save(); }
        public void Save()
        {
            MusicVolume = Volume(MusicVolume, .55f); EffectsVolume = Volume(EffectsVolume, .8f);
            if (Aim != AimMode.NearestEnemy) Aim = AimMode.Movement;
            PlayerPrefs.SetFloat(prefix + "musicVolume", MusicVolume);
            PlayerPrefs.SetFloat(prefix + "sfxVolume", EffectsVolume);
            PlayerPrefs.SetInt(prefix + "haptics", Haptics ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "shake", Shake ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "damageNumbers", DamageNumbers ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "aim", (int)Aim);
            PlayerPrefs.SetInt(prefix + "introductionSeen", IntroductionSeen ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "seenHints", SeenHints);
            PlayerPrefs.Save();
        }

        static float Volume(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
    }
}
