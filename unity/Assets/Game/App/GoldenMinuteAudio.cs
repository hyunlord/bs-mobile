using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Game.App
{
    // Real listener mix, copied into a preallocated SPSC ring. No audio-thread file writes.
    public sealed class GoldenMinuteAudio : MonoBehaviour
    {
        const int RingSize = 32, MaximumSamplesPerBlock = 32768;
        sealed class Block
        {
            public readonly byte[] Bytes = new byte[MaximumSamplesPerBlock * 2];
            public int Length, Ready;
        }
        readonly Block[] blocks = new Block[RingSize];
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        Thread worker;
        int channels, sampleRate, produced, consumed, activeCallbacks, dropped;
        long sampleCount, nonzeroSamples, writerAllocated = -1;
        public bool AllocationCounterAvailable { get; private set; }
        double videoDsp, firstAudioDsp;
        volatile bool armed, completing, completed;
        string error;
        AudioListener originalListener, captureListener;
        bool originalEnabled;
        public long NonzeroSampleCount => Interlocked.Read(ref nonzeroSamples);
        public long SampleCount => Interlocked.Read(ref sampleCount);
        public long WriterAllocatedBytes => Interlocked.Read(ref writerAllocated);
        public int DroppedBlocks => Volatile.Read(ref dropped);
        public bool Completed => completed;
        public string Error => error;
        public double VideoOffsetSeconds => firstAudioDsp - videoDsp;

        public static GoldenMinuteAudio BeginMixedOutput(AudioListener listener, string path)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var owner = new GameObject("Golden minute mixed output");
            owner.transform.SetParent(listener.transform, false);
            var capture = owner.AddComponent<GoldenMinuteAudio>();
            capture.originalListener = listener; capture.originalEnabled = listener.enabled;
            listener.enabled = false;
            try
            {
                capture.captureListener = owner.AddComponent<AudioListener>();
                capture.Begin(path); return capture;
            }
            catch { capture.RestoreListener(); Destroy(owner); throw; }
        }

        void RestoreListener()
        {
            if (captureListener != null) captureListener.enabled = false;
            if (originalListener != null) originalListener.enabled = originalEnabled;
        }

        public void Begin(string path)
        {
            sampleRate = AudioSettings.outputSampleRate;
            for (var index = 0; index < RingSize; index++) blocks[index] = new Block();
            worker = new Thread(() => Write(path)) { IsBackground = true, Name = "Golden capture PCM writer" };
            worker.Start();
        }

        public void MarkVideoStart(double wallSeconds, double dspSeconds)
        {
            videoDsp = dspSeconds;
            armed = true;
        }

        void OnAudioFilterRead(float[] data, int channelCount)
        {
            if (!armed || completing) return;
            Interlocked.Increment(ref activeCallbacks);
            try
            {
                if (completing) return;
                var block = blocks[produced % RingSize];
                if (data.Length > MaximumSamplesPerBlock || Volatile.Read(ref block.Ready) != 0 || channels != 0 && channels != channelCount)
                {
                    Interlocked.Increment(ref dropped); return;
                }
                channels = channelCount;
                if (produced == 0) firstAudioDsp = AudioSettings.dspTime;
                long audible = 0;
                for (var index = 0; index < data.Length; index++)
                {
                    var sample = (short)(Math.Max(-1, Math.Min(1, data[index])) * short.MaxValue);
                    if (sample != 0) audible++;
                    block.Bytes[index * 2] = (byte)sample; block.Bytes[index * 2 + 1] = (byte)(sample >> 8);
                }
                block.Length = data.Length * 2;
                Interlocked.Add(ref nonzeroSamples, audible);
                Volatile.Write(ref block.Ready, 1); produced++; wake.Set();
            }
            finally { Interlocked.Decrement(ref activeCallbacks); }
        }

        void Write(string path)
        {
            AllocationCounterAvailable = AllocationCounterProbe.CurrentThreadAvailable();
            var allocationStart = AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                stream.Write(new byte[44], 0, 44);
                while (true)
                {
                    var block = blocks[consumed % RingSize];
                    if (Volatile.Read(ref block.Ready) == 0)
                    {
                        if (completing && Volatile.Read(ref activeCallbacks) == 0) break;
                        wake.WaitOne(10); continue;
                    }
                    stream.Write(block.Bytes, 0, block.Length);
                    Interlocked.Add(ref sampleCount, block.Length / 2);
                    Volatile.Write(ref block.Ready, 0); consumed++;
                }
                stream.Position = 0;
                using var header = new BinaryWriter(stream, Encoding.ASCII, true);
                var length = checked((int)(SampleCount * 2)); var count = Math.Max(1, channels);
                header.Write(Encoding.ASCII.GetBytes("RIFF")); header.Write(36 + length);
                header.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); header.Write(16); header.Write((short)1);
                header.Write((short)count); header.Write(sampleRate); header.Write(sampleRate * count * 2);
                header.Write((short)(count * 2)); header.Write((short)16);
                header.Write(Encoding.ASCII.GetBytes("data")); header.Write(length);
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally { Interlocked.Exchange(ref writerAllocated, AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : -1); completed = true; }
        }

        public void StopAudio() { RestoreListener(); completing = true; wake.Set(); }
        public bool Join(int milliseconds) => worker == null || worker.Join(milliseconds);
        void OnApplicationQuit() { StopAudio(); Join(5000); }
        void OnDestroy() { StopAudio(); Join(5000); }
    }
}
