using System;
using Game.App;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class RunPreferencesTests
    {
        string prefix;
        [SetUp] public void SetUp() => prefix = "sowsiege.fp.test." + Guid.NewGuid().ToString("N") + ".";
        [TearDown] public void TearDown()
        {
            foreach (var key in RunPreferences.Keys) PlayerPrefs.DeleteKey(prefix + key);
            PlayerPrefs.Save();
        }

        [Test] public void CorruptPreferencesKeepSafeDefaultsAndIndependentHints()
        {
            PlayerPrefs.SetFloat(prefix + "musicVolume", float.NaN);
            PlayerPrefs.SetFloat(prefix + "sfxVolume", 9);
            PlayerPrefs.SetInt(prefix + "aim", 777);
            PlayerPrefs.SetInt(prefix + "seenHints", 255);
            var settings = RunPreferences.Load(prefix);
            Assert.That(settings.MusicVolume, Is.EqualTo(.55f));
            Assert.That(settings.EffectsVolume, Is.EqualTo(1));
            Assert.That(settings.Aim, Is.EqualTo(AimMode.Movement));
            Assert.That(settings.SeenHints, Is.EqualTo(15));
            Assert.That(settings.Shake && settings.DamageNumbers && settings.Haptics, Is.True);
        }

        [Test] public void SettingsRoundTripWithoutConsumingUnshownHints()
        {
            var settings = RunPreferences.Load(prefix);
            settings.MusicVolume = .12f; settings.EffectsVolume = 0;
            settings.Haptics = false; settings.Shake = false; settings.DamageNumbers = false;
            settings.Aim = AimMode.NearestEnemy; settings.IntroductionSeen = true;
            settings.MarkHintSeen(FirstRunHint.Card);
            settings.Save();
            var loaded = RunPreferences.Load(prefix);
            Assert.That(loaded.MusicVolume, Is.EqualTo(.12f));
            Assert.That(loaded.EffectsVolume, Is.Zero);
            Assert.That(loaded.Haptics || loaded.Shake || loaded.DamageNumbers, Is.False);
            Assert.That(loaded.Aim, Is.EqualTo(AimMode.NearestEnemy));
            Assert.That(loaded.IntroductionSeen, Is.True);
            Assert.That(loaded.HasSeen(FirstRunHint.Card), Is.True);
            Assert.That(loaded.HasSeen(FirstRunHint.Move), Is.False);
            Assert.That(loaded.HasSeen(FirstRunHint.Harvest), Is.False);
            Assert.That(loaded.HasSeen(FirstRunHint.Muster), Is.False);
        }

        [Test] public void FeelTrialsDefaultOffAndSnapshotDoesNotFollowLaterPreferenceChanges()
        {
            var settings = RunPreferences.Load(prefix);
            Assert.That(settings.SnapshotFeel(), Is.Null);
            Assert.That(settings.FeelFeedback || settings.HeroRigPreview, Is.False);
            settings.FeelSpeed = true; settings.FeelContact = true; settings.FeelHealth = true;
            settings.FeelFeedback = true; settings.HeroRigPreview = true;
            var snapshot = settings.SnapshotFeel();
            settings.Save();
            settings.FeelSpeed = false;
            Assert.That(snapshot.FasterLord, Is.True);
            var loaded = RunPreferences.Load(prefix);
            Assert.That(loaded.SnapshotFeel(), Is.EqualTo(snapshot));
            Assert.That(loaded.FeelFeedback && loaded.HeroRigPreview, Is.True);
        }
    }
}
