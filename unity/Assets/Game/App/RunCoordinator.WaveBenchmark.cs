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
        bool waveBenchmarkRequested, waveBenchmarkReady, waveBenchmarkEnded, benchmarkCapture, benchmarkInterrupted;
        string benchmarkMode = "capped";
        CrowdReplayBenchmark crowdBenchmark;
        bool offlineReplayRequested;
        OfflineReplayCapture offlineReplayCapture;
        bool benchmarkAllocationAvailable;
        bool benchmarkSkipGrounding, benchmarkTarget60Experiment;
        BenchmarkCoreDiagnostics benchmarkCoreDiagnostics;
        PlayerInput benchmarkMovement;
        bool benchmarkProfileRequested, benchmarkProfilerEnabled;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int benchmarkTraceWindow, benchmarkTraceEndTick, benchmarkStallTraceAttempt;
        int benchmarkDiagnosticPass = 1, benchmarkDiagnosticPasses = 1;
        int[] benchmarkDiagnosticTraceRange;
        string benchmarkDiagnosticRoot;
        readonly int[] benchmarkTraceTicks = { 8900, 11100, 13500 };
        readonly int[] crowdBenchmarkTraceTicks = { 10800, 11100, 11400 };
        public static (int Passes, int[] TraceRange) ParseBenchmarkDiagnosticOptions(string[] args)
        {
            int passes = 1; int[] range = null; bool repeatSeen = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--wave-benchmark-repeat")
                {
                    if (repeatSeen || ++i >= args.Length || args[i] != "2") throw new ArgumentException("--wave-benchmark-repeat requires 2 and may appear once.");
                    repeatSeen = true; passes = 2;
                }
                else if (args[i] == "--wave-benchmark-trace-range")
                {
                    if (range != null || i + 2 >= args.Length || !int.TryParse(args[++i], out var start) || !int.TryParse(args[++i], out var end) || start < 0 || end <= start || end - start > 240)
                        throw new ArgumentException("--wave-benchmark-trace-range requires ordered ticks spanning at most 240 ticks.");
                    range = new[] { start, end };
                }
            }
            if ((passes > 1 || range != null) && (Array.IndexOf(args, "--wave-benchmark") < 0 || Array.IndexOf(args, "--offline-replay-capture") >= 0 || Array.IndexOf(args, "--wave-benchmark-capture") >= 0))
                throw new ArgumentException("Diagnostic repeat/trace requires a capture-free wave benchmark.");
            if (range != null && Array.IndexOf(args, "--wave-benchmark-profile") < 0) throw new ArgumentException("Trace range requires --wave-benchmark-profile.");
            return (passes, range);
        }
#endif
        public static string ParseWaveBenchmarkMode(string[] arguments)
        {
            string result = null;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] != "--wave-benchmark-mode") continue;
                if (result != null || ++i >= arguments.Length || (arguments[i] != "capped" && arguments[i] != "uncapped"))
                    throw new ArgumentException("--wave-benchmark-mode requires capped or uncapped and may appear once.");
                result = arguments[i];
            }
            return result ?? "capped";
        }
        public static bool ParseWaveBenchmarkProfile(string[] arguments, bool development)
        {
            var first = Array.IndexOf(arguments, "--wave-benchmark-profile");
            if (first < 0) return false;
            if (!development || Array.LastIndexOf(arguments, "--wave-benchmark-profile") != first)
                throw new ArgumentException("--wave-benchmark-profile requires a Development build and may appear once.");
            return true;
        }
        public static bool ParseWaveBenchmarkSkipGrounding(string[] arguments)
        {
            var first = Array.IndexOf(arguments, "--wave-benchmark-skip-grounding");
            if (first < 0) return false;
            var benchmark = Array.IndexOf(arguments, "--wave-benchmark");
            if (first != Array.LastIndexOf(arguments, "--wave-benchmark-skip-grounding") ||
                benchmark < 0 || benchmark + 1 >= arguments.Length || arguments[benchmark + 1].StartsWith("--", StringComparison.Ordinal) ||
                Array.IndexOf(arguments, "--crowd-replay-benchmark") >= 0 ||
                Array.IndexOf(arguments, "--offline-replay-capture") >= 0 ||
                Array.IndexOf(arguments, "--autoplay-capture") >= 0)
                throw new ArgumentException("--wave-benchmark-skip-grounding is a single C05-only diagnostic flag; crowd, offline and autoplay are forbidden.");
            return true;
        }
        const int BenchmarkMaximumRows = 200000;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly Unity.Profiling.ProfilerMarker BenchmarkPendingInterval = new Unity.Profiling.ProfilerMarker("SowSiege.Benchmark.PendingInterval");
        static readonly Unity.Profiling.ProfilerMarker BenchmarkTimingCollection = new Unity.Profiling.ProfilerMarker("SowSiege.Benchmark.TimingCollection");
        static readonly Unity.Profiling.ProfilerMarker BenchmarkTraceHousekeeping = new Unity.Profiling.ProfilerMarker("SowSiege.Benchmark.TraceHousekeeping");
        static readonly Unity.Profiling.ProfilerMarker BenchmarkPacing = new Unity.Profiling.ProfilerMarker("SowSiege.Benchmark.Pacing");
        static readonly Unity.Profiling.ProfilerMarker BenchmarkSurfaceQuery = new Unity.Profiling.ProfilerMarker("SowSiege.Benchmark.SurfaceQuery");
        static readonly Unity.Profiling.ProfilerMarker BenchmarkPreCoreSetup = new Unity.Profiling.ProfilerMarker("SowSiege.Benchmark.PreCoreSetup");
#endif
        static readonly Unity.Profiling.ProfilerMarker BenchmarkSnapshot = new Unity.Profiling.ProfilerMarker("SowSiege.BenchmarkSnapshot");
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
            public int frame, tick, enemies, commands, gcCollections, advances, drawCalls, spriteInstances;
            public long allocatedBytes, entryTimestamp, exitTimestamp, nextEntryTimestamp;
            public double inputDeltaSeconds, accumulatorBefore, accumulatorAfter;
            public double originalWallMs, excludedBackgroundMs;
            public bool partialBoundary;
            public double wallMs, simulationMs, snapshotMs, acceptMs, presentMs, hudMs, advanceMs;
            public bool focused;
            public WorldPresentationDiagnostics composition;
        }
        struct BenchmarkTiming { public int observedFrame; public long observationTimestamp; public FrameTiming value; }
        [Serializable] sealed class BenchmarkResult
        {
            public string commit, sourceHash, dataHash, replaySha256, stateHash, expectedStateHash, error, graphicsApi, quality, platform;
            public bool sourceDirty, verified, developmentBuild, frameTimingEnabled, interrupted, profilerEnabled, allocationCounterAvailable, heroRig;
            public bool performanceEligible = true;
            public bool diagnosticOnly, actorGroundingSkipped;
            public string fixture = "c05", playbackBoundary;
            public string benchmarkMode, gcMode, metalDisplayLinkEnvironment, pacingPolicy;
            public string foregroundPolicy, foregroundAppEvidence, exclusionLedgerContract;
            public BenchmarkExclusion[] excludedIntervals;
            public BenchmarkFocusEvent[] focusEvents;
            public int frameCapacity, timingCapacity, coreTickSamples;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            public int diagnosticPass, diagnosticPasses;
            public string repeatCacheContract;
#endif
            public string coreTimingContract = "Optional core-ticks.csv records pre-Advance tick and accepted Advance only within benchmark windows; no pre-roll. Enemy includes Separation, which includes Residual: nested times and allocations must not be summed. MovementAiContact is the sorted live-enemy action/movement/contact loop before separation, nested in Enemy, not pure integration. Full Advance excludes snapshot/presentation; appended snapshotMs is the same Advance capture only. Unavailable allocation counters are -1.";
            public int tick, commands, frames, renderTimingSamples, width, height, targetFrameRate, vSyncCount, renderWarmupFrames = 120;
            public ulong cpuTimerFrequency;
            public long stopwatchFrequency;
            public string timestampContract = "frames entry/exit/nextEntryTimestamp and render observationTimestamp use Stopwatch ticks at stopwatchFrequency. FrameTiming frameStartTimestamp uses Unity CPU timer at cpuTimerFrequency; equal frequencies alone do not establish equal epoch. Preserve delayed observation and calibrate association; never join observedFrame directly.";
            public string windows = "7200-9900,10200-12900,13200-14400", timingContract = "frames.csv: Update-to-next-Update wall includes rendering and pacing; CPU scopes disjoint. render-timings.csv: delayed FrameTimingManager observations, timestamped separately, not joined to density. Allocation delta is main-thread managed allocation; GC count is process-wide generation 0.";
        }
        void TryStartWaveBenchmark()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--wave-benchmark");
            if (index < 0 && Array.IndexOf(args, "--wave-benchmark-skip-grounding") < 0 && Array.IndexOf(args, "--crowd-target60-experiment") < 0) return;
            waveBenchmarkRequested = true;
            try
            {
                benchmarkTarget60Experiment = DisplayCadence.ParseCrowdTarget60Experiment(args);
                benchmarkSkipGrounding = ParseWaveBenchmarkSkipGrounding(args);
                if (!IsWave || index + 1 >= args.Length || AutoplayCapture.Active != null) throw new ArgumentException("Wave benchmark requires wave-1a, a replay path, and no autoplay capture.");
                var output = Array.IndexOf(args, "--wave-benchmark-output");
                if (output < 0 || output + 1 >= args.Length) throw new ArgumentException("Missing --wave-benchmark-output directory.");
                var destination = WaveBenchmarkAccount.Current.OutputDirectory;
                if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length != 0) throw new IOException("Benchmark output must be empty.");
                Directory.CreateDirectory(destination);
                benchmarkOutput = destination;
                crowdBenchmark = CrowdReplayBenchmark.Parse(args);
                offlineReplayRequested = OfflineReplayCapture.Requested(args);
                benchmarkMode = benchmarkTarget60Experiment ? "diagnostic-crowd-target60-vsync0" : crowdBenchmark == null ? ParseWaveBenchmarkMode(args) : "normal-mac-vsync";
                benchmarkAllocationAvailable = AllocationCounterProbe.CurrentThreadAvailable();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                benchmarkProfileRequested = ParseWaveBenchmarkProfile(args, true);
                var diagnosticOptions = ParseBenchmarkDiagnosticOptions(args);
                benchmarkDiagnosticPasses = diagnosticOptions.Passes; benchmarkDiagnosticTraceRange = diagnosticOptions.TraceRange;
                benchmarkDiagnosticRoot = benchmarkOutput;
                if (benchmarkDiagnosticPasses > 1) { benchmarkOutput = Path.Combine(benchmarkDiagnosticRoot, "pass-1"); Directory.CreateDirectory(benchmarkOutput); }
#else
                benchmarkProfileRequested = ParseWaveBenchmarkProfile(args, false);
                if (Array.IndexOf(args, "--wave-benchmark-repeat") >= 0 || Array.IndexOf(args, "--wave-benchmark-trace-range") >= 0 || Array.IndexOf(args, "--wave-benchmark-stall-trace") >= 0)
                    throw new ArgumentException("Repeat and trace range require a Development build.");
#endif
                benchmarkProfilerEnabled = benchmarkProfileRequested;
                if (benchmarkProfileRequested || Array.IndexOf(args, "--wave-benchmark-core-diagnostics") >= 0)
                    benchmarkCoreDiagnostics = new BenchmarkCoreDiagnostics(BenchmarkMaximumRows, benchmarkAllocationAvailable, benchmarkProfileRequested);
                if (benchmarkMode == "uncapped")
                {
                    benchmarkFrames.Capacity = BenchmarkMaximumRows;
                    benchmarkTimings.Capacity = BenchmarkMaximumRows;
                }
                var bytes = File.ReadAllBytes(args[index + 1]);
                using (var sha = SHA256.Create()) benchmarkReplaySha = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (benchmarkReplaySha != (crowdBenchmark == null ? "f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5" : CrowdReplayBenchmark.ReplaySha256)) throw new InvalidDataException("Benchmark fixture identity mismatch.");
                using (var stream = new MemoryStream(bytes)) benchmarkReplay = ReplayCodec.Read(stream);
                ContentCatalog frozen = null; string hash = null;
                if (crowdBenchmark == null) LoadFrozenWaveBenchmark(ref frozen, ref hash);
                else { frozen = FoundationBoot.Catalog; hash = CanonicalContent.DataHash; crowdBenchmark.Validate(benchmarkReplaySha, hash, benchmarkReplay.End.Tick, benchmarkReplay.End.StateHash); }
                if (frozen == null || hash != benchmarkReplay.Header.Options.DataHash) throw new InvalidDataException("Frozen benchmark catalog absent or mismatched; prepare the benchmark build.");
                benchmarkCapture = Array.IndexOf(args, "--wave-benchmark-capture") >= 0;
                Screen.SetResolution(crowdBenchmark == null ? 1600 : 1920, crowdBenchmark == null ? 900 : 1080, FullScreenMode.Windowed);
                if (crowdBenchmark == null) { Application.targetFrameRate = benchmarkMode == "uncapped" ? -1 : 60; QualitySettings.vSyncCount = 0; }
                else if (benchmarkTarget60Experiment) DisplayCadence.ApplyCrowdTarget60Experiment();
                else DisplayCadence.Apply();
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
                Session.Diagnostics = benchmarkCoreDiagnostics;
                activeFeelRules=benchmarkReplay.Header.Options.Feel;activeHitFeedback=false;activeHeroRig=crowdBenchmark?.HeroRig ?? false;
                Aim = benchmarkReplay.Header.Options.InitialAimMode;
                seed = benchmarkReplay.Header.Options.Run.Seed;
                // Only original accepted commands advance state. Warmup has no fabricated input.
                while (benchmarkCommand < benchmarkReplay.Commands.Count && benchmarkReplay.Commands[benchmarkCommand].Tick < (crowdBenchmark == null ? 7200 : CrowdReplayBenchmark.StartTick)) ApplyBenchmarkCommand(false, ref benchmarkPending);
                CaptureSnapshots(); previous = CurrentFrame;
                world = new GameObject("World renderer").AddComponent<WorldRenderer>();
                world.PresentationDiagnosticsEnabled = benchmarkCoreDiagnostics != null;
                world.DiagnosticSkipActorGrounding = benchmarkSkipGrounding;
                var settings = CanonicalContent.Presentation.Camera;
                world.Initialize(Camera.main, new WorldCameraSettings(settings.WorldUnitsPerUnityUnit, settings.MinHalfHeight, settings.MaxHalfHeight, settings.EstatePadding, settings.FollowMilliseconds, settings.ZoomMilliseconds), runCatalog.Tuning.DefaultEstate, CurrentFrame.MapWidth, CurrentFrame.MapHeight);
                world.AcceptWave(runCatalog, CurrentWave); world.AcceptFrame(CurrentFrame, FirstPlayable);
                preferences.MusicVolume = .55f; preferences.EffectsVolume = .8f; preferences.Haptics = false;
                preferences.Shake = true; preferences.DamageNumbers = true;
                ApplyPreferences(); sound.ResetRun(); waveSound?.ResetRun(); bossPhaseCue.Reset(); lastWaveSound = -1;
                ShowHud(); accumulator = 0;
                if (crowdBenchmark != null) InitializeSmoothness();
                File.WriteAllText(Path.Combine(benchmarkOutput, "device.json"), JsonUtility.ToJson(DeviceFacts.Capture(), true));
                if (offlineReplayRequested) offlineReplayCapture = new OfflineReplayCapture(benchmarkOutput, Screen.width, Screen.height);
                ResetBenchmarkClock(); waveBenchmarkReady = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                RefreshLongFrameCountersAfterLoading();
#endif
            }
            catch (Exception exception) { EndWaveBenchmark(exception); }
        }
        readonly BenchmarkForegroundGate benchmarkForeground = new BenchmarkForegroundGate();
        readonly List<BenchmarkExclusion> benchmarkExclusions = new List<BenchmarkExclusion>();
        readonly List<BenchmarkFocusEvent> benchmarkFocusEvents = new List<BenchmarkFocusEvent>();
        [Serializable] public struct BenchmarkExclusion
        {
            public int firstFrame, lastFrame, tick;
            public long startTimestamp, endTimestamp;
            public string reason;
        }
        [Serializable] public struct BenchmarkFocusEvent
        {
            public int frame, tick;
            public long timestamp, utcUnixMilliseconds;
            public bool focus, paused;
            public string kind, foregroundApp;
        }
        void BenchmarkLifecycle(bool focusEvent, bool value)
        {
            if (focusEvent) benchmarkForeground.Focus(value); else benchmarkForeground.Pause(value);
            benchmarkFocusEvents.Add(new BenchmarkFocusEvent { frame = Time.frameCount, tick = CurrentFrame?.Tick ?? 0,
                timestamp = Stopwatch.GetTimestamp(), utcUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                focus = benchmarkForeground.Focused, paused = benchmarkForeground.Paused,
                kind = focusEvent ? "focus" : "pause", foregroundApp = "external-observer-required" });
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (focusEvent ? !value : value) StopBenchmarkProfilerTraceForLifecycle(focusEvent ? "focus-lost" : "application-paused");
#endif
        }
        bool SuspendBenchmarkFrame(long timestamp)
        {
            if (benchmarkForeground.Focused != Application.isFocused)
            {
                BenchmarkLifecycle(true, Application.isFocused);
                timestamp = Stopwatch.GetTimestamp();
            }
            if (!benchmarkForeground.SkipFrame()) return false;
            var start = benchmarkFrameStart;
            if (benchmarkHasPending)
            {
                var interval = new BenchmarkForegroundInterval(benchmarkPending.entryTimestamp);
                foreach (var transition in benchmarkFocusEvents)
                    if (transition.timestamp >= benchmarkPending.entryTimestamp && transition.timestamp <= timestamp)
                        interval.Transition(transition.timestamp, transition.focus && !transition.paused);
                benchmarkPending.nextEntryTimestamp = timestamp;
                benchmarkPending.originalWallMs = (timestamp - benchmarkPending.entryTimestamp) * 1000d / Stopwatch.Frequency;
                benchmarkPending.wallMs = interval.Finish(timestamp) * 1000d / Stopwatch.Frequency;
                benchmarkPending.excludedBackgroundMs = benchmarkPending.originalWallMs - benchmarkPending.wallMs;
                benchmarkPending.partialBoundary = true;
                benchmarkPending.allocatedBytes = benchmarkAllocationAvailable ? GC.GetAllocatedBytesForCurrentThread() - benchmarkAllocatedStart : -1;
                benchmarkPending.gcCollections = GC.CollectionCount(0) - benchmarkGcStart;
                if (benchmarkPending.wallMs <= 0) throw new InvalidOperationException("Cannot prove a positive foreground interval at lifecycle boundary.");
                if (benchmarkFrames.Count == benchmarkFrames.Capacity) throw new InvalidOperationException("Benchmark frame capacity exceeded.");
                benchmarkFrames.Add(benchmarkPending);
            }
            var first = Time.frameCount;
            var reason = !benchmarkForeground.Focused || benchmarkForeground.Paused ? "background" : "resume-boundary";
            var last = benchmarkExclusions.Count - 1;
            if (last >= 0 && benchmarkExclusions[last].lastFrame + 1 >= first && benchmarkExclusions[last].reason == reason)
            {
                var previous = benchmarkExclusions[last]; previous.lastFrame = Time.frameCount; previous.endTimestamp = timestamp;
                benchmarkExclusions[last] = previous;
            }
            else benchmarkExclusions.Add(new BenchmarkExclusion { firstFrame = first, lastFrame = Time.frameCount,
                tick = CurrentFrame.Tick, startTimestamp = start, endTimestamp = timestamp, reason = reason });
            benchmarkHasPending = false;
            ResetBenchmarkClock();
            return true;
        }
        void ResetBenchmarkClock() { benchmarkFrameStart = Stopwatch.GetTimestamp(); benchmarkAllocatedStart = benchmarkAllocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1; benchmarkGcStart = GC.CollectionCount(0); }
        static double BenchmarkMilliseconds(long start) => (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        bool BenchmarkWindow(int tick) => crowdBenchmark != null ? CrowdReplayBenchmark.InWindow(tick) : tick >= 7200 && tick <= 9900 || tick >= 10200 && tick <= 12900 || tick >= 13200 && tick <= 14400;
        void UpdateWaveBenchmark()
        {
            var entryTimestamp = Stopwatch.GetTimestamp();
            var entryAllocated = benchmarkAllocationAvailable ? GC.GetAllocatedBytesForCurrentThread() : -1;
            var entryGc = GC.CollectionCount(0);
            if (!waveBenchmarkReady || waveBenchmarkEnded) return;
            try
            {
                if (!offlineReplayRequested && SuspendBenchmarkFrame(entryTimestamp)) return;
                if (offlineReplayCapture?.Error != null) throw offlineReplayCapture.Error;
                if (offlineReplayCapture != null && offlineReplayCapture.Pending) return;
                var presentationDelta = offlineReplayRequested ? (float)OfflineReplayCapture.FrameSeconds : Time.unscaledDeltaTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                using (BenchmarkPendingInterval.Auto())
#endif
                if (benchmarkHasPending)
                {
                    benchmarkPending.nextEntryTimestamp = entryTimestamp;
                    benchmarkPending.wallMs = (entryTimestamp - benchmarkFrameStart) * 1000d / Stopwatch.Frequency;
                    benchmarkPending.allocatedBytes = benchmarkAllocationAvailable ? entryAllocated - benchmarkAllocatedStart : -1;
                    benchmarkPending.gcCollections = entryGc - benchmarkGcStart;
                    if (benchmarkFrames.Count == benchmarkFrames.Capacity) throw new InvalidOperationException("Benchmark frame capacity exceeded; no measured rows were dropped.");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    profilerTrace.ObserveBenchmarkInterval(new DevelopmentProfilerTrace.StallFrame { Frame = benchmarkPending.frame, Tick = benchmarkPending.tick, WallMs = benchmarkPending.wallMs, StartTimestamp = benchmarkPending.entryTimestamp, EndTimestamp = entryTimestamp });
#endif
                    benchmarkFrames.Add(benchmarkPending); benchmarkHasPending = false;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                using (BenchmarkTimingCollection.Auto())
#endif
                if (!offlineReplayRequested && FrameTimingManager.GetLatestTimings(1, benchmarkTimingBuffer) > 0 && benchmarkTimingBuffer[0].frameStartTimestamp != benchmarkLastTiming)
                {
                    benchmarkLastTiming = benchmarkTimingBuffer[0].frameStartTimestamp;
                    if (benchmarkTimings.Count == benchmarkTimings.Capacity) throw new InvalidOperationException("Benchmark timing capacity exceeded; no observations were dropped.");
                    benchmarkTimings.Add(new BenchmarkTiming { observedFrame = Time.frameCount, observationTimestamp = Stopwatch.GetTimestamp(), value = benchmarkTimingBuffer[0] });
                }
                if (benchmarkCommand == benchmarkReplay.Commands.Count && (offlineReplayRequested || benchmarkFrames.Count > 0 && benchmarkFrames[benchmarkFrames.Count - 1].tick == CurrentFrame.Tick)) { EndWaveBenchmark(null); return; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                using (BenchmarkTraceHousekeeping.Auto())
                {
                benchmarkProfilerEnabled |= UnityEngine.Profiling.Profiler.enabled;
                if (!profilerTrace.StallTriggerRequested && (crowdBenchmark != null || benchmarkDiagnosticTraceRange != null) && profilerTrace.Active && CurrentFrame.Tick >= benchmarkTraceEndTick)
                    profilerTrace.Stop(CurrentFrame.Tick, benchmarkDiagnosticTraceRange != null ? "explicit-window-end" : "crowd-window-end");
                profilerTrace.Pump(CurrentFrame.Tick);
                if (benchmarkProfileRequested && profilerTrace.StallTriggerRequested && benchmarkTraceWindow == 0 && !benchmarkInterrupted && benchmarkWarmFrames <= 60)
                    StartBenchmarkStallTrace();
                var traceTicks = benchmarkDiagnosticTraceRange != null ? benchmarkDiagnosticTraceRange : crowdBenchmark == null ? benchmarkTraceTicks : crowdBenchmarkTraceTicks;
                var traceCount = benchmarkDiagnosticTraceRange != null ? 1 : traceTicks.Length;
                if (benchmarkProfileRequested && !profilerTrace.StallTriggerRequested && !profilerTrace.Active && !benchmarkInterrupted && benchmarkWarmFrames == 0 && benchmarkTraceWindow < traceCount && CurrentFrame.Tick >= traceTicks[benchmarkTraceWindow])
                {
                    benchmarkTraceEndTick = benchmarkDiagnosticTraceRange != null ? benchmarkDiagnosticTraceRange[1] : crowdBenchmark == null ? int.MaxValue : Math.Min(traceTicks[benchmarkTraceWindow] + 180, CrowdReplayBenchmark.EndTick);
                    benchmarkTraceWindow++;
                    if (!profilerTrace.Start(benchmarkOutput, (crowdBenchmark == null ? "wave-c05-" : "crowd-411-") + benchmarkTraceWindow, CurrentFrame.Tick, benchmarkReplay.Header.Options.DataHash, DevelopmentProfilerTrace.BenchmarkScope))
                        throw new InvalidOperationException("Could not start requested benchmark profiler window.");
                    benchmarkProfilerEnabled = true;
                }
                }
#endif
                benchmarkFrameStart = entryTimestamp; benchmarkAllocatedStart = entryAllocated; benchmarkGcStart = entryGc;
                var sample = new BenchmarkFrame { frame = Time.frameCount, focused = Application.isFocused, entryTimestamp = entryTimestamp, inputDeltaSeconds = presentationDelta, accumulatorBefore = accumulator };
                var warming = benchmarkWarmFrames > 0;
                if (warming)
                {
                    benchmarkWarmFrames--; accumulator = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (benchmarkWarmFrames == 1) RefreshLongFrameCountersAfterLoading();
#endif
                    if (benchmarkCapture && benchmarkWarmFrames == 60) ScreenCapture.CaptureScreenshot(Path.Combine(benchmarkOutput, "warmup-7200.png"));
                }
                else
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    using (BenchmarkSurfaceQuery.Auto())
#endif
                    if (Screen.width != (crowdBenchmark == null ? 1600 : 1920) || Screen.height != (crowdBenchmark == null ? 900 : 1080)) throw new InvalidOperationException("Benchmark render surface changed.");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    using (BenchmarkPacing.Auto())
                    {
#endif
                    accumulator += offlineReplayRequested ? OfflineReplayCapture.FrameSeconds : Time.unscaledDeltaTime;
                    var step = 1d / CurrentFrame.TickRate;
                    var advanceBudget = new FrameAdvanceBudget(!offlineReplayRequested);
                    while (accumulator >= step && benchmarkCommand < benchmarkReplay.Commands.Count)
                    {
                        var command = benchmarkReplay.Commands[benchmarkCommand];
                        if (command.Kind == ReplayCommandKind.Advance && !advanceBudget.TryAdvance(ref accumulator, step)) break;
                        ApplyBenchmarkCommand(true, ref sample);
                        sample.commands++;
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    }
#endif
                }
                BeginLongFrameHud();
                var started = Stopwatch.GetTimestamp(); using (RunProfilerMarkers.Hud.Auto()) hud?.Present(CurrentFrame, FirstPlayable); sample.hudMs = BenchmarkMilliseconds(started);
                EndLongFrameHud();
                if (Ui.PortraitEnabled) { Ui.SynchronizeViewport(); world.SetPresentationViewport(Ui.PortraitPixels); }
                var visible = Ui.PortraitEnabled ? Ui.SafePixels : Screen.safeArea;
                if (hud != null)
                {
                    if (Ui.PortraitEnabled || crowdBenchmark != null)
                    {
                        var overlays = Ui.GetWorldOverlayBounds();
                        visible = Rect.MinMaxRect(Mathf.Max(visible.xMin, overlays.xMin), Mathf.Max(visible.yMin, overlays.yMin), Mathf.Min(visible.xMax, overlays.xMax), Mathf.Min(visible.yMax, overlays.yMax));
                    }
                    else
                    {
                        visible.yMin += Mathf.Min(UiTokens.BottomWorldInset * Ui.Canvas.scaleFactor, visible.height * .2f);
                        visible.yMax = Mathf.Max(visible.yMin + 1, visible.yMax - hud.ReservedTopPixels);
                    }
                }
                world.ShowAnnouncements = true;
                started = Stopwatch.GetTimestamp();
                var alpha = warming ? 1 : (float)(accumulator * CurrentFrame.TickRate);
                if (crowdBenchmark != null)
                {
                    if (!warming) PredictLord(benchmarkMovement, presentationDelta);
                    alpha = renderInterpolation.Present(CurrentFrame.Tick, alpha, warming, presentationDelta, CurrentFrame.TickRate);
                }
                using (RunProfilerMarkers.WorldPresent.Auto()) world.Present(previous, CurrentFrame, FirstPlayable, alpha, presentationDelta, visible);
                sample.presentMs = BenchmarkMilliseconds(started);
                LongFrameViewPresented();
                sample.composition = world.LastDiagnostics;
                sample.tick = CurrentFrame.Tick; sample.enemies = CurrentFrame.Counts.Enemies;
                sample.drawCalls = world.DrawCalls; sample.spriteInstances = world.SubmittedInstances;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                profilerTrace.EmitBenchmarkFrame(sample.tick, sample.enemies, warming ? 0 : BenchmarkWindow(sample.tick) ? 1 : 2);
#endif
                sample.accumulatorAfter = accumulator;
                sample.exitTimestamp = Stopwatch.GetTimestamp();
                benchmarkPending = sample;
                benchmarkHasPending = !offlineReplayRequested && !warming && BenchmarkWindow(CurrentFrame.Tick);
                if (offlineReplayRequested && !warming) StartCoroutine(offlineReplayCapture.SaveRenderedFrame(CurrentFrame.Tick, alpha));
                if (!offlineReplayRequested) FrameTimingManager.CaptureFrameTimings();
            }
            catch (Exception exception) { EndWaveBenchmark(exception); }
        }
        void ApplyBenchmarkCommand(bool present, ref BenchmarkFrame sample)
        {
            var command = benchmarkReplay.Commands[benchmarkCommand++];
            var measureTick = benchmarkCoreDiagnostics != null && present && command.Kind == ReplayCommandKind.Advance && BenchmarkWindow(command.Tick);
            long started;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (BenchmarkPreCoreSetup.Auto())
            {
#endif
            if (measureTick) benchmarkCoreDiagnostics.BeginTick(command.Tick, Time.frameCount);
            started = Stopwatch.GetTimestamp();
            BeginLongFrameCommand(command.Kind);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            }
#endif
            try { using (RunProfilerMarkers.CoreApply.Auto()) Session.Apply(command); }
            finally { EndLongFrameCommand(command.Kind); if (measureTick) benchmarkCoreDiagnostics.EndTick(); }
            var elapsed = BenchmarkMilliseconds(started); sample.simulationMs += elapsed;
            if (command.Kind == ReplayCommandKind.Advance) { sample.advances++; sample.advanceMs += elapsed; benchmarkMovement = command.Input; }
            foreach (var checkpoint in benchmarkReplay.Checkpoints)
                if (checkpoint.AppliedCommands == Session.NextSequence && checkpoint.StateHash != Session.ComputeStateHash()) throw new InvalidDataException("Benchmark checkpoint mismatch.");
            if (!present) return;
            started = Stopwatch.GetTimestamp(); previous = CurrentFrame; using (BenchmarkSnapshot.Auto()) CaptureSnapshots();
            var snapshotMs = BenchmarkMilliseconds(started); sample.snapshotMs += snapshotMs;
            if (measureTick) benchmarkCoreDiagnostics.RecordSnapshot(command.Tick, Time.frameCount, snapshotMs);
            if (crowdBenchmark != null && command.Kind == ReplayCommandKind.Advance) ObserveLordMovement(command.Input);
            started = Stopwatch.GetTimestamp();
            using (RunProfilerMarkers.AcceptPresentation.Auto())
            {
                world.AcceptWave(runCatalog, CurrentWave); world.AcceptFrame(CurrentFrame, FirstPlayable);
                sound.AcceptCommonFeedback(CurrentFrame, waveSound.PlayCommon); AcceptWaveAudio();
            }
            sample.acceptMs += BenchmarkMilliseconds(started);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void StartBenchmarkStallTrace()
        {
            benchmarkTraceWindow = 1;
            benchmarkStallTraceAttempt++;
            var session = "stall-trigger-" + benchmarkDiagnosticPass + "-attempt-" + benchmarkStallTraceAttempt;
            if (!profilerTrace.Start(benchmarkOutput, session, CurrentFrame.Tick, benchmarkReplay.Header.Options.DataHash, DevelopmentProfilerTrace.BenchmarkScope))
                throw new InvalidOperationException("Could not start requested stall-trigger trace.");
        }
        void StopBenchmarkProfilerTraceForLifecycle(string reason)
        {
            if (!profilerTrace.Active) return;
            var rearmStallTrigger = profilerTrace.StallTriggerRequested && !profilerTrace.ActiveStallTriggerCaptured && benchmarkTraceWindow == 1;
            profilerTrace.Stop(CurrentFrame?.Tick ?? 0, reason);
            if (rearmStallTrigger) benchmarkTraceWindow = 0;
        }
#endif
        void EndWaveBenchmark(Exception exception)
        {
            if (waveBenchmarkEnded) return;
            waveBenchmarkEnded = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            benchmarkProfilerEnabled |= UnityEngine.Profiling.Profiler.enabled;
            profilerTrace.Stop(CurrentFrame?.Tick ?? 0, exception == null ? "run-complete" : "error");
#endif
            var result = new BenchmarkResult { frameCapacity = benchmarkFrames.Capacity, timingCapacity = benchmarkTimings.Capacity, benchmarkMode = benchmarkMode, profilerEnabled = benchmarkProfilerEnabled, gcMode = UnityEngine.Scripting.GarbageCollector.GCMode.ToString(), commit = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash, sourceDirty = BuildIdentity.SourceDirty, replaySha256 = benchmarkReplaySha, developmentBuild = UnityEngine.Debug.isDebugBuild, width = Screen.width, height = Screen.height, targetFrameRate = Application.targetFrameRate, vSyncCount = QualitySettings.vSyncCount, frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(), interrupted = benchmarkInterrupted, frames = benchmarkFrames.Count, renderTimingSamples = benchmarkTimings.Count, platform = Application.platform.ToString(), graphicsApi = SystemInfo.graphicsDeviceType.ToString(), quality = QualitySettings.names[QualitySettings.GetQualityLevel()], error = exception?.ToString() };
            result.foregroundPolicy = "pause-input-exclude-boundary-resume-v1";
            result.exclusionLedgerContract = "Excluded intervals identify skipped frame IDs. Their timestamp spans may overlap foreground work retained in partialBoundary rows; they are not pure excluded-duration totals. Use each boundary row excludedBackgroundMs for its excluded duration.";
            result.foregroundAppEvidence = "foreground-app-events.csv external NSWorkspace observer required";
            result.excludedIntervals = benchmarkExclusions.ToArray();
            result.focusEvents = benchmarkFocusEvents.ToArray();
            result.cpuTimerFrequency = FrameTimingManager.GetCpuTimerFrequency();
            result.stopwatchFrequency = Stopwatch.Frequency;
            result.allocationCounterAvailable = benchmarkAllocationAvailable;
            result.coreTickSamples = benchmarkCoreDiagnostics?.Count ?? 0;
            result.heroRig = activeHeroRig;
            result.metalDisplayLinkEnvironment = Environment.GetEnvironmentVariable("UNITY_USE_METAL_DISPLAY_LINK") ?? "unset";
            result.diagnosticOnly = benchmarkTarget60Experiment || benchmarkSkipGrounding || benchmarkCoreDiagnostics != null || benchmarkProfilerEnabled || result.developmentBuild;
            result.performanceEligible = !offlineReplayRequested && !result.diagnosticOnly;
            LongFrameBenchmarkMetadata(result);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            result.diagnosticPass = benchmarkDiagnosticPass; result.diagnosticPasses = benchmarkDiagnosticPasses;
            result.repeatCacheContract = "Same process; identical replay initial state; scene-local world/pools rebuilt, engine shader/resource caches retained. No forced GC or unload.";
#endif
            result.actorGroundingSkipped = benchmarkSkipGrounding;
            result.pacingPolicy = offlineReplayRequested ? FrameAdvanceBudget.LegacyPolicy : FrameAdvanceBudget.DeferredPolicy;
            if (offlineReplayRequested) result.benchmarkMode = "offline-render-60fps";
            if (crowdBenchmark != null)
            {
                result.fixture = "rules-off-six-minute-8c94ce2";
                result.windows = "10800-11530";
                result.playbackBoundary = "Capture-free native replay, not live input. Original accepted commands are pre-rolled without rendering to tick10800; 120 rendered warmup frames then 1x playback until original terminal state. No JPEG, readback, audio recording or forced survival. Card commands replay immediately; original human pause durations are not encoded. Draw calls are world batch submissions, excluding Unity UI and render-pipeline passes. Allocation bytes unavailable are -1.";
            }
            if (offlineReplayRequested) result.playbackBoundary = "오프라인 렌더 · 60fps · 무음. Original commands and terminal hash; fixed 1/60 playback/presentation delta and end-of-frame synchronous readback. No native performance gate evidence. See offline-capture.json and offline-frame-index.csv.";
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
                if (offlineReplayCapture != null) offlineReplayCapture.Finish(OfflineReplayCapture.ExpectedFrames(CrowdReplayBenchmark.EndTick - CrowdReplayBenchmark.StartTick, CurrentFrame.TickRate), result.verified);
                if (benchmarkOutput != null)
                {
                    using (var writer = new StreamWriter(Path.Combine(benchmarkOutput, "frames.csv")))
                    {
                        writer.WriteLine("frame,tick,enemies,commands,wallMs,simulationMs,snapshotMs,acceptMs,presentMs,hudMs,allocatedBytes,gcCollections,focused,advances,advanceMs,worldBatchDrawCalls,worldInstances,entryTimestamp,exitTimestamp,nextEntryTimestamp,inputDeltaSeconds,accumulatorBefore,accumulatorAfter,partialBoundary,originalWallMs,excludedBackgroundMs");
                        foreach (var row in benchmarkFrames) writer.WriteLine(FormattableString.Invariant($"{row.frame},{row.tick},{row.enemies},{row.commands},{row.wallMs:R},{row.simulationMs:R},{row.snapshotMs:R},{row.acceptMs:R},{row.presentMs:R},{row.hudMs:R},{row.allocatedBytes},{row.gcCollections},{(row.focused ? 1 : 0)}") + FormattableString.Invariant($",{row.advances},{row.advanceMs:R},{row.drawCalls},{row.spriteInstances},{row.entryTimestamp},{row.exitTimestamp},{row.nextEntryTimestamp},{row.inputDeltaSeconds:R},{row.accumulatorBefore:R},{row.accumulatorAfter:R},{(row.partialBoundary ? 1 : 0)},{row.originalWallMs:R},{row.excludedBackgroundMs:R}"));
                    }
                    using (var writer = new StreamWriter(Path.Combine(benchmarkOutput, "render-timings.csv")))
                    {
                        writer.WriteLine("observedFrame,frameStartTimestamp,cpuFrameMs,cpuMainThreadMs,cpuRenderThreadMs,cpuPresentWaitMs,gpuMs,observationTimestamp");
                        foreach (var row in benchmarkTimings) writer.WriteLine(FormattableString.Invariant($"{row.observedFrame},{row.value.frameStartTimestamp},{row.value.cpuFrameTime:R},{row.value.cpuMainThreadFrameTime:R},{row.value.cpuRenderThreadFrameTime:R},{row.value.cpuMainThreadPresentWaitTime:R},{row.value.gpuFrameTime:R},{row.observationTimestamp}"));
                    }
                    if (benchmarkCoreDiagnostics != null)
                    {
                        using (var writer = new StreamWriter(Path.Combine(benchmarkOutput, "core-ticks.csv"))) benchmarkCoreDiagnostics.WriteCsv(writer);
                        using (var writer = new StreamWriter(Path.Combine(benchmarkOutput, "composition.csv")))
                        {
                            writer.WriteLine("frame,tick,enemies,totalMs,setupMs,cameraUiMs,fallowMaskMs,terrainLandmarksMs,waveWorldObjectsMs,attackEffectsMs,enemyActorsMs,heroThreatsMs,spriteBatchSubmitMs,cleanupMs,drawCalls,instances,maskUpdates,maskPixelsUploaded,maskPaintPixelVisits,groundingAdds");
                            foreach (var row in benchmarkFrames)
                            {
                                var c = row.composition;
                                writer.WriteLine(FormattableString.Invariant($"{row.frame},{row.tick},{row.enemies},{c.TotalMs:R},{c.SetupMs:R},{c.CameraUiMs:R},{c.FallowMaskMs:R},{c.TerrainLandmarksMs:R},{c.WaveWorldObjectsMs:R},{c.AttackEffectsMs:R},{c.EnemyActorsMs:R},{c.HeroThreatsMs:R},{c.SpriteBatchSubmitMs:R},{c.CleanupMs:R},{c.DrawCalls},{c.SubmittedInstances},{c.MaskUpdates},{c.MaskPixelsUploaded},{c.MaskPaintPixelVisits},{c.GroundingAdds}"));
                            }
                        }
                    }
                    FinishLongFrames();
                    File.WriteAllText(Path.Combine(benchmarkOutput, "benchmark.json"), JsonUtility.ToJson(result, true));
                }
            }
            catch (Exception failure)
            {
                UnityEngine.Debug.LogException(failure); result.verified = false; result.error = failure.ToString();
                if (benchmarkOutput != null) File.WriteAllText(Path.Combine(benchmarkOutput, "benchmark.json"), JsonUtility.ToJson(result, true));
            }
            finally { offlineReplayCapture?.Dispose(); }
            if (!result.verified) UnityEngine.Debug.LogError(result.error ?? "Benchmark failed.");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (result.verified && !benchmarkInterrupted && benchmarkDiagnosticPass < benchmarkDiagnosticPasses)
            {
                StartCoroutine(RepeatWaveBenchmark());
                return;
            }
#endif
            Application.Quit(result.verified ? 0 : 1);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        IEnumerator RepeatWaveBenchmark()
        {
            waveBenchmarkReady = false;
            yield return null;
            ResetLongFramesForRepeat();
            benchmarkDiagnosticPass++;
            benchmarkOutput = Path.Combine(benchmarkDiagnosticRoot, "pass-" + benchmarkDiagnosticPass);
            Directory.CreateDirectory(benchmarkOutput);
            benchmarkExclusions.Clear(); benchmarkFocusEvents.Clear();
            benchmarkFrames.Clear(); benchmarkTimings.Clear(); benchmarkLastTiming = 0;
            benchmarkCommand = 0; benchmarkWarmFrames = 120; benchmarkPending = default; benchmarkHasPending = false;
            benchmarkMovement = default; benchmarkTraceWindow = 0; benchmarkTraceEndTick = 0; benchmarkStallTraceAttempt = 0;
            benchmarkProfilerEnabled = benchmarkProfileRequested;
            if (benchmarkCoreDiagnostics != null) benchmarkCoreDiagnostics = new BenchmarkCoreDiagnostics(BenchmarkMaximumRows, benchmarkAllocationAvailable, benchmarkProfileRequested);
            waveBenchmarkEnded = false; accumulator = 0;
            yield return BeginWaveBenchmark();
        }
#endif
    }
}
