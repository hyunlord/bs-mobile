using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.App
{
    // Listener filter records the actual mixed output, never substitutes authored audio.
    public sealed class GoldenMinuteAudio : MonoBehaviour
    {
        readonly object gate = new object();
        FileStream stream;
        byte[] bytes = Array.Empty<byte>();
        int channels, sampleRate;
        AudioListener originalListener, captureListener;
        bool originalEnabled;
        public long NonzeroSampleCount { get; private set; }
        public long SampleCount { get; private set; }

        public static GoldenMinuteAudio BeginMixedOutput(AudioListener listener, string path)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            var owner = new GameObject("Golden minute mixed output");
            owner.transform.SetParent(listener.transform, false);
            var capture = owner.AddComponent<GoldenMinuteAudio>();
            capture.originalListener = listener;
            capture.originalEnabled = listener.enabled;
            listener.enabled = false;
            try
            {
                capture.captureListener = owner.AddComponent<AudioListener>();
                capture.Begin(path);
                return capture;
            }
            catch
            {
                capture.RestoreListener();
                Destroy(owner);
                throw;
            }
        }

        void RestoreListener()
        {
            if (captureListener != null) captureListener.enabled = false;
            if (originalListener != null) originalListener.enabled = originalEnabled;
        }

        public void Begin(string path)
        {
            lock (gate)
            {
                sampleRate = AudioSettings.outputSampleRate;
                stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                stream.Write(new byte[44], 0, 44);
            }
        }

        void OnAudioFilterRead(float[] data, int channelCount)
        {
            lock (gate)
            {
                if (stream == null) return;
                channels = channelCount;
                if (bytes.Length != data.Length * 2) bytes = new byte[data.Length * 2];
                for (var index = 0; index < data.Length; index++)
                {
                    var sample = (short)(Math.Max(-1, Math.Min(1, data[index])) * short.MaxValue);
                    if (sample != 0) NonzeroSampleCount++;
                    bytes[index * 2] = (byte)sample; bytes[index * 2 + 1] = (byte)(sample >> 8);
                }
                stream.Write(bytes, 0, bytes.Length);
                SampleCount += data.Length;
            }
        }

        public void StopAudio()
        {
            RestoreListener();
            lock (gate)
            {
                if (stream == null) return;
                try
                {
                    stream.Position = 0;
                    using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
                    var length = checked((int)(SampleCount * 2));
                    var count = Math.Max(1, channels);
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + length);
                    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1);
                    writer.Write((short)count); writer.Write(sampleRate); writer.Write(sampleRate * count * 2);
                    writer.Write((short)(count * 2)); writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(length);
                }
                finally { stream.Dispose(); stream = null; }
            }
        }

        void OnApplicationQuit() => StopAudio();
        void OnDestroy() => StopAudio();
    }
}
