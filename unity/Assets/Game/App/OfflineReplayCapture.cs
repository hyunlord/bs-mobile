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
        readonly OfflineReplayAudio audio;
        Exception captureError;
        bool disposed;
        int lastUnityFrame = -1;
        public int Frames { get; private set; }
        public bool Pending { get; private set; }
        public bool AudioPending => audio != null && audio.PendingFrames > 0;
        public Exception Error => captureError ?? audio?.Error;
        public static bool Requested(string[] args)
        {
            OfflineReplayAudio.Requested(args);
            var first = Array.IndexOf(args, "--offline-replay-capture");
            if (first < 0) return false;
            if (first != Array.LastIndexOf(args, "--offline-replay-capture") || (Array.IndexOf(args, "--crowd-replay-benchmark") < 0 && Array.IndexOf(args, "--recorded-replay-window") < 0 && CameraScaleComparison.Parse(args) == null))
                throw new ArgumentException("Offline capture requires one flag and the fixed crowd replay, a recorded replay window or explicit frozen C05 camera review.");
            foreach (var forbidden in new[] { "--wave-benchmark-profile", "--wave-benchmark-core-diagnostics", "--wave-benchmark-capture", "--wave-benchmark-mode" })
                if (Array.IndexOf(args, forbidden) >= 0) throw new ArgumentException("Offline review capture cannot collect performance evidence: " + forbidden);
            return true;
        }
        public static int ExpectedFrames(int ticks, int tickRate)
        {
            if (ticks < 0 || tickRate <= 0 || FramesPerSecond % tickRate != 0) throw new ArgumentOutOfRangeException(nameof(tickRate));
            return checked(ticks * (FramesPerSecond / tickRate));
        }
        // Review measurement only: also keep every frame as a lossless PNG in offline-frames-png (the JPEGs stay as they are for
        // video and sheets), so pixel measurements are free of JPEG loss.
        public const string LosslessFlag = "--offline-replay-lossless";
        readonly string losslessOutput;
        public OfflineReplayCapture(string directory, int width, int height)
        {
            output = Path.Combine(directory, "offline-frames");
            Directory.CreateDirectory(output);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), LosslessFlag) >= 0)
            {
                losslessOutput = Path.Combine(directory, "offline-frames-png");
                Directory.CreateDirectory(losslessOutput);
            }
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
            if (OfflineReplayAudio.Requested(Environment.GetCommandLineArgs()))
            {
                audio = new GameObject("Offline replay audio").AddComponent<OfflineReplayAudio>();
                try { audio.Initialize(directory); }
                catch { audio.Dispose(); UnityEngine.Object.Destroy(audio.gameObject); Dispose(); throw; }
            }
        }
        public IEnumerator SaveRenderedFrame(int tick, float alpha)
        {
            if (Pending) throw new InvalidOperationException("An offline frame is still pending.");
            Pending = true;
            audio?.MarkVideoFrame(Frames, tick, alpha);
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
                if (losslessOutput != null) File.WriteAllBytes(Path.Combine(losslessOutput, $"frame-{Frames:D6}.png"), pixels.EncodeToPNG());
                index.WriteLine(FormattableString.Invariant($"{Frames},{Time.frameCount},{tick},{alpha:R},{Frames * FrameSeconds:R}"));
                lastUnityFrame = Time.frameCount;
                Frames++;
            }
            catch (Exception exception) { captureError = exception; }
            finally { RenderTexture.active = prior; Pending = false; }
        }
        public void Finish(int expected, bool verified)
        {
            index.Flush();
            audio?.Finish(expected, verified);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "offline-capture.json"), JsonUtility.ToJson(new Manifest
            {
                frames = Frames, expectedFrames = expected, complete = verified && Error == null && !Pending && Frames == expected,
                readbackRowsFlipped = flipReadbackRows, audioIncluded = audio != null,
                label = audio == null ? "오프라인 렌더 · 60fps · 무음" : "오프라인 렌더 · 60fps · 게임 소리 포함"
            }, true));
            if (Error != null) throw new IOException("Offline image capture failed.", Error);
            if (Pending || Frames != expected) throw new InvalidDataException("Offline capture frame count mismatch.");
        }
        [Serializable] sealed class Manifest
        {
            public string label = "오프라인 렌더 · 60fps · 무음";
            public string contract = "Fixed 1/60 presentation and original 30Hz replay commands; contiguous end-of-frame JPEGs. Not real-time or frame-pacing evidence. Encode frame-%06d.jpg at 60fps without frame resampling.";
            public int fps = FramesPerSecond, frames, expectedFrames;
            public bool complete, readbackRowsFlipped, audioIncluded, performanceEligible = false;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (audio != null) { audio.Dispose(); UnityEngine.Object.Destroy(audio.gameObject); }
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
