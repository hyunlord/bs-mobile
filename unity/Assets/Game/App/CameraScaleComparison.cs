using System;

namespace Game.App
{
    public sealed class CameraScaleComparison
    {
        public float Magnification { get; }
        public string Window { get; }
        public int StartTick { get; }
        public int EndTick { get; }
        public int PreRollTick => Math.Max(0, StartTick - 30);
        CameraScaleComparison(float scale, string window, int start, int end)
        { Magnification = scale; Window = window; StartTick = start; EndTick = end; }
        public bool InWindow(int tick) => tick >= StartTick && tick <= EndTick;
        public bool CaptureFrame(int tick, float alpha) => InWindow(tick) && (tick > StartTick || alpha > 0);
        public static CameraScaleComparison Parse(string[] args)
        {
            var knight = KnightReview.Parse(args);
            var scale = Array.IndexOf(args, "--camera-scale");
            var window = Array.IndexOf(args, "--camera-scale-window");
            if (scale < 0 && window < 0 && knight == null) return null;
            if ((knight == null && (scale < 0 || window < 0)) || (scale >= 0 && scale + 1 >= args.Length) || (window >= 0 && window + 1 >= args.Length) ||
                scale != Array.LastIndexOf(args, "--camera-scale") || window != Array.LastIndexOf(args, "--camera-scale-window") ||
                Array.IndexOf(args, "--wave-benchmark") < 0 || (knight == null && HeroMotionComparison.Parse(args)?.Mode != "live"))
                throw new ArgumentException("Camera review requires one scale, one window, frozen replay and --hero-motion live.");
            foreach (var forbidden in new[] { "--crowd-replay-benchmark", "--crowd-hero-b", "--hero-motion-window", "--hero-concept", "--feel-comparison", "--feel-trial", "--wave-benchmark-repeat", "--wave-benchmark-profile", "--wave-benchmark-core-diagnostics", "--wave-benchmark-capture", "--wave-benchmark-mode", "--wave-benchmark-skip-grounding", "--crowd-target60-experiment", "--autoplay-capture", "--smoothness-trace" })
                if (Array.IndexOf(args, forbidden) >= 0) throw new ArgumentException("Camera review cannot vary another condition: " + forbidden);
            if (knight == null && Array.IndexOf(args, "--impact-feedback-baseline") >= 0)
                throw new ArgumentException("Impact baseline requires the explicit knight review.");
            var text = scale < 0 ? "1.4" : args[scale + 1];
            var value = text == "1" ? 1f : text == "1.4" ? 1.4f : text == "1.8" && knight == null ? 1.8f : 0;
            if (value == 0) throw new ArgumentException("Camera scale must be 1, 1.4 or 1.8.");
            var name = window < 0 ? knight.DamageReplay ? "damage" : "early" : args[window + 1];
            if (knight != null && knight.DamageReplay != (name == "damage")) throw new ArgumentException("Distinct damage replay requires its damage window only.");
            var start = name == "early" ? 0 : name == "mid" ? 10800 : name == "dense" || name == "damage" && knight?.DamageReplay == true ? 13500 : -1;
            if (start < 0) throw new ArgumentException("Camera window must be early, mid or dense.");
            return new CameraScaleComparison(value, name, start, name == "damage" ? 13643 : start + 900);
        }
    }
}
