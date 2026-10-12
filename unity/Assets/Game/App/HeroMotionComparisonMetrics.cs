using System;
using System.IO;
using Unity.Profiling;
using UnityEngine;

namespace Game.App
{
    // Opt-in bounded diagnostic, never constructed by ordinary play. Sample at the next
    // Update: ProfilerRecorder values belong to the completed frame, including LateUpdate.
    public sealed class HeroMotionComparisonMetrics : IDisposable
    {
        const int Capacity = 18000;
        readonly Sample[] samples = new Sample[Capacity];
        ProfilerRecorder deformation, main;
        int count;
        readonly FrameTiming[] timing = new FrameTiming[1];
        public int DroppedSamples { get; private set; }
        struct Sample
        {
            public int frame, tick, enemies, gc;
            public double interval, pose, main, skin, render, gpu;
            public long managed, native;
            public bool focus;
        }
        public HeroMotionComparisonMetrics()
        {
            deformation = ProfilerRecorder.StartNew(ProfilerCategory.U2D, "DeformationManager.LateUpdate", 1);
            main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        }
        public void RecordCompletedFrame(int frame, int tick, int enemies, double intervalMilliseconds, double poseMilliseconds, bool foregroundInterval)
        {
            if (count == Capacity) { DroppedSamples++; return; }
            // Memory polling is sparse; retain -1 for frames without a memory observation.
            var memory = count % 60 == 0;
            var timingAvailable = FrameTimingManager.GetLatestTimings(1, timing) > 0;
            FrameTimingManager.CaptureFrameTimings();
            samples[count++] = new Sample { frame = frame, tick = tick, enemies = enemies,
                interval = intervalMilliseconds, pose = poseMilliseconds,
                render = timingAvailable ? timing[0].cpuRenderThreadFrameTime : -1,
                gpu = timingAvailable ? timing[0].gpuFrameTime : -1,
                main = main.Valid && main.Count > 0 ? main.LastValue * 0.000001 : -1,
                skin = deformation.Valid && deformation.Count > 0 ? deformation.LastValue * 0.000001 : -1,
                managed = memory ? GC.GetTotalMemory(false) : -1,
                native = memory ? UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() : -1,
                gc = GC.CollectionCount(0), focus = foregroundInterval };
        }
        public void Save(string directory, string mode, string replaySha256, string finalStateHash, bool captureActive,
            bool replayVerified, bool benchmarkEligible)
        {
            Directory.CreateDirectory(directory);
            using (var writer = new StreamWriter(Path.Combine(directory, "hero-motion-metrics.csv")))
            {
                writer.WriteLine("frame,tick,enemies,intervalMs,previousPoseMs,mainThreadMs,spriteSkinLateUpdateMs,latestRenderThreadMs,latestGpuMs,managedBytes,unityAllocatedBytes,gen0Count,focused");
                for (var i = 0; i < count; i++)
                {
                    var s = samples[i];
                    writer.WriteLine(FormattableString.Invariant($"{s.frame},{s.tick},{s.enemies},{s.interval:R},{s.pose:R},{s.main:R},{s.skin:R},{s.render:R},{s.gpu:R},{s.managed},{s.native},{s.gc},{(s.focus ? 1 : 0)}"));
                }
            }
            File.WriteAllText(Path.Combine(directory, "hero-motion-metrics.json"), JsonUtility.ToJson(new Manifest {
                contract = mode == "a-knight-live" ? "a-knight-live-v1" : HeroMotionComparison.Contract,
                mode = mode, replaySha256 = replaySha256, finalStateHash = finalStateHash,
                frames = count, dropped = DroppedSamples, captureActive = captureActive,
                replayVerified = replayVerified, developmentBuild = UnityEngine.Debug.isDebugBuild,
                performanceEligible = replayVerified && benchmarkEligible && !UnityEngine.Debug.isDebugBuild && !captureActive && count > 0 && DroppedSamples == 0,
                spriteSkinRecorderAvailable = deformation.Valid, mainRecorderAvailable = main.Valid }, true));
        }
        [Serializable] sealed class Manifest
        {
            public string contract = HeroMotionComparison.Contract, mode, replaySha256, finalStateHash;
            public string interpretation = "Record at next Update; negative timings mean unavailable, never zero cost. Skin marker includes deferred package LateUpdate, not only pose submission. Filter focused rows: foreground transitions in this or the previous frame are excluded. Cross-check benchmark.json and its exclusion ledger. Eligibility requires verified terminal replay and eligible release benchmark; teardown alone is not completion. Build size is a separate on-disk measurement. FrameTimingManager GPU/render values are latest asynchronous timings, not exact row attribution. Unity allocated memory is not process RSS.";
            public int frames, dropped;
            public bool captureActive, performanceEligible, replayVerified, developmentBuild, spriteSkinRecorderAvailable, mainRecorderAvailable;
        }
        public void Dispose() { deformation.Dispose(); main.Dispose(); }
    }
}
