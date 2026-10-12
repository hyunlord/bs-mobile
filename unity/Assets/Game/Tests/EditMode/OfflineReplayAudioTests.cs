using System;
using System.IO;
using Game.App;
using NUnit.Framework;
using UnityEngine;

public sealed class OfflineReplayAudioTests
{
    [Test] public void OrdinaryExecutionDoesNotRecordAudio() => Assert.That(OfflineReplayAudio.Requested(Array.Empty<string>()), Is.False);
    [Test] public void AudioRequiresExplicitOfflineCapture()
    {
        Assert.That(OfflineReplayAudio.Requested(new[] { "--offline-replay-capture", "--offline-replay-audio" }), Is.True);
        Assert.Throws<ArgumentException>(() => OfflineReplayAudio.Requested(new[] { "--offline-replay-audio" }));
        Assert.Throws<ArgumentException>(() => OfflineReplayAudio.Requested(new[] { "--offline-replay-capture", "--offline-replay-audio", "--offline-replay-audio" }));
    }
    [TestCase(48000, 1440000), TestCase(44100, 1323000)]
    public void ThirtySecondsHasExactSamplesWithoutResampling(int rate, long expected)
    {
        Assert.That(OfflineReplayAudio.ExpectedSampleFrames(1800, rate), Is.EqualTo(expected));
        Assert.That(OfflineReplayAudio.ExpectedSampleFrames(1, rate) * 1800, Is.EqualTo(expected));
    }
    [Test] public void NonIntegralRateIsRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => OfflineReplayAudio.ExpectedSampleFrames(1800, 44099));
    [Test] public void SurroundIsNotSilentlyDownmixed()
    {
        Assert.That(OfflineReplayAudio.Channels(AudioSpeakerMode.Stereo), Is.EqualTo(2));
        Assert.That(OfflineReplayAudio.Channels(AudioSpeakerMode.Mono), Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => OfflineReplayAudio.Channels(AudioSpeakerMode.Mode5point1));
    }
    [Test] public void WaveHeaderUsesInterleavedPcm16ByteCountAndPreservesPayloadPosition()
    {
        using var stream = new MemoryStream();
        OfflineReplayAudio.WriteWaveHeader(stream, 48000, 2, 800);
        Assert.That(stream.Length, Is.EqualTo(44)); Assert.That(stream.Position, Is.EqualTo(44));
        using var reader = new BinaryReader(stream);
        stream.Position = 4; Assert.That(reader.ReadUInt32(), Is.EqualTo(3236));
        stream.Position = 20; Assert.That(reader.ReadUInt16(), Is.EqualTo(1)); Assert.That(reader.ReadUInt16(), Is.EqualTo(2));
        Assert.That(reader.ReadInt32(), Is.EqualTo(48000)); Assert.That(reader.ReadInt32(), Is.EqualTo(192000));
        Assert.That(reader.ReadUInt16(), Is.EqualTo(4)); Assert.That(reader.ReadUInt16(), Is.EqualTo(16));
        stream.Position = 40; Assert.That(reader.ReadUInt32(), Is.EqualTo(3200));
        stream.Position = 100;
        OfflineReplayAudio.WriteWaveHeader(stream, 48000, 2, 1600);
        Assert.That(stream.Position, Is.EqualTo(100));
    }
}
