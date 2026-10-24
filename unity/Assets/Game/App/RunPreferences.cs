using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public enum FirstRunHint { Move = 1, Card = 2, Harvest = 4, Muster = 8 }

    public sealed class RunPreferences
    {
        const int FeelDefaultsVersion = 1;
        public const string Prefix = "sowsiege.fp.v1.";
        public static readonly string[] Keys = { "musicVolume", "sfxVolume", "haptics", "shake", "damageNumbers", "aim", "introductionSeen", "seenHints", "feelSpeed", "feelContact", "feelHealth", "feelFeedback", "heroRigPreview", "heroRigSelectedB", "feelDefaultsVersion", "estatePrototype", "estateMapView" };
        readonly string prefix;
        public float MusicVolume { get; set; } = .55f;
        public float EffectsVolume { get; set; } = .8f;
        public bool Haptics { get; set; } = true;
        public bool Shake { get; set; } = true;
        public bool DamageNumbers { get; set; } = true;
        public AimMode Aim { get; set; }
        public bool IntroductionSeen { get; set; }
        public int SeenHints { get; private set; }
        public bool FeelSpeed { get; set; }
        public bool FeelContact { get; set; }
        public bool FeelHealth { get; set; }
        public bool FeelFeedback { get; set; }
        public bool HeroRigPreview { get; set; }
        // Development option (#366): the estate tab opens the M3a-2 map prototype instead of the manor page.
        public bool EstatePrototype { get; set; }
        // The estate map's last view (#379), "zoom;u;v" in invariant culture; empty until the player moves the map.
        public string EstateMapView { get; set; } = "";
        public FeelExperimentOptions SnapshotFeel() => FeelSpeed || FeelContact || FeelHealth
            ? new FeelExperimentOptions(FeelSpeed, FeelContact, FeelHealth) : null;

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
            value.FeelSpeed = PlayerPrefs.GetInt(prefix + "feelSpeed", 0) == 1;
            value.FeelContact = PlayerPrefs.GetInt(prefix + "feelContact", 0) == 1;
            value.FeelHealth = PlayerPrefs.GetInt(prefix + "feelHealth", 0) == 1;
            value.FeelFeedback = PlayerPrefs.GetInt(prefix + "feelFeedback", 0) == 1;
            value.HeroRigPreview = PlayerPrefs.GetInt(prefix + "heroRigSelectedB", 1) == 1;
            value.EstatePrototype = PlayerPrefs.GetInt(prefix + "estatePrototype", 0) == 1;
            value.EstateMapView = PlayerPrefs.GetString(prefix + "estateMapView", "");
            if (PlayerPrefs.GetInt(prefix + "feelDefaultsVersion", 0) < FeelDefaultsVersion)
            {
                value.FeelSpeed = true;
                value.FeelFeedback = true;
                value.FeelContact = false;
                value.FeelHealth = false;
                value.Save();
            }
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
            PlayerPrefs.SetInt(prefix + "feelSpeed", FeelSpeed ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "feelContact", FeelContact ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "feelHealth", FeelHealth ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "feelFeedback", FeelFeedback ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "heroRigSelectedB", HeroRigPreview ? 1 : 0);
            PlayerPrefs.SetInt(prefix + "estatePrototype", EstatePrototype ? 1 : 0);
            PlayerPrefs.SetString(prefix + "estateMapView", EstateMapView ?? "");
            PlayerPrefs.SetInt(prefix + "feelDefaultsVersion", FeelDefaultsVersion);
            PlayerPrefs.Save();
        }

        static float Volume(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);
    }
}
