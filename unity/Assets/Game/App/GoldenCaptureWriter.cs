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
        public double CapturedTimestamp, ReadbackTimestamp;
        public bool Battle, InitialRun;
        public GoldenCaptureFrame(int bytes) { Pixels = new byte[bytes]; }
    }

    public sealed class GoldenCaptureWriter
    {
        readonly GoldenCaptureFrame[] slots;
        readonly string folder;
        readonly int width, height;
        readonly bool flipVertically;
        readonly byte[] rowScratch;
        readonly Thread thread;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool completing, completed, prepared;
        public bool Prepared => prepared;
        string error;
        int written;
        long allocatedBytes = -1;
        public bool AllocationCounterAvailable { get; private set; }
        public bool Completed => completed;
        public string Error => error;
        public int Written => Volatile.Read(ref written);
        public long AllocatedBytes => Interlocked.Read(ref allocatedBytes);
        public bool BattleWritten { get; private set; }
        public bool InitialRunWritten { get; private set; }

        public GoldenCaptureWriter(string output, int pixelWidth, int pixelHeight, GoldenCaptureFrame[] frames, bool flipReadbackRows = false)
        {
            folder = output; width = pixelWidth; height = pixelHeight; slots = frames;
            flipVertically = flipReadbackRows; rowScratch = new byte[width * 4];
            thread = new Thread(Write) { IsBackground = true, Name = "Golden capture image writer" };
            thread.Start();
        }
        public void Notify() => wake.Set();
        public void Complete() { completing = true; wake.Set(); }
        public bool Join(int milliseconds) => thread.Join(milliseconds);

        public static void FlipRows(byte[] pixels, int rowBytes, int rows, byte[] scratch)
        {
            if (rowBytes <= 0 || rows <= 0 || pixels.Length != rowBytes * rows || scratch.Length < rowBytes) throw new ArgumentException("Invalid frame row storage.");
            for (var row = 0; row < rows / 2; row++)
            {
                var top = row * rowBytes; var bottom = (rows - row - 1) * rowBytes;
                Buffer.BlockCopy(pixels, top, scratch, 0, rowBytes);
                Buffer.BlockCopy(pixels, bottom, pixels, top, rowBytes);
                Buffer.BlockCopy(scratch, 0, pixels, bottom, rowBytes);
            }
        }

        void Write()
        {
            AllocationCounterAvailable = AllocationCounterProbe.CurrentThreadAvailable();
            var allocationStart = AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "frames"));
                using var concat = new StreamWriter(Path.Combine(folder, "frames.ffconcat"));
                using var csv = new StreamWriter(Path.Combine(folder, "frames.csv"));
                using var timing = new StreamWriter(Path.Combine(folder, "capture-pipeline.csv"));
                timing.WriteLine("frame,readback_ms,ready_wait_ms,flip_ms,jpeg_ms,file_write_ms,screenshot_ms,writer_total_ms");
                concat.WriteLine("ffconcat version 1.0");
                csv.WriteLine("frame,wall_seconds,tick,card_pause,render_frame,dsp_seconds");
                var warmup = ImageConversion.EncodeArrayToJPG(new byte[width * height * 4], GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, 0, 92);
                GC.KeepAlive(warmup); prepared = true;
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
                    var started = Timestamp();
                    var name = "frames/frame-" + slot.Sequence.ToString("D6", CultureInfo.InvariantCulture) + ".jpg";
                    if (flipVertically) FlipRows(slot.Pixels, width * 4, height, rowScratch);
                    var flipped = Timestamp();
                    var jpg = ImageConversion.EncodeArrayToJPG(slot.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, 0, 92);
                    var encoded = Timestamp();
                    File.WriteAllBytes(Path.Combine(folder, name), jpg);
                    var saved = Timestamp();
                    if (previousName != null)
                    {
                        concat.WriteLine("file '" + previousName + "'");
                        concat.WriteLine("option framerate 1000000");
                        concat.WriteLine("duration " + (slot.WallSeconds - previousTime).ToString("F9", CultureInfo.InvariantCulture));
                    }
                    csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F9},{2},{3},{4},{5:F9}", slot.Sequence, slot.WallSeconds, slot.Tick, slot.CardPause, slot.RenderFrame, slot.DspSeconds));
                    var screenshotStart = Timestamp();
                    if (slot.InitialRun)
                    {
                        File.WriteAllBytes(Path.Combine(folder, "02-native-run.png"), ImageConversion.EncodeArrayToPNG(slot.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height));
                        InitialRunWritten = true;
                    }
                    if (slot.Battle)
                    {
                        File.WriteAllBytes(Path.Combine(folder, "05-native-golden-battle.png"), ImageConversion.EncodeArrayToPNG(slot.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height));
                        BattleWritten = true;
                    }
                    var finished = Timestamp();
                    timing.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6}",
                        slot.Sequence, (slot.ReadbackTimestamp - slot.CapturedTimestamp) * 1000,
                        (started - slot.ReadbackTimestamp) * 1000, (flipped - started) * 1000,
                        (encoded - flipped) * 1000, (saved - encoded) * 1000,
                        (finished - screenshotStart) * 1000, (finished - started) * 1000));
                    previousName = name; previousTime = slot.WallSeconds;
                    Volatile.Write(ref slot.State, 0);
                    Interlocked.Increment(ref written);
                    Interlocked.Exchange(ref allocatedBytes, AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : -1);
                }
                if (previousName != null)
                {
                    // Last packet has no following presentation timestamp. Never duplicate a frame.
                    concat.WriteLine("file '" + previousName + "'");
                    concat.WriteLine("option framerate 1000000");
                }
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally { Interlocked.Exchange(ref allocatedBytes, AllocationCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : -1); completed = true; }
        }

        static double Timestamp() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    }
}
