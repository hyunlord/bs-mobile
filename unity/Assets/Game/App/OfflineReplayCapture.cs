using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Game.App
{
    // This writer deliberately blocks after rendering. Its timings are never performance evidence.
    public sealed class OfflineReplayCapture : IDisposable
    {
        public const int FramesPerSecond = 60;
        public const double FrameSeconds = 1d / FramesPerSecond;
        readonly string output;
        readonly int previousCaptureRate, previousTarget, previousVsync;
        readonly bool previousBackground;
        readonly RenderTexture target;
        readonly Texture2D pixels;
        readonly bool flipReadbackRows;
        readonly byte[] readbackPixels, rowScratch;
        readonly WaitForEndOfFrame endOfFrame = new WaitForEndOfFrame();
        readonly StreamWriter index;
        bool disposed;
        int lastUnityFrame = -1;
        public int Frames { get; private set; }
        public bool Pending { get; private set; }
        public Exception Error { get; private set; }
        public static bool Requested(string[] args)
        {
            var first = Array.IndexOf(args, "--offline-replay-capture");
            if (first < 0) return false;
            if (first != Array.LastIndexOf(args, "--offline-replay-capture") || (Array.IndexOf(args, "--crowd-replay-benchmark") < 0 && CameraScaleComparison.Parse(args) == null))
                throw new ArgumentException("Offline capture requires one flag and the fixed crowd replay or explicit frozen C05 camera review.");
            foreach (var forbidden in new[] { "--wave-benchmark-profile", "--wave-benchmark-core-diagnostics", "--wave-benchmark-capture", "--wave-benchmark-mode" })
                if (Array.IndexOf(args, forbidden) >= 0) throw new ArgumentException("Offline review capture cannot collect performance evidence: " + forbidden);
            return true;
        }
        public static int ExpectedFrames(int ticks, int tickRate)
        {
            if (ticks < 0 || tickRate <= 0 || FramesPerSecond % tickRate != 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            return checked(ticks * (FramesPerSecond / tickRate));
        }
        public OfflineReplayCapture(string directory, int width, int height)
        {
            output = Path.Combine(directory, "offline-frames");
            Directory.CreateDirectory(output);
            index = new StreamWriter(Path.Combine(directory, "offline-frame-index.csv"));
            index.WriteLine("index,unityFrame,tick,alpha,seconds");
            target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            target.Create();
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            flipReadbackRows = SystemInfo.graphicsUVStartsAtTop;
            if (flipReadbackRows)
            {
                readbackPixels = new byte[checked(width * height * 3)];
                rowScratch = new byte[checked(width * 3)];
            }
            previousCaptureRate = Time.captureFramerate;
            previousTarget = Application.targetFrameRate;
            previousVsync = QualitySettings.vSyncCount;
            previousBackground = Application.runInBackground;
            Time.captureFramerate = FramesPerSecond;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
        }
        public IEnumerator SaveRenderedFrame(int tick, float alpha)
        {
            if (Pending) throw new InvalidOperationException("An offline frame is still pending.");
            Pending = true;
            yield return endOfFrame;
            var prior = RenderTexture.active;
            try
            {
                if (disposed || Time.frameCount == lastUnityFrame) throw new InvalidOperationException("Duplicate or disposed offline frame.");
                if (Frames > 0 && Time.frameCount != lastUnityFrame + 1) throw new InvalidOperationException("A rendered offline frame was skipped.");
                ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                // Match the realtime writer: Metal screen-target rows run top-first.
                // ReadPixels does not normalize this captured render target for JPEG.
                if (flipReadbackRows)
                {
                    pixels.GetRawTextureData<byte>().CopyTo(readbackPixels);
                    GoldenCaptureWriter.FlipRows(readbackPixels, target.width * 3, target.height, rowScratch);
                    pixels.LoadRawTextureData(readbackPixels);
                }
                pixels.Apply(false, false);
                File.WriteAllBytes(Path.Combine(output, $"frame-{Frames:D6}.jpg"), pixels.EncodeToJPG(95));
                index.WriteLine(FormattableString.Invariant($"{Frames},{Time.frameCount},{tick},{alpha:R},{Frames * FrameSeconds:R}"));
                lastUnityFrame = Time.frameCount;
                Frames++;
            }
            catch (Exception exception) { Error = exception; }
            finally { RenderTexture.active = prior; Pending = false; }
        }
        public void Finish(int expected, bool verified)
        {
            index.Flush();
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "offline-capture.json"), JsonUtility.ToJson(new Manifest
            {
                frames = Frames, expectedFrames = expected, complete = verified && Error == null && !Pending && Frames == expected,
                readbackRowsFlipped = flipReadbackRows
            }, true));
            if (Error != null) throw new IOException("Offline image capture failed.", Error);
            if (Pending || Frames != expected) throw new InvalidDataException("Offline capture frame count mismatch.");
        }
        [Serializable] sealed class Manifest
        {
            public string label = "오프라인 렌더 · 60fps · 무음";
            public string contract = "Fixed 1/60 presentation and original 30Hz replay commands; contiguous end-of-frame JPEGs. Not real-time or frame-pacing evidence. Encode frame-%06d.jpg at 60fps without frame resampling.";
            public int fps = FramesPerSecond, frames, expectedFrames;
            public bool complete, readbackRowsFlipped, performanceEligible = false;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            index.Dispose();
            target.Release();
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(pixels);
            Time.captureFramerate = previousCaptureRate;
            Application.targetFrameRate = previousTarget;
            QualitySettings.vSyncCount = previousVsync;
            Application.runInBackground = previousBackground;
        }
    }
}
