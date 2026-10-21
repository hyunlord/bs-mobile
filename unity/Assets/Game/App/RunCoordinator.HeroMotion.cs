using System;
using Game.View;
using System.IO;
using UnityEngine;

namespace Game.App
{
    public sealed partial class RunCoordinator
    {
        bool heroMotionArgumentsRead;
        bool benchmarkImpactBaseline;
        KnightReview knightReview;
        HeroMotionComparison heroMotionComparison;
        WorldRenderer configuredMotionWorld;
        HeroMotionComparisonMetrics heroMotionMetrics;
        string heroMotionMetricsDirectory;
        bool heroMotionMetricsCapture, heroMotionMetricsFinished;

        void ConfigureHeroMotionComparison()
        {
            if (!heroMotionArgumentsRead)
            {
                knightReview = KnightReview.Parse(Environment.GetCommandLineArgs());
                heroMotionComparison = HeroMotionComparison.Parse(Environment.GetCommandLineArgs());
                heroMotionArgumentsRead = true;
                var args = Environment.GetCommandLineArgs();
                var impactBaseline=Array.IndexOf(args,"--impact-feedback-baseline");
                if(impactBaseline>=0)
                {
                    if(Array.LastIndexOf(args,"--impact-feedback-baseline")!=impactBaseline||Array.IndexOf(args,"--wave-benchmark")<0)
                        throw new ArgumentException("Impact baseline requires the fixed replay benchmark.");
                    benchmarkImpactBaseline=true;
                }
                var metrics = Array.IndexOf(args, "--hero-motion-metrics");
                if (metrics >= 0)
                {
                    if ((heroMotionComparison == null && knightReview == null) || metrics != Array.LastIndexOf(args, "--hero-motion-metrics") || metrics + 1 >= args.Length || args[metrics + 1].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException("--hero-motion-metrics requires a directory and --hero-motion or --knight-review.");
                    heroMotionMetricsDirectory = Path.GetFullPath(args[metrics + 1]);
                    heroMotionMetricsCapture = Array.IndexOf(args, "--autoplay-capture") >= 0 || Array.IndexOf(args, "--offline-replay-capture") >= 0;
                    heroMotionMetrics = new HeroMotionComparisonMetrics();
                }
            }
            if (world == null || ReferenceEquals(configuredMotionWorld, world)) return;
            configuredMotionWorld = world;
            if (heroMotionComparison != null)
            {
                world.PrepareHeroMotionPreview(heroMotionComparison.RenderMode);
                if (cameraScaleComparison == null) world.ReviewCameraMagnification = 1;
            }
            else world.PrepareHeroKnight();
            world.EdgeWarningsEnabled = knightReview?.EdgeWarnings ?? true;
        }
        // Call at Update entry, before submitting this frame's pose, including benchmark mode.
        void RecordHeroMotionComparisonFrame()
        {
            if (heroMotionMetrics == null || heroMotionMetricsFinished || CurrentFrame == null || (waveBenchmarkRequested && (!waveBenchmarkReady || benchmarkWarmFrames > 0 || waveBenchmarkEnded))) return;
            var foregroundInterval = Application.isFocused && (!waveBenchmarkRequested ||
                benchmarkForeground.Focused && !benchmarkForeground.Paused &&
                (benchmarkFocusEvents.Count == 0 || benchmarkFocusEvents[benchmarkFocusEvents.Count - 1].frame < Time.frameCount - 1));
            heroMotionMetrics.RecordCompletedFrame(Time.frameCount - 1, CurrentFrame.Tick,
                CurrentFrame.Enemies.Count, Time.unscaledDeltaTime * 1000d,
                world?.HeroMotionLiveMetrics?.LastPoseMilliseconds ?? -1, foregroundInterval);
        }
        // Call on benchmark completion and OnDestroy. Idempotent across both paths.
        void FinishHeroMotionComparisonMetrics(bool replayVerified = false, bool benchmarkEligible = false)
        {
            if (heroMotionMetrics == null || heroMotionMetricsFinished) return;
            heroMotionMetricsFinished = true;
            try { heroMotionMetrics.Save(heroMotionMetricsDirectory, knightReview != null ? "a-knight-live" : heroMotionComparison.Mode,
                benchmarkReplaySha ?? "not-a-replay-capture", Session?.ComputeStateHash() ?? "unavailable", heroMotionMetricsCapture,
                replayVerified, benchmarkEligible); }
            finally { heroMotionMetrics.Dispose(); }
        }
    }
}
