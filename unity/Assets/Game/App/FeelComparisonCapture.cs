using System;
using UnityEngine;

namespace Game.App
{
    public sealed class FeelComparisonCapture
    {
        public string Mode { get; }
        public bool RequestedRulesOn => Mode == "rules-on";
        public bool RequestedFeedback => RequestedRulesOn;
        public bool RequestedHeroB => Mode == "walk-b";
        public bool Walk => Mode == "walk-a" || Mode == "walk-b";
        public string InputProtocol => Walk ? "tick-walk-right-v1" : "tick-circle-30s-v1";
        public CaptureSegmentWindow Window => new CaptureSegmentWindow(Walk ? 0 : 360, Walk ? 5 : 30, !Walk);

        FeelComparisonCapture(string mode) { Mode = mode; }

        public static FeelComparisonCapture Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--feel-comparison");
            if (index < 0) return null;
            if (Array.LastIndexOf(args, "--feel-comparison") != index || index + 1 >= args.Length)
                throw new ArgumentException("--feel-comparison requires one comparison mode.");
            var mode = args[index + 1];
            if (mode != "walk-a" && mode != "walk-b" && mode != "rules-off" && mode != "rules-on")
                throw new ArgumentException("Unknown feel comparison mode.");
            if (Array.IndexOf(args, "--autoplay-capture") < 0)
                throw new ArgumentException("Feel comparison requires isolated --autoplay-capture.");
            foreach (var flag in new[] { "--capture-start-seconds", "--capture-duration-seconds", "--capture-landscape", "--capture-weak-build", "--capture-movement" })
                if (Array.IndexOf(args, flag) >= 0) throw new ArgumentException("Feel comparison owns its window and movement; incompatible flag: " + flag);
            return new FeelComparisonCapture(mode);
        }

        public static CaptureSegmentWindow ResolveWindow(string[] args) => Parse(args)?.Window ?? CaptureSegmentWindow.Parse(args);

        public Vector2 Direction(int tick, int tickRate)
        {
            if (tick < 0 || tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (Walk) return Vector2.right;
            var phase = (tick % (tickRate * 30)) * (Math.PI * 2 / (tickRate * 30));
            return new Vector2((float)Math.Cos(phase), (float)Math.Sin(phase));
        }
    }
}
