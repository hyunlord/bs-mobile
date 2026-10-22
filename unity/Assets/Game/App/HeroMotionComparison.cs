using System;

namespace Game.App
{
    // A presentation-only opt-in. Both modes must replay the same authoritative input tape.
    public sealed class HeroMotionComparison
    {
        public string Mode { get; }
        public int RenderMode => Mode == "baked" ? 1 : 2;
        public const string Contract = "hero-current-b-parts-iron-blade-v1";
        HeroMotionComparison(string mode) { Mode = mode; }

        public static HeroMotionComparison Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--hero-motion");
            if (index < 0) return null;
            if (index != Array.LastIndexOf(args, "--hero-motion") || index + 1 >= args.Length)
                throw new ArgumentException("--hero-motion requires exactly one baked or live value.");
            var mode = args[index + 1];
            if (mode != "baked" && mode != "live") throw new ArgumentException("Unknown hero motion comparison mode.");
            foreach (var incompatible in new[] { "--hero-concept", "--feel-comparison", "--feel-trial" })
                if (Array.IndexOf(args, incompatible) >= 0)
                    throw new ArgumentException("Hero motion comparison cannot change hero parts or gameplay rules: " + incompatible);
            return new HeroMotionComparison(mode);
        }
    }
    public sealed class HeroMotionComparisonWindow
    {
        public int StartTick { get; }
        public int EndTick { get; }
        HeroMotionComparisonWindow(int start, int end) { StartTick = start; EndTick = end; }
        public static HeroMotionComparisonWindow Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--hero-motion-window");
            if (index < 0) return null;
            if (index != Array.LastIndexOf(args, "--hero-motion-window") || index + 1 >= args.Length ||
                HeroMotionComparison.Parse(args) == null || Array.IndexOf(args, "--crowd-replay-benchmark") < 0)
                throw new ArgumentException("Hero motion window requires one early/evolved value, hero mode and the authentic crowd replay.");
            switch (args[index + 1])
            {
                case "early": return new HeroMotionComparisonWindow(0, 900);
                case "evolved": return new HeroMotionComparisonWindow(CrowdReplayBenchmark.StartTick, CrowdReplayBenchmark.EndTick);
                default: throw new ArgumentException("Unknown hero motion window.");
            }
        }
    }
}
