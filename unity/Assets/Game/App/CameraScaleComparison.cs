using System;

namespace Game.App
{
    public sealed class CameraScaleComparison
    {
        public float Magnification { get; }
        public string Window { get; }
        public int StartTick { get; }
        public int EndTick => StartTick + 900;
        public int PreRollTick => Math.Max(0, StartTick - 30);
        CameraScaleComparison(float scale, string window, int start)
        { Magnification = scale; Window = window; StartTick = start; }
        public bool InWindow(int tick) => tick >= StartTick && tick <= EndTick;
        public bool CaptureFrame(int tick, float alpha) => InWindow(tick) && (tick > StartTick || alpha > 0);
        public static CameraScaleComparison Parse(string[] args)
        {
            var scale = Array.IndexOf(args, "--camera-scale");
            var window = Array.IndexOf(args, "--camera-scale-window");
            if (scale < 0 && window < 0) return null;
            if (scale < 0 || window < 0 || scale + 1 >= args.Length || window + 1 >= args.Length ||
                scale != Array.LastIndexOf(args, "--camera-scale") || window != Array.LastIndexOf(args, "--camera-scale-window") ||
                Array.IndexOf(args, "--wave-benchmark") < 0 || HeroMotionComparison.Parse(args)?.Mode != "live")
                throw new ArgumentException("Camera review requires one scale, one window, frozen replay and --hero-motion live.");
            foreach (var forbidden in new[] { "--crowd-replay-benchmark", "--crowd-hero-b", "--hero-motion-window", "--hero-concept", "--feel-comparison", "--feel-trial", "--impact-feedback-baseline", "--wave-benchmark-repeat", "--wave-benchmark-profile", "--wave-benchmark-core-diagnostics", "--wave-benchmark-capture", "--wave-benchmark-mode", "--wave-benchmark-skip-grounding", "--crowd-target60-experiment", "--autoplay-capture", "--smoothness-trace" })
                if (Array.IndexOf(args, forbidden) >= 0) throw new ArgumentException("Camera review cannot vary another condition: " + forbidden);
            var value = args[scale + 1] == "1" ? 1f : args[scale + 1] == "1.4" ? 1.4f : args[scale + 1] == "1.8" ? 1.8f : 0;
            if (value == 0) throw new ArgumentException("Camera scale must be 1, 1.4 or 1.8.");
            var name = args[window + 1];
            var start = name == "early" ? 0 : name == "mid" ? 10800 : name == "dense" ? 13500 : -1;
            if (start < 0) throw new ArgumentException("Camera window must be early, mid or dense.");
            return new CameraScaleComparison(value, name, start);
        }
    }
}
