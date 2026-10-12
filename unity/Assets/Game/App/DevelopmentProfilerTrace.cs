#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Game.App.Generated;
using UnityEngine;
using UnityEngine.Profiling;

namespace Game.App
{
    public sealed class DevelopmentProfilerTrace
    {
        static readonly Unity.Profiling.ProfilerMarker TraceFileStat = new Unity.Profiling.ProfilerMarker("SowSiege.Diagnostic.TraceFileStat");
        const double MaximumSeconds = 10;
        const int TriggerBufferBytes = 128 * 1024 * 1024;
        const long TriggerFileSoftLimit = 512L * 1024 * 1024;
        public struct StallFrame { public int Frame, Tick; public double WallMs; public long StartTimestamp, EndTimestamp; }
        public sealed class StallWindow
        {
            readonly int startupFrame;
            public StallFrame Trigger { get; private set; } = new StallFrame { Frame = -1 };
            public StallWindow(int startFrame) { startupFrame = startFrame; }
            public bool Observe(StallFrame frame)
            {
                if (frame.Frame <= startupFrame) return false;
                if (Trigger.Frame < 0 && frame.WallMs > 100) Trigger = frame;
                return Trigger.Frame >= 0 && frame.Frame >= Trigger.Frame + 4;
            }
        }
        public bool StallTriggerRequested { get; }
        public bool ActiveStallTriggerCaptured => stallWindow != null && stallWindow.Trigger.Frame >= 0;
        StallWindow stallWindow;
        int previousBufferBytes;
        bool ownsBuffer;
        double nextSizeCheck;
        public static bool ParseStallTrigger(string[] args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            var first = Array.IndexOf(args, "--wave-benchmark-stall-trace");
            if (first < 0) return false;
            if (first != Array.LastIndexOf(args, "--wave-benchmark-stall-trace") || Array.IndexOf(args, "--wave-benchmark") < 0 || Array.IndexOf(args, "--wave-benchmark-profile") < 0 ||
                Array.IndexOf(args, "--wave-benchmark-trace-range") >= 0 || Array.IndexOf(args, "--offline-replay-capture") >= 0 || Array.IndexOf(args, "--wave-benchmark-capture") >= 0 || Array.IndexOf(args, "--diagnostic-trace-tick") >= 0)
                throw new ArgumentException("Stall trace requires a capture-free benchmark profiler, without another trace schedule.");
            return true;
        }
        public void ObserveBenchmarkInterval(StallFrame frame)
        {
            if (!Active || stallWindow == null) return;
            var complete = stallWindow.Observe(frame);
            metadata.triggerFrame = stallWindow.Trigger.Frame; metadata.triggerTick = stallWindow.Trigger.Tick;
            metadata.triggerWallMs = stallWindow.Trigger.WallMs;
            metadata.triggerStartTimestamp = stallWindow.Trigger.StartTimestamp; metadata.triggerEndTimestamp = stallWindow.Trigger.EndTimestamp;
            if (complete) Stop(frame.Tick, "long-frame-trigger");
        }
        public const string NormalScope = "diagnostic-profiler-on-not-performance-acceptance";
        public const string BenchmarkScope = "wave-benchmark-profiler-on-not-performance-acceptance";
        public static readonly Guid BenchmarkMetadataId = new Guid("5c6c4987-c51b-41d5-b213-729f357d1690");
        public const int BenchmarkMetadataTag = 1;
        readonly int[] frameMetadata = new int[4];
        public void EmitBenchmarkFrame(int tick, int enemies, int phase)
        {
            if (!Active) return;
            frameMetadata[0] = Time.frameCount; frameMetadata[1] = tick;
            frameMetadata[2] = enemies; frameMetadata[3] = phase;
            Profiler.EmitFrameMetaData(BenchmarkMetadataId, BenchmarkMetadataTag, frameMetadata);
        }
        readonly int? requestedTick;
        readonly bool allocationStacksRequested;
        bool previousAllocationStacks, ownsAllocationStacks;
        bool scheduledConsumed;
        public DevelopmentProfilerTrace() : this(Environment.GetCommandLineArgs()) { }
        public DevelopmentProfilerTrace(string[] arguments)
        {
            requestedTick = ParseRequestedTick(arguments);
            allocationStacksRequested = ParseAllocationStacks(arguments);
            StallTriggerRequested = ParseStallTrigger(arguments);
        }
        public static bool ParseAllocationStacks(string[] arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            var first = Array.IndexOf(arguments, "--diagnostic-allocation-stacks");
            if (first >= 0 && first != Array.LastIndexOf(arguments, "--diagnostic-allocation-stacks"))
                throw new ArgumentException("--diagnostic-allocation-stacks may appear once.");
            return first >= 0;
        }
        public static int? ParseRequestedTick(string[] arguments)
        {
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            int? result = null;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] != "--diagnostic-trace-tick") continue;
                if (result.HasValue || ++i >= arguments.Length || !int.TryParse(arguments[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var tick) || tick < 0)
                    throw new ArgumentException("--diagnostic-trace-tick requires one nonnegative integer and may appear once.");
                result = tick;
            }
            return result;
        }
        public bool TryConsumeScheduledTick(int tick, bool eligible)
        {
            if (!eligible || scheduledConsumed || !requestedTick.HasValue || tick < requestedTick.Value) return false;
            scheduledConsumed = true;
            return true;
        }
        public void StartScheduled(RunRecording recording, int tick, string dataHash, bool eligible)
        {
            if (TryConsumeScheduledTick(tick, eligible)) Start(recording, tick, dataHash);
        }
#if UNITY_EDITOR
        public void EditorRearmScheduledTraceAfterStopForTesting(string reason, bool triggerCaptured)
        {
            if (ShouldRearmScheduledTrace(reason, triggerCaptured)) scheduledConsumed = false;
        }
#endif
        TraceMetadata metadata;
        string metadataPath;
        readonly List<string> warnings = new List<string>();
        readonly object warningLock = new object();
        int warningCount;
        public bool Active => metadata != null;

        public bool Start(RunRecording recording, int tick, string dataHash)
        {
            if (recording == null) return false;
            return Start(recording.DirectoryPath, recording.SessionId, tick, dataHash, NormalScope);
        }

        public bool Start(string directory, string sessionId, int tick, string dataHash, string scope)
        {
            if (scope != NormalScope && scope != BenchmarkScope) throw new ArgumentException("Unknown trace scope.", nameof(scope));
            if (Active || string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(sessionId) || Profiler.enabled || Profiler.enableBinaryLog)
            {
                UnityEngine.Debug.LogWarning("Diagnostic trace refused: recording is unavailable or a profiler already owns capture.");
                return false;
            }
            try
            {
                var path = Path.Combine(directory, "diagnostic-" + Guid.NewGuid().ToString("N") + ".raw");
                metadataPath = path + ".json";
                lock (warningLock) { warnings.Clear(); warningCount = 0; }
                metadata = new TraceMetadata
                {
                    scope = scope, sessionId = sessionId, build = BuildIdentity.Commit, sourceHash = BuildIdentity.SourceHash,
                    sourceDirty = BuildIdentity.SourceDirty, dataHash = dataHash, rawFile = Path.GetFileName(path),
                    startedUtc = DateTime.UtcNow.ToString("O"), startFrame = Time.frameCount, startTick = tick,
                    startRealtime = Time.realtimeSinceStartupAsDouble, unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(), allocationCallstacks = allocationStacksRequested
                };
                if (StallTriggerRequested && scope == BenchmarkScope)
                {
                    metadata.requestedSeconds = 120; metadata.triggerThresholdMs = 100; metadata.contextFrames = 4;
                    metadata.profilerBufferBytes = TriggerBufferBytes; metadata.rawFileSoftLimitBytes = TriggerFileSoftLimit;
                    stallWindow = new StallWindow(Time.frameCount);
                    previousBufferBytes = Profiler.maxUsedMemory; ownsBuffer = true; Profiler.maxUsedMemory = TriggerBufferBytes;
                    nextSizeCheck = metadata.startRealtime + 1;
                }
                else stallWindow = null;
                Save();
                Application.logMessageReceivedThreaded += OnLog;
                previousAllocationStacks = Profiler.enableAllocationCallstacks;
                ownsAllocationStacks = true;
                Profiler.enableAllocationCallstacks = allocationStacksRequested;
                Profiler.logFile = path; Profiler.enableBinaryLog = true; Profiler.enabled = true;
                return true;
            }
            catch (Exception error)
            {
                Stop(tick, "start-failed");
                UnityEngine.Debug.LogWarning("Diagnostic trace start failed: " + error.Message);
                return false;
            }
        }

        public void Pump(int tick)
        {
            if (!Active) return;
            var now = Time.realtimeSinceStartupAsDouble;
            if (now - metadata.startRealtime >= metadata.requestedSeconds) { Stop(tick, "time-limit"); return; }
            if (stallWindow == null || now < nextSizeCheck) return;
            nextSizeCheck = now + 1;
            bool atLimit;
            using (TraceFileStat.Auto())
            {
                var file = new FileInfo(Path.Combine(Path.GetDirectoryName(metadataPath), metadata.rawFile));
                atLimit = file.Exists && file.Length >= TriggerFileSoftLimit;
            }
            if (atLimit) Stop(tick, "size-limit");
        }

        public void Stop(int tick, string reason)
        {
            if (!Active) return;
            var rearmScheduled = ShouldRearmScheduledTrace(reason, stallWindow != null && stallWindow.Trigger.Frame >= 0);
            try
            {
                Profiler.enabled = false; Profiler.logFile = string.Empty; Profiler.enableBinaryLog = false;
                Application.logMessageReceivedThreaded -= OnLog;
                metadata.endFrame = Time.frameCount; metadata.endTick = tick;
                metadata.endRealtime = Time.realtimeSinceStartupAsDouble; metadata.stopReason = reason;
                lock (warningLock) { metadata.profilerWarningCount = warningCount; metadata.profilerWarnings = warnings.ToArray(); }
                var path = Path.Combine(Path.GetDirectoryName(metadataPath), metadata.rawFile);
                metadata.rawBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (metadata.rawBytes > 0)
                    using (var hash = SHA256.Create()) using (var input = File.OpenRead(path))
                        metadata.rawSha256 = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                metadata.closed = true;
                Save();
                UnityEngine.Debug.Log("DIAGNOSTIC_TRACE_CLOSED " + metadata.rawFile + " reason=" + reason);
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("Diagnostic trace close failed: " + error.Message); }
            finally
            {
                Application.logMessageReceivedThreaded -= OnLog;
                metadata = null; stallWindow = null;
                if (rearmScheduled) scheduledConsumed = false;
                if (ownsBuffer) { Profiler.maxUsedMemory = previousBufferBytes; ownsBuffer = false; }
                if (ownsAllocationStacks)
                {
                    Profiler.enableAllocationCallstacks = previousAllocationStacks;
                    ownsAllocationStacks = false;
                }
            }
        }

        bool ShouldRearmScheduledTrace(string reason, bool triggerCaptured)
        {
            return requestedTick.HasValue && scheduledConsumed && !triggerCaptured && (reason == "focus-lost" || reason == "application-paused");
        }
        void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Log || (condition.IndexOf("profiler", StringComparison.OrdinalIgnoreCase) < 0 && condition.IndexOf("buffer", StringComparison.OrdinalIgnoreCase) < 0)) return;
            lock (warningLock) { warningCount++; if (warnings.Count < 16) warnings.Add(condition); }
        }
        void Save() => File.WriteAllText(metadataPath, JsonUtility.ToJson(metadata, true));

        [Serializable]
        sealed class TraceMetadata
        {
            public string scope = "diagnostic-profiler-on-not-performance-acceptance";
            public string sessionId, build, sourceHash, dataHash, rawFile, startedUtc, unityVersion, platform;
            public bool sourceDirty, closed, allocationCallstacks;
            public int startFrame, endFrame, startTick, endTick, profilerWarningCount;
            public double startRealtime, endRealtime;
            public double requestedSeconds = MaximumSeconds;
            public long rawBytes;
            public int triggerFrame = -1, triggerTick, contextFrames, profilerBufferBytes;
            public double triggerWallMs, triggerThresholdMs;
            public long triggerStartTimestamp, triggerEndTimestamp, rawFileSoftLimitBytes;
            public string triggerContract = "Opt-in benchmark: trace starts during rendered warmup; only completed measured intervals can trigger. First >100ms interval plus four following frame IDs. Stopwatch timestamps identify Update-entry intervals, not raw-profiler frame boundaries. Raw file size is a once-per-second soft limit with in-flight buffer overshoot; profiler warnings and exact raw metadata coverage remain mandatory. Stop/hash/write cost is diagnostic boundary, never acceptance.";
            public string rawSha256 = "", stopReason = "open";
            public string[] profilerWarnings = Array.Empty<string>();
        }
    }
}
#endif
