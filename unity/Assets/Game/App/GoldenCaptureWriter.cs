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
        // 0 free, 1 GPU pending, 2 ready, 3 encoding, 4 encoded awaiting ordered commit. Volatile publication transfers ownership.
        public int State, Sequence, Tick, RenderFrame, CardPause;
        public double WallSeconds, DspSeconds;
        public double CapturedTimestamp, ReadbackTimestamp;
        public bool Battle, InitialRun;
        public GoldenCaptureFrame(int bytes) { Pixels = new byte[bytes]; }
    }

    public sealed class GoldenCaptureWriter
    {
        const int EncoderCount = 2;
        readonly GoldenCaptureFrame[] slots;
        readonly EncodedFrame[] encoded;
        readonly string folder;
        readonly int width, height;
        readonly bool flipVertically;
        readonly Func<byte[], byte[]> encodeJpeg;
        readonly Thread thread;
        readonly object claimLock = new object();
        readonly Thread[] encoders = new Thread[EncoderCount];
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly AutoResetEvent[] encoderWake = { new AutoResetEvent(false), new AutoResetEvent(false) };
        readonly long[] allocations = new long[EncoderCount + 1];
        volatile bool completing, completed, prepared;
        int encodersReady, encodersRemaining = EncoderCount, nextEncode, written;
        string error;
        public bool Prepared => prepared;
        public bool AllocationCounterAvailable { get; private set; }
        public bool Completed => completed;
        public string Error => Volatile.Read(ref error);
        public int Written => Volatile.Read(ref written);
        public long AllocatedBytes
        {
            get
            {
                if (!AllocationCounterAvailable) return -1;
                long total = 0;
                for (var i = 0; i < allocations.Length; i++) total += Interlocked.Read(ref allocations[i]);
                return total;
            }
        }
        public bool BattleWritten { get; private set; }
        public bool InitialRunWritten { get; private set; }

        sealed class EncodedFrame
        {
            public byte[] Jpeg, Screenshot;
            public double Started, Flipped, JpegFinished, Finished;
            public int Encoder;
        }

        // Optional encoder seam permits deterministic ownership/order tests without timing the JPEG library.
        public GoldenCaptureWriter(string output, int pixelWidth, int pixelHeight, GoldenCaptureFrame[] frames, bool flipReadbackRows = false, Func<byte[], byte[]> jpegEncoder = null)
        {
            if (frames == null || frames.Length == 0 || pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentException("Capture requires bounded frame storage and positive dimensions.");
            folder = output; width = pixelWidth; height = pixelHeight; slots = frames;
            flipVertically = flipReadbackRows; encodeJpeg = jpegEncoder ?? EncodeJpeg;
            encoded = new EncodedFrame[slots.Length];
            for (var i = 0; i < encoded.Length; i++) encoded[i] = new EncodedFrame();
            thread = new Thread(Write) { IsBackground = true, Name = "Golden capture ordered writer" };
            thread.Start();
        }
        byte[] EncodeJpeg(byte[] pixels) => ImageConversion.EncodeArrayToJPG(pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height, 0, 92);
        public void Notify()
        {
            wake.Set();
            for (var i = 0; i < encoderWake.Length; i++) encoderWake[i].Set();
        }
        public void Complete() { completing = true; Notify(); }
        public bool Join(int milliseconds) => thread.Join(milliseconds);
        void Fail(Exception exception) { Interlocked.CompareExchange(ref error, exception.ToString(), null); completing = true; Notify(); }

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

        public static void MakeScreenOpaque(byte[] pixels)
        {
            if (pixels == null || pixels.Length % 4 != 0) throw new ArgumentException("Expected RGBA screen pixels.");
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }

        int ClaimReadyFrame()
        {
            // Publish exclusive slot ownership before exposing the next sequence, even after a full ring wrap.
            lock (claimLock)
            {
                var index = nextEncode % slots.Length;
                var slot = slots[index];
                if (Volatile.Read(ref slot.State) != 2) return -1;
                if (slot.Sequence != nextEncode) throw new InvalidDataException("Capture encode sequence mismatch: expected " + nextEncode + ", observed " + slot.Sequence);
                Volatile.Write(ref slot.State, 3);
                nextEncode++;
                return index;
            }
        }

        void Encode(int worker)
        {
            var available = AllocationCounterProbe.CurrentThreadAvailable();
            if (!available) AllocationCounterAvailable = false;
            var allocationStart = available ? GC.GetAllocatedBytesForCurrentThread() : -1;
            try
            {
                var scratch = new byte[width * 4];
                // Prepare each native encoder thread before permitting gameplay capture.
                var warmup = EncodeJpeg(new byte[checked(width * height * 4)]);
                GC.KeepAlive(warmup);
                Interlocked.Increment(ref encodersReady); wake.Set();
                while (Error == null)
                {
                    var index = ClaimReadyFrame();
                    if (index < 0)
                    {
                        if (completing) break;
                        encoderWake[worker].WaitOne(10); continue;
                    }
                    var slot = slots[index];
                    var result = encoded[index]; result.Encoder = worker; result.Started = Timestamp();
                    if (flipVertically) FlipRows(slot.Pixels, width * 4, height, scratch);
                    result.Flipped = Timestamp();
                    result.Jpeg = encodeJpeg(slot.Pixels);
                    if (result.Jpeg == null) throw new InvalidDataException("JPEG encoder returned no bytes.");
                    result.JpegFinished = Timestamp();
                    if (slot.InitialRun || slot.Battle)
                    {
                        // The display is opaque; framebuffer alpha is blend bookkeeping, not screenshot transparency.
                        MakeScreenOpaque(slot.Pixels);
                        result.Screenshot = ImageConversion.EncodeArrayToPNG(slot.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)width, (uint)height);
                    }
                    result.Finished = Timestamp();
                    Interlocked.Exchange(ref allocations[worker], available ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : 0);
                    // Publication owns both pixel and encoded buffers until the ordered writer frees this slot.
                    Volatile.Write(ref slot.State, 4); wake.Set();
                }
            }
            catch (Exception exception) { Fail(exception); }
            finally
            {
                Interlocked.Exchange(ref allocations[worker], available ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : 0);
                Interlocked.Decrement(ref encodersRemaining); wake.Set();
            }
        }

        void Write()
        {
            AllocationCounterAvailable = AllocationCounterProbe.CurrentThreadAvailable();
            var writerCounterAvailable = AllocationCounterAvailable;
            var allocationStart = writerCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            try
            {
                Directory.CreateDirectory(Path.Combine(folder, "frames"));
                using var concat = new StreamWriter(Path.Combine(folder, "frames.ffconcat"));
                using var csv = new StreamWriter(Path.Combine(folder, "frames.csv"));
                using var timing = new StreamWriter(Path.Combine(folder, "capture-pipeline.csv"));
                timing.WriteLine("frame,readback_ms,ready_wait_ms,flip_ms,jpeg_ms,file_write_ms,screenshot_ms,writer_total_ms,ordered_wait_ms,encoder");
                concat.WriteLine("ffconcat version 1.0");
                csv.WriteLine("frame,wall_seconds,tick,card_pause,render_frame,dsp_seconds");
                for (var i = 0; i < EncoderCount; i++)
                {
                    var worker = i;
                    encoders[i] = new Thread(() => Encode(worker)) { IsBackground = true, Name = "Golden JPEG encoder " + worker };
                    encoders[i].Start();
                }
                string previousName = null;
                double previousTime = 0;
                while (Error == null)
                {
                    if (Volatile.Read(ref encodersReady) == EncoderCount) prepared = true;
                    var index = written % slots.Length; var slot = slots[index];
                    if (Volatile.Read(ref slot.State) != 4)
                    {
                        if (completing && Volatile.Read(ref encodersRemaining) == 0)
                        {
                            foreach (var pending in slots) if (Volatile.Read(ref pending.State) != 0) throw new InvalidDataException("Capture ended with an uncommitted sequence gap.");
                            break;
                        }
                        wake.WaitOne(10); continue;
                    }
                    if (slot.Sequence != written) throw new InvalidDataException("Capture commit sequence mismatch.");
                    var result = encoded[index]; var commitStarted = Timestamp();
                    var name = "frames/frame-" + slot.Sequence.ToString("D6", CultureInfo.InvariantCulture) + ".jpg";
                    File.WriteAllBytes(Path.Combine(folder, name), result.Jpeg);
                    var saved = Timestamp();
                    if (previousName != null)
                    {
                        concat.WriteLine("file '" + previousName + "'");
                        concat.WriteLine("option framerate 1000000");
                        concat.WriteLine("duration " + (slot.WallSeconds - previousTime).ToString("F9", CultureInfo.InvariantCulture));
                    }
                    csv.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F9},{2},{3},{4},{5:F9}", slot.Sequence, slot.WallSeconds, slot.Tick, slot.CardPause, slot.RenderFrame, slot.DspSeconds));
                    var screenshotStart = Timestamp();
                    if (slot.InitialRun) { File.WriteAllBytes(Path.Combine(folder, "02-native-run.png"), result.Screenshot); InitialRunWritten = true; }
                    if (slot.Battle) { File.WriteAllBytes(Path.Combine(folder, "05-native-golden-battle.png"), result.Screenshot); BattleWritten = true; }
                    var finished = Timestamp();
                    timing.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F6},{9}",
                        slot.Sequence, (slot.ReadbackTimestamp - slot.CapturedTimestamp) * 1000,
                        (result.Started - slot.ReadbackTimestamp) * 1000, (result.Flipped - result.Started) * 1000,
                        (result.JpegFinished - result.Flipped) * 1000, (saved - commitStarted) * 1000,
                        (result.Finished - result.JpegFinished + finished - screenshotStart) * 1000,
                        (finished - result.Started) * 1000, (commitStarted - result.Finished) * 1000, result.Encoder));
                    previousName = name; previousTime = slot.WallSeconds;
                    result.Jpeg = result.Screenshot = null;
                    Volatile.Write(ref slot.State, 0);
                    Interlocked.Increment(ref written);
                    Interlocked.Exchange(ref allocations[EncoderCount], writerCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : 0);
                }
                if (previousName != null)
                {
                    // Last packet has no following presentation timestamp. Never duplicate a frame.
                    concat.WriteLine("file '" + previousName + "'");
                    concat.WriteLine("option framerate 1000000");
                }
            }
            catch (Exception exception) { Fail(exception); }
            finally
            {
                completing = true; Notify();
                foreach (var encoder in encoders) if (encoder != null && encoder.IsAlive) encoder.Join();
                Interlocked.Exchange(ref allocations[EncoderCount], writerCounterAvailable ? GC.GetAllocatedBytesForCurrentThread() - allocationStart : 0);
                completed = true;
            }
        }

        static double Timestamp() => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency;
    }
}
