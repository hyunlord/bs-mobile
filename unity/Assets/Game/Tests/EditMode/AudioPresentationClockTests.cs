using System;
using Game.View;
using NUnit.Framework;
using UnityEngine;

public sealed class AudioPresentationClockTests
{
    [Test] public void OfflineClockIsExplicitAndClearingItRestoresRealtime()
    {
        var host = new GameObject("Audio clock test");
        try
        {
            var common = host.AddComponent<FirstPlayableAudio>();
            var wave = host.AddComponent<WaveAudio>();
            common.SetPresentationClock(123.5f); wave.SetPresentationClock(123.5f);
            Assert.That(common.EditorPresentationClock, Is.EqualTo(123.5f));
            Assert.That(wave.EditorPresentationClock, Is.EqualTo(123.5f));
            common.SetPresentationClock(null); wave.SetPresentationClock(null);
            Assert.That(common.EditorPresentationClock, Is.EqualTo(Time.unscaledTime));
            Assert.That(wave.EditorPresentationClock, Is.EqualTo(Time.unscaledTime));
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
    [Test] public void HapticDeadlineUsesTheSamePresentationTime()
    {
        var host = new GameObject("Haptic clock test");
        try
        {
            var common = host.AddComponent<FirstPlayableAudio>();
            common.SetPresentationClock(10); common.PlayEvolutionHaptic();
            Assert.That(common.EditorHapticDeadline, Is.EqualTo(11));
            common.SetPresentationClock(10.5f); common.PlayEvolutionHaptic();
            Assert.That(common.EditorHapticDeadline, Is.EqualTo(11));
            common.SetPresentationClock(11); common.PlayEvolutionHaptic();
            Assert.That(common.EditorHapticDeadline, Is.EqualTo(12));
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
    [TestCase(-1f), TestCase(float.NaN), TestCase(float.PositiveInfinity)]
    public void InvalidClockValuesAreRejected(float value)
    {
        var host = new GameObject("Invalid audio clock test");
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => host.AddComponent<FirstPlayableAudio>().SetPresentationClock(value));
            Assert.Throws<ArgumentOutOfRangeException>(() => host.AddComponent<WaveAudio>().SetPresentationClock(value));
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
}
