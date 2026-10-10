using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;

namespace Game.Editor
{
    public static class ProfilerTraceExport
    {
        [MenuItem("Sow and Siege/Export diagnostic profiler trace")]
        public static void SelectTrace()
        {
            var input = EditorUtility.OpenFilePanel("Recorded diagnostic trace", "", "raw");
            if (string.IsNullOrEmpty(input)) return;
            Export(input, input + ".export");
        }

        public static void Run()
        {
            Export(Environment.GetEnvironmentVariable("UNITY_PROFILER_TRACE"), Environment.GetEnvironmentVariable("UNITY_PROFILER_EXPORT"));
        }

        public static void Export(string input, string output)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Specify UNITY_PROFILER_TRACE and a new UNITY_PROFILER_EXPORT directory.");
            input = Path.GetFullPath(input); output = Path.GetFullPath(output);
            if (!File.Exists(input) || !File.Exists(input + ".json")) throw new FileNotFoundException("Trace and adjacent provenance JSON are required.");
            if (Directory.Exists(output)) throw new IOException("Export directory already exists; retain prior evidence.");
            if (Profiler.enabled || Profiler.enableBinaryLog) throw new InvalidOperationException("Stop live profiling before importing a recorded trace.");
            var provenance = File.ReadAllText(input + ".json");
            var metadata = JsonUtility.FromJson<TraceMetadata>(provenance);
            var hash = Hash(input);
            if (metadata == null || !metadata.closed || metadata.scope != "diagnostic-profiler-on-not-performance-acceptance" || metadata.rawFile != Path.GetFileName(input)
                || metadata.rawBytes != new FileInfo(input).Length || !string.Equals(metadata.rawSha256, hash, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrEmpty(metadata.sessionId) || metadata.endFrame < metadata.startFrame || metadata.endRealtime < metadata.startRealtime)
                throw new InvalidDataException("Trace provenance is incomplete or does not match raw bytes.");
            if (!Hex(metadata.build, 40) || !Hex(metadata.sourceHash, 64) || !Hex(metadata.dataHash, 64)
                || !Regex.IsMatch(provenance, "\"sourceDirty\"\\s*:\\s*(true|false)\\s*[,}]")
                || metadata.unityVersion == null || !Regex.IsMatch(metadata.unityVersion, @"^\d+\.\d+\.\d+[abfp]\d+$") || metadata.startFrame < 0 || metadata.startTick < 0 || metadata.endTick < metadata.startTick
                || double.IsNaN(metadata.startRealtime) || double.IsInfinity(metadata.startRealtime) || metadata.startRealtime < 0
                || double.IsNaN(metadata.endRealtime) || double.IsInfinity(metadata.endRealtime)
                || !new[] { "Android", "OSXPlayer", "OSXEditor", "WindowsEditor", "LinuxEditor" }.Contains(metadata.platform)
                || !new[] { "time-limit", "manual", "run-complete", "recording-closed", "application-paused", "focus-lost", "error" }.Contains(metadata.stopReason))
                throw new InvalidDataException("Trace build/source/data identity, runtime or capture bounds are invalid.");
            if (metadata.profilerWarningCount != 0) throw new InvalidDataException("Profiler warnings require investigation before this trace can be accepted.");
            Profiler.enabled = false;
            if (!ProfilerDriver.LoadProfile(input, false)) throw new InvalidDataException("Unity could not load the raw profile.");
            var first = ProfilerDriver.firstFrameIndex; var last = ProfilerDriver.lastFrameIndex;
            if (first < 0 || last < first) throw new InvalidDataException("Imported trace has no frame range.");
            Directory.CreateDirectory(output);
            var report = new ExportReport
            {
                rawSha256 = hash, sessionId = metadata.sessionId, platform = metadata.platform,
                build = metadata.build, sourceHash = metadata.sourceHash, sourceDirty = metadata.sourceDirty, dataHash = metadata.dataHash,
                unityVersion = metadata.unityVersion, startTick = metadata.startTick, endTick = metadata.endTick, stopReason = metadata.stopReason,
                startUnityFrame = metadata.startFrame, endUnityFrame = metadata.endFrame,
                firstProfilerFrame = first, lastProfilerFrame = last
            };
            var observed = new SortedSet<string>(StringComparer.Ordinal);
            using (var frames = new StreamWriter(Path.Combine(output, "frames.csv")))
            using (var samples = new StreamWriter(Path.Combine(output, "samples.csv")))
            {
                frames.WriteLine("profilerFrame,cpuFrameMs,threads,gcAllocEvents,gcMetadataEvents,gcAllocatedBytes");
                samples.WriteLine("profilerFrame,threadIndex,threadName,sampleIndex,sampleName,inclusiveMs,allocationBytes");
                for (var frame = first; frame <= last; frame++)
                {
                    var threads = 0; var gcEvents = 0; var gcMetadata = 0; long gcBytes = 0; float frameMs = 0;
                    for (var thread = 0; ; thread++)
                    {
                        using var view = ProfilerDriver.GetRawFrameDataView(frame, thread);
                        if (!view.valid) break;
                        if (thread == 0) frameMs = view.frameTimeMs;
                        threads++;
                        for (var sample = 0; sample < view.sampleCount; sample++)
                        {
                            var name = view.GetSampleName(sample) ?? string.Empty;
                            if (name.Length == 0) report.unnamedSamples++;
                            var custom = name.StartsWith("SowSiege.", StringComparison.Ordinal);
                            var gc = name.IndexOf("GC", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Garbage", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (custom || gc) observed.Add(name);
                            var allocation = "";
                            if (name == "GC.Alloc")
                            {
                                gcEvents++;
                                if (view.GetSampleMetadataCount(sample) > 0)
                                {
                                    var bytes = view.GetSampleMetadataAsLong(sample, 0);
                                    if (bytes >= 0) { allocation = N(bytes); gcBytes += bytes; gcMetadata++; }
                                }
                            }
                            var sampleMs = view.GetSampleTimeMs(sample);
                            if (float.IsNaN(sampleMs) || float.IsInfinity(sampleMs) || sampleMs < 0) throw new InvalidDataException("Invalid sample duration in profiler frame " + frame);
                            samples.WriteLine(string.Join(",", N(frame), N(thread), Csv(view.threadName), N(sample), Csv(name), N(sampleMs), allocation));
                            report.exportedSamples++;
                        }
                    }
                    if (threads == 0) throw new InvalidDataException("Missing profiler frame " + frame);
                    if (float.IsNaN(frameMs) || float.IsInfinity(frameMs) || frameMs < 0) throw new InvalidDataException("Invalid CPU frame duration.");
                    frames.WriteLine(string.Join(",", N(frame), N(frameMs), N(threads), N(gcEvents), N(gcMetadata), gcEvents > 0 && gcMetadata == gcEvents ? N(gcBytes) : ""));
                    report.frames++;
                }
            }
            report.observedMarkers = observed.ToArray();
            var required = new[] { "SowSiege.CoreApply", "SowSiege.CaptureRun", "SowSiege.CaptureFirstPlayable", "SowSiege.WorldPresent", "SowSiege.Hud", "SowSiege.Telemetry" };
            report.missingMarkers = required.Where(name => !observed.Contains(name)).ToArray();
            report.complete = report.frames > 0 && report.missingMarkers.Length == 0;
            File.WriteAllText(Path.Combine(output, "export.json"), JsonUtility.ToJson(report, true));
            File.Copy(input + ".json", Path.Combine(output, "trace-provenance.json"));
            if (!report.complete) throw new InvalidDataException("Trace lacks required gameplay scopes: " + string.Join(",", report.missingMarkers));
            Debug.Log("PROFILER_TRACE_EXPORTED frames=" + report.frames + " samples=" + report.exportedSamples);
        }

        static bool Hex(string value, int length) => value != null && value.Length == length && value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F');
        static string N<T>(T value) where T : IFormattable => value.ToString(null, CultureInfo.InvariantCulture);
        static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        static string Hash(string path)
        {
            using var hash = SHA256.Create(); using var input = File.OpenRead(path);
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }
        [Serializable]
        sealed class TraceMetadata
        {
            public string scope, rawFile, rawSha256, sessionId, platform, build, sourceHash, dataHash, unityVersion, stopReason;
            public bool closed, sourceDirty;
            public long rawBytes;
            public int startFrame, endFrame, startTick, endTick, profilerWarningCount;
            public double startRealtime, endRealtime;
        }
        [Serializable]
        sealed class ExportReport
        {
            public string scope = "diagnostic-inclusive-scope-times-not-performance-acceptance";
            public string unnamedSampleScope = "Blank sample names are unavailable in the imported raw view; their thread, index and inclusive timing are retained.";
            public string allocationScope = "Observed GC.Alloc events across captured threads; blank bytes mean absent or incomplete metadata, never proven zero allocation.";
            public string timingScope = "Profiler frame indices are not telemetry row IDs. Inclusive nested samples must not be summed. CPU frame time is not GPU or presentation latency.";
            public string rawSha256, sessionId, platform, build, sourceHash, dataHash, unityVersion, stopReason;
            public string backend = "not-recorded-by-trace-sidecar; do not infer from platform";
            public int firstProfilerFrame, lastProfilerFrame, frames, exportedSamples, unnamedSamples, startUnityFrame, endUnityFrame, startTick, endTick;
            public bool sourceDirty;
            public bool complete;
            public string[] observedMarkers, missingMarkers;
        }
    }
}
