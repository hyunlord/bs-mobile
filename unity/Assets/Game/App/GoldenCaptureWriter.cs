using System;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Game.App
{
    public sealed class GoldenCaptureFrame
    {
        public readonly byte[] Pixels;
        // 0 free, 1 GPU pending, 2 ready, 3 writing. Volatile publication transfers ownership.
        public int State, Sequence, Tick, RenderFrame, CardPause;
        public double WallSeconds, DspSeconds;
        public bool Battle;
        public GoldenCaptureFrame(int bytes) { Pixels = new byte[bytes]; }
    }

    public sealed class GoldenCaptureWriter
    {
        readonly GoldenCaptureFrame[] slots;
        readonly string folder;
        readonly int width, height;
        readonly Thread thread;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool completing, completed;
        string error;
        int written;
        long allocatedBytes;
        public bool Completed => completed;
        public string Error => error;
        public int Written => Volatile.Read(ref written);
        public long AllocatedBytes => Interlocked.Read(ref allocatedBytes);
        public bool BattleWritten { get; private set; }

        public GoldenCaptureWriter(string output, int pixelWidth, int pixelHeight, GoldenCaptureFrame[] frames)
        {
            folder = output; width = pixelWidth; height = pixelHeight; slots = frames;
            thread = new Thread(Write) { IsBackground = true, Name = "Golden capture image writer" };
            thread.Start();
        }
        public void Notify() => wake.Set();
        public void Complete() { completing = true; wake.Set(); }
        public bool Join(int milliseconds) => thread.Join(milliseconds);

        void Write()
        {
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "frames"));
                using var concat = new StreamWriter(Path.Combine(folder, "frames.ffconcat"));
                using var csv = new StreamWriter(Path.Combine(folder, "frames.csv"));
                concat.WriteLine("ffconcat version 1.0");
                csv.WriteLine("frame,wall_seconds,tick,card_pause,render_frame,dsp_seconds");
                string previousName = null;
                double previousTime = 0;
                while (true)
                {
                    var slot = slots[written % slots.Length];
                    if (Volatile.Read(ref slot.State) != 2)
                    {
                        if (completing) break;
                        wake.WaitOne(10); continue;
                    }
                    Volatile.Write(ref slot.State, 3);
                    var name = "frames/frame-" + slot.Sequence.ToString("D6", CultureInfo.InvariantCulture) + ".jpg";
                    var jpg = ImageConversion.EncodeArrayToJPG(slot.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, 0, 92);
                    File.WriteAllBytes(Path.Combine(folder, name), jpg);
                    if (previousName != null)
                    {
                        concat.WriteLine("file '" + previousName + "'");
                        concat.WriteLine("option framerate 1000000");
                        concat.WriteLine("duration " + (slot.WallSeconds - previousTime).ToString("F9", CultureInfo.InvariantCulture));
                    }
                    csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F9},{2},{3},{4},{5:F9}", slot.Sequence, slot.WallSeconds, slot.Tick, slot.CardPause, slot.RenderFrame, slot.DspSeconds));
                    if (slot.Battle)
                    {
                        File.WriteAllBytes(Path.Combine(folder, "05-native-golden-battle.png"), ImageConversion.EncodeArrayToPNG(slot.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height));
                        BattleWritten = true;
                    }
                    previousName = name; previousTime = slot.WallSeconds;
                    Volatile.Write(ref slot.State, 0);
                    Interlocked.Increment(ref written);
                    Interlocked.Exchange(ref allocatedBytes, GC.GetAllocatedBytesForCurrentThread() - allocationStart);
                }
                if (previousName != null)
                {
                    // Last packet has no following presentation timestamp. Never duplicate a frame.
                    concat.WriteLine("file '" + previousName + "'");
                    concat.WriteLine("option framerate 1000000");
                }
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally { Interlocked.Exchange(ref allocatedBytes, GC.GetAllocatedBytesForCurrentThread() - allocationStart); completed = true; }
        }
    }
}
