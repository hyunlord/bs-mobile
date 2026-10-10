using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Game.App.Generated;
using Game.View;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        static partial void LoadFrozenWaveBenchmark(ref ContentCatalog catalog, ref string dataHash);
        bool waveBenchmarkRequested, waveBenchmarkReady, waveBenchmarkEnded, benchmarkCapture;
        ReplayDocument benchmarkReplay;
        string benchmarkOutput, benchmarkReplaySha;
        int benchmarkCommand, benchmarkWarmFrames = 120;
        long benchmarkFrameStart, benchmarkAllocatedStart;
        int benchmarkGcStart;
        BenchmarkFrame benchmarkPending;
        bool benchmarkHasPending;
        readonly List<BenchmarkFrame> benchmarkFrames = new List<BenchmarkFrame>(20000);
        readonly List<BenchmarkTiming> benchmarkTimings = new List<BenchmarkTiming>(20000);
        readonly FrameTiming[] benchmarkTimingBuffer = new FrameTiming[1];
        ulong benchmarkLastTiming;

        struct BenchmarkFrame
        {
            public int frame, tick, enemies, commands, gcCollections;
            public long allocatedBytes;
            public double wallMs, simulationMs, snapshotMs, acceptMs, presentMs, hudMs;
            public bool focused;
        }
        struct BenchmarkTiming { public int observedFrame; public FrameTiming value; }
        [Serializable] sealed class BenchmarkResult
        {
            public string commit, sourceHash, dataHash, replaySha256, stateHash, expectedStateHash, error, graphicsApi, quality, platform;
            public bool sourceDirty, verified, developmentBuild, frameTimingEnabled;
            public int tick, commands, frames, renderTimingSamples, width, height, targetFrameRate, vSyncCount, renderWarmupFrames = 120;
            public string windows = "7200-9900,10200-12900,13200-14400", timingContract = "frames.csv: Update-to-next-Update wall includes rendering and pacing; CPU scopes disjoint. render-timings.csv: delayed FrameTimingManager observations, timestamped separately, not joined to density. Allocation delta is main-thread managed allocation; GC count is process-wide generation 0.";
        }
        void TryStartWaveBenchmark()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--wave-benchmark");
            if (index < 0) return;
            waveBenchmarkRequested = true;
            try
            {
                if (!IsWave || index + 1 >= args.Length || AutoplayCapture.Active != null) throw new ArgumentException("Wave benchmark requires wave-1a, a replay path, and no autoplay capture.");
                var output = Array.IndexOf(args, "--wave-benchmark-output");
                if (output < 0 || output + 1 >= args.Length) throw new ArgumentException("Missing --wave-benchmark-output directory.");
                var destination = Path.GetFullPath(args[output + 1]);
                if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length != 0) throw new IOException("Benchmark output must be empty.");
                Directory.CreateDirectory(destination);
                benchmarkOutput = destination;
                var bytes = File.ReadAllBytes(args[index + 1]);
                using (var sha = SHA256.Create()) benchmarkReplaySha = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (benchmarkReplaySha != "f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5") throw new InvalidDataException("C05 fixture identity mismatch.");
                using (var stream = new MemoryStream(bytes)) benchmarkReplay = ReplayCodec.Read(stream);
                ContentCatalog frozen = null; string hash = null;
                LoadFrozenWaveBenchmark(ref frozen, ref hash);
                if (frozen == null || hash != benchmarkReplay.Header.Options.DataHash) throw new InvalidDataException("Frozen benchmark catalog absent or mismatched; prepare the benchmark build.");
                benchmarkCapture = Array.IndexOf(args, "--wave-benchmark-capture") >= 0;
                Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
                Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
                runCatalog = frozen;
                StartCoroutine(BeginWaveBenchmark());
            }
            catch (Exception exception) { EndWaveBenchmark(exception); }
        }
        IEnumerator BeginWaveBenchmark()
        {
            yield return SceneManager.LoadSceneAsync("Run");
            try
            {
                Session = new InteractiveSession(runCatalog, benchmarkReplay.Header.Options);
                Aim = benchmarkReplay.Header.Options.InitialAimMode;
                seed = benchmarkReplay.Header.Options.Run.Seed;
                // Only original accepted commands advance state. Warmup has no fabricated input.
                while (Session.Simulation.World.Tick < 7200) ApplyBenchmarkCommand(false, ref benchmarkPending);
                CaptureSnapshots(); previous = Frame;
                world = new GameObject("World renderer").AddComponent<WorldRenderer>();
                var settings = CanonicalContent.Presentation.Camera;
                world.Initialize(Camera.main, new WorldCameraSettings(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), runCatalog.Tuning.DefaultEstate, Frame.MapWidth, Frame.MapHeight);
                world.AcceptWave(runCatalog, Wave); world.AcceptFrame(Frame, FirstPlayable);
                preferences.MusicVolume = .55f; preferences.EffectsVolume = .8f; preferences.Haptics = false;
                preferences.Shake = true; preferences.DamageNumbers = true;
                ApplyPreferences(); sound.ResetRun(); waveSound?.ResetRun(); bossPhaseCue.Reset(); lastWaveSound = -1;
                ShowHud(); accumulator = 0;
                File.WriteAllText(Path.Combine(benchmarkOutput, "device.json"), JsonUtility.ToJson(DeviceFacts.Capture(), true));
                ResetBenchmarkClock(); waveBenchmarkReady = true;
            }
            catch (Exception exception) { EndWaveBenchmark(exception); }
        }
        void ResetBenchmarkClock() { benchmarkFrameStart = Stopwatch.GetTimestamp(); benchmarkAllocatedStart = GC.GetAllocatedBytesForCurrentThread(); benchmarkGcStart = GC.CollectionCount(0); }
        static double BenchmarkMilliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        static bool BenchmarkWindow(int tick) => tick >= 7200 && tick <= 9900 || tick >= 10200 && tick <= 12900 || tick >= 13200 && tick <= 14400;
        void UpdateWaveBenchmark()
        {
            if (!waveBenchmarkReady || waveBenchmarkEnded) return;
            try
            {
                if (benchmarkHasPending)
                {
                    benchmarkPending.wallMs = BenchmarkMilliseconds(benchmarkFrameStart);
                    benchmarkPending.allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - benchmarkAllocatedStart;
                    benchmarkPending.gcCollections = GC.CollectionCount(0) - benchmarkGcStart;
                    benchmarkFrames.Add(benchmarkPending); benchmarkHasPending = false;
                }
                if (FrameTimingManager.GetLatestTimings(1, benchmarkTimingBuffer) > 0 && benchmarkTimingBuffer[0].frameStartTimestamp != benchmarkLastTiming)
                {
                    benchmarkLastTiming = benchmarkTimingBuffer[0].frameStartTimestamp;
                    benchmarkTimings.Add(new BenchmarkTiming { observedFrame = Time.frameCount, value = benchmarkTimingBuffer[0] });
                }
                if (benchmarkCommand == benchmarkReplay.Commands.Count) { EndWaveBenchmark(null); return; }
                ResetBenchmarkClock();
                var sample = new BenchmarkFrame { frame = Time.frameCount, focused = Application.isFocused };
                var warming = benchmarkWarmFrames > 0;
                if (warming)
                {
                    benchmarkWarmFrames--; accumulator = 0;
                    if (benchmarkCapture && benchmarkWarmFrames == 60) ScreenCapture.CaptureScreenshot(Path.Combine(benchmarkOutput, "warmup-7200.png"));
                }
                else
                {
                    if (Screen.width != 1600 || Screen.height != 900) throw new InvalidOperationException("Benchmark requires a 1600x900 render surface.");
                    accumulator += Time.unscaledDeltaTime;
                    var step = 1d / Frame.TickRate;
                    while (accumulator >= step && benchmarkCommand < benchmarkReplay.Commands.Count)
                    {
                        var command = benchmarkReplay.Commands[benchmarkCommand];
                        ApplyBenchmarkCommand(true, ref sample);
                        sample.commands++;
                        if (command.Kind == ReplayCommandKind.Advance) accumulator -= step;
                    }
                }
                var started = Stopwatch.GetTimestamp(); hud?.Present(Frame, FirstPlayable); sample.hudMs = BenchmarkMilliseconds(started);
                var visible = Screen.safeArea;
                if (hud != null)
                {
                    visible.yMin += Mathf.Min(UiTokens.BottomWorldInset * Ui.Canvas.scaleFactor, visible.height * .2f);
                    visible.yMax = Mathf.Max(visible.yMin + 1, visible.yMax - hud.ReservedTopPixels);
                }
                world.ShowAnnouncements = true;
                started = Stopwatch.GetTimestamp();
                world.Present(previous, Frame, FirstPlayable, warming ? 1 : (float)(accumulator * Frame.TickRate), Time.unscaledDeltaTime, visible);
                sample.presentMs = BenchmarkMilliseconds(started);
                sample.tick = Frame.Tick; sample.enemies = Frame.Counts.Enemies;
                benchmarkPending = sample;
                benchmarkHasPending = !warming && BenchmarkWindow(Frame.Tick);
                FrameTimingManager.CaptureFrameTimings();
            }
            catch (Exception exception) { EndWaveBenchmark(exception); }
        }
        void ApplyBenchmarkCommand(bool present, ref BenchmarkFrame sample)
        {
            var command = benchmarkReplay.Commands[benchmarkCommand++];
            var started = Stopwatch.GetTimestamp(); Session.Apply(command); sample.simulationMs += BenchmarkMilliseconds(started);
            foreach (var checkpoint in benchmarkReplay.Checkpoints)
                if (checkpoint.AppliedCommands == Session.NextSequence && checkpoint.StateHash != Session.ComputeStateHash()) throw new InvalidDataException("Benchmark checkpoint mismatch.");
            if (!present) return;
            started = Stopwatch.GetTimestamp(); previous = Frame; CaptureSnapshots(); sample.snapshotMs += BenchmarkMilliseconds(started);
            started = Stopwatch.GetTimestamp();
            world.AcceptWave(runCatalog, Wave); world.AcceptFrame(Frame, FirstPlayable);
            sound.AcceptCommonFeedback(Frame, waveSound.PlayCommon); AcceptWaveAudio();
            sample.acceptMs += BenchmarkMilliseconds(started);
        }
        void EndWaveBenchmark(Exception exception)
        {
            if (waveBenchmarkEnded) return;
            waveBenchmarkEnded = true;
            var result = new BenchmarkResult { commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty, replaySha256 = benchmarkReplaySha, developmentBuild = UnityEngine.Debug.isDebugBuild, width = Screen.width, height = Screen.height, targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount, frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), frames = benchmarkFrames.Count, renderTimingSamples = benchmarkTimings.Count, platform = Application.platform.ToString(), graphicsApi = SystemInfo.graphicsDeviceType.ToString(), quality = QualitySettings.names[QualitySettings.GetQualityLevel()], error = exception?.ToString() };
            try
            {
                if (exception == null)
                {
                    var summary = Session.GetSummary();
                    result.tick = summary.Tick; result.commands = benchmarkCommand; result.stateHash = summary.StateHash;
                    result.expectedStateHash = benchmarkReplay.End.StateHash; result.dataHash = benchmarkReplay.Header.Options.DataHash;
                    var kind = summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit;
                    result.verified = summary.Tick == benchmarkReplay.End.Tick && summary.StateHash == benchmarkReplay.End.StateHash && kind == benchmarkReplay.End.Kind && Session.NextSequence == benchmarkReplay.End.AppliedCommands;
                    if (!result.verified) result.error = "Original replay terminal state mismatch.";
                }
                if (benchmarkOutput != null)
                {
                    using (var writer = new StreamWriter(Path.Combine(benchmarkOutput, "frames.csv")))
                    {
                        writer.WriteLine("frame,tick,enemies,commands,wallMs,simulationMs,snapshotMs,acceptMs,presentMs,hudMs,allocatedBytes,gcCollections,focused");
                        foreach (var row in benchmarkFrames) writer.WriteLine(FormattableString.Invariant($"{row.frame},{row.tick},{row.enemies},{row.commands},{row.wallMs:R},{row.simulationMs:R},{row.snapshotMs:R},{row.acceptMs:R},{row.presentMs:R},{row.hudMs:R},{row.allocatedBytes},{row.gcCollections},{(row.focused ? 1 : 0)}"));
                    }
                    using (var writer = new StreamWriter(Path.Combine(benchmarkOutput, "render-timings.csv")))
                    {
                        writer.WriteLine("observedFrame,frameStartTimestamp,cpuFrameMs,cpuMainThreadMs,cpuRenderThreadMs,cpuPresentWaitMs,gpuMs");
                        foreach (var row in benchmarkTimings) writer.WriteLine(FormattableString.Invariant($"{row.observedFrame},{row.value.frameStartTimestamp},{row.value.cpuFrameTime:R},{row.value.cpuMainThreadFrameTime:R},{row.value.cpuRenderThreadFrameTime:R},{row.value.cpuMainThreadPresentWaitTime:R},{row.value.gpuFrameTime:R}"));
                    }
                    File.WriteAllText(Path.Combine(benchmarkOutput, "benchmark.json"), JsonUtility.ToJson(result, true));
                }
            }
            catch (Exception failure) { UnityEngine.Debug.LogException(failure); result.verified = false; }
            if (!result.verified) UnityEngine.Debug.LogError(result.error ?? "Benchmark failed.");
            Application.Quit(result.verified ? 0 : 1);
        }
    }
}
