using System;
using UnityEngine;

namespace Game.App
{
    // Only presentation differs. RunCoordinator samples Direction once per authoritative tick.
    public sealed class ImpactComparisonCapture
    {
        public string Mode { get; }
        public bool Enabled => Mode == "applied";
        public string InputProtocol => "impact-square-tick-tape-v1";
        public CaptureSegmentWindow Window => new CaptureSegmentWindow(0, 30);
        ImpactComparisonCapture(string mode) { Mode = mode; }

        public static ImpactComparisonCapture Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--impact-comparison");
            if (index < 0) return null;
            if (Array.LastIndexOf(args, "--impact-comparison") != index || index + 1 >= args.Length)
                throw new ArgumentException("--impact-comparison requires one baseline or applied value.");
            var mode = args[index + 1];
            if (mode != "baseline" && mode != "applied") throw new ArgumentException("Unknown impact comparison mode.");
            if (Array.IndexOf(args, "--autoplay-capture") < 0)
                throw new ArgumentException("Impact comparison requires isolated --autoplay-capture.");
            foreach (var flag in new[] { "--feel-comparison", "--feel-trial", "--field-video", "--capture-movement", "--capture-priority", "--capture-target-material", "--capture-start-seconds", "--capture-duration-seconds", "--capture-landscape", "--capture-weak-build" })
                if (Array.IndexOf(args, flag) >= 0) throw new ArgumentException("Impact comparison owns its fixed input and window: " + flag);
            return new ImpactComparisonCapture(mode);
        }

        public Vector2 Direction(int tick, int tickRate)
        {
            if (tick < 0 || tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            switch (tick / (tickRate * 5) % 4)
            {
                case 0: return Vector2.right;
                case 1: return Vector2.up;
                case 2: return Vector2.left;
                default: return Vector2.down;
            }
        }
    }
}
