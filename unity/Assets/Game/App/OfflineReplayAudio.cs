using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace Game.App
{
    // Review-only sample-clock capture. The mixer advances even on warmup or excluded video frames.
    public sealed class OfflineReplayAudio : MonoBehaviour, IDisposable
    {
        NativeArray<float> buffer;
        byte[] pcm, rawPcm;
        FileStream raw;
        StreamWriter blocks;
        readonly Queue<FrameMark> pending = new Queue<FrameMark>();
        struct FrameMark { public int videoFrame, unityFrame, tick; public float alpha; public long rawStart; }
        long rawSampleFrames, captureStart = -1, captureEnd;
        int firstRenderUnityFrame = -1, lastMarkedUnityFrame = -1, drainFrames;
        public int PendingFrames => pending.Count;
        FileStream wave;
        StreamWriter index;
        StreamWriter diagnostics;
        string directory;
        int rate, channels, markedUnityFrame = -1, markedVideoFrame = -1, markedTick;
        float markedAlpha;
        long sampleFrames, nonzeroSamples;
        double sumSquares, peak;
        int frames, renderedFrames, discardedFrames;
        int lastRenderUnityFrame = -1;
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
        public static int RenderSampleCount(int sampleFrames, int channelCount)
        {
            if (sampleFrames < 0) throw new InvalidDataException("Mixer returned a negative DSP sample count.");
            if (channelCount < 1 || channelCount > 2) throw new ArgumentOutOfRangeException(nameof(channelCount));
            return checked(sampleFrames * channelCount);
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
            pcm = new byte[buffer.Length * 2]; rawPcm = new byte[pcm.Length];
            raw = new FileStream(Path.Combine(directory, "offline-audio-raw.pcm"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            blocks = new StreamWriter(Path.Combine(directory, "offline-audio-blocks.csv"));
            blocks.WriteLine("renderFrame,unityFrame,startSampleFrame,sampleFrames,logicalEndSampleFrame,channels,sampleRate");
            wave = new FileStream(Path.Combine(directory, "offline-audio.wav"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            WriteWaveHeader(wave, rate, channels, 0);
            index = new StreamWriter(Path.Combine(directory, "offline-audio-index.csv"));
            index.WriteLine("videoFrame,unityFrame,tick,alpha,startSampleFrame,sampleFrames,sampleRate,channels,rawStartSampleFrame");
            diagnostics = new StreamWriter(Path.Combine(directory, "offline-audio-render.csv"));
            diagnostics.WriteLine("unityFrame,previousRenderUnityFrame,markedVideoFrame,captured,reportedSampleFrames,expectedSampleFrames,channels,sampleRate,captureFramerate,captureDeltaTime,unscaledDeltaTime");
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
            var captured = markedUnityFrame == Time.frameCount;
            diagnostics.WriteLine(FormattableString.Invariant($"{Time.frameCount},{lastRenderUnityFrame},{markedVideoFrame},{captured},{count},{ExpectedSampleFrames(1, rate)},{channels},{rate},{Time.captureFramerate},{Time.captureDeltaTime:R},{Time.unscaledDeltaTime:R}"));
            diagnostics.Flush();
            var floatCount = RenderSampleCount(count, channels);
            if (firstRenderUnityFrame < 0) firstRenderUnityFrame = Time.frameCount;
            if (lastRenderUnityFrame >= 0 && Time.frameCount != lastRenderUnityFrame + 1)
                throw new InvalidDataException("Offline mixer skipped a Unity frame; logical sample origin is invalid.");
            if (captured)
            {
                if (markedVideoFrame != frames + pending.Count || (lastMarkedUnityFrame >= 0 && Time.frameCount != lastMarkedUnityFrame + 1))
                    throw new InvalidDataException("Video marks must be consecutive and ordered on the mixer clock.");
                var start = ExpectedSampleFrames(renderedFrames, rate);
                if (captureStart < 0) captureStart = start;
                captureEnd = start + ExpectedSampleFrames(1, rate);
                pending.Enqueue(new FrameMark { videoFrame = markedVideoFrame, unityFrame = Time.frameCount, tick = markedTick, alpha = markedAlpha, rawStart = start });
                lastMarkedUnityFrame = Time.frameCount; drainFrames = 0;
            }
            else if (pending.Count > 0 && ++drainFrames > 4)
                throw new InvalidDataException("Offline DSP postroll did not supply the pending video sample boundary.");
            if (floatCount > buffer.Length)
            {
                buffer.Dispose(); buffer = new NativeArray<float>(floatCount, Allocator.Persistent);
                rawPcm = new byte[checked(floatCount * 2)];
            }
            var samples = buffer.GetSubArray(0, floatCount);
            for (var i = 0; i < samples.Length; i++) samples[i] = float.NaN;
            if (!AudioRenderer.Render(samples)) throw new InvalidOperationException("Unity AudioRenderer.Render failed.");
            for (var i = 0; i < samples.Length; i++)
            {
                var value = samples[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidDataException("Mixer did not overwrite every requested sample with finite PCM.");
                var sample = (short)Math.Round(Math.Max(-1, Math.Min(1, value)) * short.MaxValue);
                rawPcm[i * 2] = (byte)(sample & 255); rawPcm[i * 2 + 1] = (byte)((sample >> 8) & 255);
            }
            raw.Write(rawPcm, 0, floatCount * 2);
            blocks.WriteLine(FormattableString.Invariant($"{renderedFrames},{Time.frameCount},{rawSampleFrames},{count},{ExpectedSampleFrames(renderedFrames + 1, rate)},{channels},{rate}"));
            rawSampleFrames += count; raw.Flush(); blocks.Flush();
            lastRenderUnityFrame = Time.frameCount; renderedFrames++;
            if (!captured) discardedFrames++;
            while (pending.Count > 0)
            {
                var mark = pending.Peek();
                if (!CopyPcmWindow(raw, wave, mark.rawStart, pcm, channels)) break;
                for (var i = 0; i < pcm.Length; i += 2)
                {
                    var value = (short)(pcm[i] | pcm[i + 1] << 8) / (double)short.MaxValue;
                    peak = Math.Max(peak, Math.Abs(value)); sumSquares += value * value;
                    if (value != 0) nonzeroSamples++;
                }
                index.WriteLine(FormattableString.Invariant($"{mark.videoFrame},{mark.unityFrame},{mark.tick},{mark.alpha:R},{sampleFrames},{ExpectedSampleFrames(1, rate)},{rate},{channels},{mark.rawStart}"));
                sampleFrames += ExpectedSampleFrames(1, rate); frames++; pending.Dequeue();
            }
        }
        public static bool CopyPcmWindow(Stream raw, Stream destination, long startSampleFrame, byte[] frame, int channelCount)
        {
            if (startSampleFrame < 0 || channelCount < 1 || channelCount > 2 || frame.Length == 0 || frame.Length % (channelCount * 2) != 0)
                throw new ArgumentException("Invalid PCM window.");
            var start = checked(startSampleFrame * channelCount * 2);
            if (raw.Length - start < frame.Length) return false;
            var saved = raw.Position;
            try
            {
                raw.Position = start;
                var read = 0;
                while (read < frame.Length)
                {
                    var count = raw.Read(frame, read, frame.Length - read);
                    if (count == 0) throw new EndOfStreamException("Incomplete DSP sample window.");
                    read += count;
                }
                destination.Write(frame, 0, frame.Length);
                return true;
            }
            finally { raw.Position = saved; }
        }
        public void Finish(int expectedFrames, bool verified)
        {
            wave.Flush(); index.Flush(); WriteWaveHeader(wave, rate, channels, sampleFrames); wave.Flush();
            var complete = verified && Error == null && pending.Count == 0 && frames == expectedFrames && sampleFrames == ExpectedSampleFrames(expectedFrames, rate) && nonzeroSamples > 0;
            File.WriteAllText(Path.Combine(directory, "offline-audio.json"), JsonUtility.ToJson(new Manifest {
                sampleRate = rate, channels = channels, videoFrames = frames, expectedFrames = expectedFrames,
                logicalSamplesPerFrame = (int)ExpectedSampleFrames(1, rate), rawSampleFrames = rawSampleFrames,
                captureStartRawSampleFrame = captureStart, captureEndRawSampleFrame = captureEnd,
                firstRenderUnityFrame = firstRenderUnityFrame, pendingFrames = pending.Count,
                sampleFrames = sampleFrames, renderedFrames = renderedFrames, discardedFrames = discardedFrames,
                nonzeroSamples = nonzeroSamples, peak = peak, rms = sampleFrames == 0 ? 0 : Math.Sqrt(sumSquares / (sampleFrames * channels)),
                complete = complete, error = Error?.ToString()
            }, true));
            if (!complete) throw new InvalidDataException("Offline audio incomplete, silent or not sample-locked to video.", Error);
        }
        [Serializable] sealed class Manifest
        {
            public string contract = "unity-audiorenderer-offline-pcm16-v2";
            public string source = "Unity main mixer via AudioRenderer; no microphone, OS recording or substituted soundtrack";
            public string timeline = "Every reported DSP block is retained in raw PCM; queued video marks select exact logical sample windows through a file-backed FIFO, including postroll. No padding, resampling or substituted samples. WAV is the contiguous captureStartRawSampleFrame..captureEndRawSampleFrame slice of raw PCM.";
            public int sampleRate, channels, videoFrames, expectedFrames, renderedFrames, discardedFrames;
            public long sampleFrames, nonzeroSamples, rawSampleFrames, captureStartRawSampleFrame, captureEndRawSampleFrame;
            public int logicalSamplesPerFrame, firstRenderUnityFrame, pendingFrames;
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
            index?.Dispose(); diagnostics?.Dispose(); raw?.Dispose(); blocks?.Dispose(); if (buffer.IsCreated) buffer.Dispose();
        }
        void OnDestroy() => Dispose();
    }
}
