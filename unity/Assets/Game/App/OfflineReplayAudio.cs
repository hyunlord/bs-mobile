using System;
using System.Collections;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Game.App
{
    // Review-only sample-clock capture. The mixer advances even on warmup or excluded video frames.
    public sealed class OfflineReplayAudio : MonoBehaviour, IDisposable
    {
        NativeArray<float> buffer;
        byte[] pcm;
        FileStream wave;
        StreamWriter index;
        string directory;
        int rate, channels, markedUnityFrame = -1, markedVideoFrame = -1, markedTick;
        float markedAlpha;
        long sampleFrames, nonzeroSamples;
        double sumSquares, peak;
        int frames, renderedFrames, discardedFrames;
        bool started, disposed;
        public Exception Error { get; private set; }
        public static bool Requested(string[] args)
        {
            var position = Array.IndexOf(args, "--offline-replay-audio");
            if (position < 0) return false;
            if (position != Array.LastIndexOf(args, "--offline-replay-audio") || Array.IndexOf(args, "--offline-replay-capture") < 0)
                throw new ArgumentException("Offline audio requires one --offline-replay-audio and --offline-replay-capture.");
            return true;
        }
        public static int Channels(AudioSpeakerMode mode) => mode == AudioSpeakerMode.Mono ? 1 :
            mode == AudioSpeakerMode.Stereo || mode == AudioSpeakerMode.Prologic ? 2 :
            throw new InvalidOperationException("Offline review audio requires mono or stereo output; do not silently downmix.");
        public static long ExpectedSampleFrames(int frames, int sampleRate)
        {
            if (frames < 0 || sampleRate <= 0 || sampleRate % OfflineReplayCapture.FramesPerSecond != 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate), "Capture requires an integral sample count per 60fps frame.");
            return (long)frames * (sampleRate / OfflineReplayCapture.FramesPerSecond);
        }
        public static void WriteWaveHeader(Stream stream, int sampleRate, int channelCount, long sampleFrameCount)
        {
            if (!stream.CanSeek || sampleFrameCount < 0 || channelCount < 1 || channelCount > 2) throw new ArgumentException("Invalid PCM WAV contract.");
            var bytes = checked((uint)(sampleFrameCount * channelCount * 2));
            var position = stream.Position;
            stream.Position = 0;
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); writer.Write(checked(bytes + 36));
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)channelCount); writer.Write(sampleRate);
                writer.Write(checked(sampleRate * channelCount * 2)); writer.Write((ushort)(channelCount * 2)); writer.Write((ushort)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(bytes);
            }
            stream.Position = Math.Max(44, position);
        }
        public void Initialize(string output)
        {
            directory = output;
            rate = AudioSettings.outputSampleRate; channels = Channels(AudioSettings.GetConfiguration().speakerMode);
            var perFrame = checked((int)ExpectedSampleFrames(1, rate));
            buffer = new NativeArray<float>(perFrame * channels, Allocator.Persistent);
            pcm = new byte[buffer.Length * 2];
            wave = new FileStream(Path.Combine(directory, "offline-audio.wav"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            WriteWaveHeader(wave, rate, channels, 0);
            index = new StreamWriter(Path.Combine(directory, "offline-audio-index.csv"));
            index.WriteLine("videoFrame,unityFrame,tick,alpha,startSampleFrame,sampleFrames,sampleRate,channels");
            if (!AudioRenderer.Start()) throw new InvalidOperationException("Unity AudioRenderer could not enter offline mode.");
            started = true;
            StartCoroutine(RenderFrames());
        }
        public void MarkVideoFrame(int videoFrame, int tick, float alpha)
        {
            if (markedUnityFrame == Time.frameCount) throw new InvalidOperationException("Audio frame marked twice.");
            markedUnityFrame = Time.frameCount; markedVideoFrame = videoFrame; markedTick = tick; markedAlpha = alpha;
        }
        IEnumerator RenderFrames()
        {
            var end = new WaitForEndOfFrame();
            while (!disposed)
            {
                yield return end;
                if (disposed || Error != null) continue;
                try { RenderFrame(); }
                catch (Exception exception) { Error = exception; }
            }
        }
        void RenderFrame()
        {
            var count = AudioRenderer.GetSampleCountForCaptureFrame();
            if (count < 0 || count * channels > buffer.Length) throw new InvalidDataException("Unity audio returned more than one fixed frame; refusing resampling or sample loss.");
            var samples = buffer.GetSubArray(0, count * channels);
            if (!AudioRenderer.Render(samples)) throw new InvalidOperationException("Unity AudioRenderer.Render failed.");
            renderedFrames++;
            if (markedUnityFrame != Time.frameCount) { discardedFrames++; return; }
            if (markedVideoFrame != frames || count != ExpectedSampleFrames(1, rate)) throw new InvalidDataException("Offline audio/video sample boundary mismatch.");
            for (var i = 0; i < samples.Length; i++)
            {
                var value = samples[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Non-finite mixer sample.");
                var magnitude = Math.Abs(value); peak = Math.Max(peak, magnitude); sumSquares += value * (double)value;
                if (magnitude > .000001) nonzeroSamples++;
                var sample = (short)Math.Round(Math.Max(-1, Math.Min(1, value)) * short.MaxValue);
                pcm[i * 2] = (byte)(sample & 255); pcm[i * 2 + 1] = (byte)((sample >> 8) & 255);
            }
            wave.Write(pcm, 0, samples.Length * 2);
            index.WriteLine(FormattableString.Invariant($"{frames},{Time.frameCount},{markedTick},{markedAlpha:R},{sampleFrames},{count},{rate},{channels}"));
            sampleFrames += count; frames++;
        }
        public void Finish(int expectedFrames, bool verified)
        {
            wave.Flush(); index.Flush(); WriteWaveHeader(wave, rate, channels, sampleFrames); wave.Flush();
            var complete = verified && Error == null && frames == expectedFrames && sampleFrames == ExpectedSampleFrames(expectedFrames, rate) && nonzeroSamples > 0;
            File.WriteAllText(Path.Combine(directory, "offline-audio.json"), JsonUtility.ToJson(new Manifest {
                sampleRate = rate, channels = channels, videoFrames = frames, expectedFrames = expectedFrames,
                sampleFrames = sampleFrames, renderedFrames = renderedFrames, discardedFrames = discardedFrames,
                nonzeroSamples = nonzeroSamples, peak = peak, rms = sampleFrames == 0 ? 0 : Math.Sqrt(sumSquares / (sampleFrames * channels)),
                complete = complete, error = Error?.ToString()
            }, true));
            if (!complete) throw new InvalidDataException("Offline audio incomplete, silent or not sample-locked to video.", Error);
        }
        [Serializable] sealed class Manifest
        {
            public string contract = "unity-audiorenderer-offline-pcm16-v1";
            public string source = "Unity main mixer via AudioRenderer; no microphone, OS recording or substituted soundtrack";
            public string timeline = "One AudioRenderer.Render per rendered frame, including discarded warmup/preroll; WAV contains exactly the marked video frames, no padding, trimming or resampling. Pair index CSV with offline-frame-index.csv.";
            public int sampleRate, channels, videoFrames, expectedFrames, renderedFrames, discardedFrames;
            public long sampleFrames, nonzeroSamples;
            public double peak, rms;
            public bool complete, performanceEligible = false;
            public string error;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; StopAllCoroutines();
            if (started) AudioRenderer.Stop();
            if (wave != null) { WriteWaveHeader(wave, rate, channels, sampleFrames); wave.Dispose(); }
            index?.Dispose(); if (buffer.IsCreated) buffer.Dispose();
        }
        void OnDestroy() => Dispose();
    }
}
