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
        const double MaximumSeconds = 10;
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
        bool scheduledConsumed;
        public DevelopmentProfilerTrace() : this(Environment.GetCommandLineArgs()) { }
        public DevelopmentProfilerTrace(string[] arguments) { requestedTick = ParseRequestedTick(arguments); }
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
                    platform = Application.platform.ToString()
                };
                Save();
                Application.logMessageReceivedThreaded += OnLog;
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
            if (Active && Time.realtimeSinceStartupAsDouble - metadata.startRealtime >= MaximumSeconds) Stop(tick, "time-limit");
        }

        public void Stop(int tick, string reason)
        {
            if (!Active) return;
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
            finally { Application.logMessageReceivedThreaded -= OnLog; metadata = null; }
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
            public bool sourceDirty, closed;
            public int startFrame, endFrame, startTick, endTick, profilerWarningCount;
            public double startRealtime, endRealtime;
            public double requestedSeconds = MaximumSeconds;
            public long rawBytes;
            public string rawSha256 = "", stopReason = "open";
            public string[] profilerWarnings = Array.Empty<string>();
        }
    }
}
#endif
