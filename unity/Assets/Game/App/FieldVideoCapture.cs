using System;
using UnityEngine;

namespace Game.App
{
    // Separate from five-second animation comparisons: actual traversal from a fresh run.
    public sealed class FieldVideoCapture
    {
        public string Mode { get; }
        public string Topology { get; }
        public string InputProtocol => Mode == "straight" ? "field-right-60s-v1" : "field-right-60s-left-60s-v1";
        public CaptureSegmentWindow Window => CaptureSegmentWindow.FieldTraversal(Mode == "out-return");
        FieldVideoCapture(string mode, string topology) { Mode = mode; Topology = topology; }
        public static FieldVideoCapture Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--field-video");
            if (index < 0)
            {
                if (Array.IndexOf(args, "--field-topology") >= 0) throw new ArgumentException("--field-topology requires --field-video.");
                return null;
            }
            if (Array.LastIndexOf(args, "--field-video") != index || index + 1 >= args.Length)
                throw new ArgumentException("--field-video requires one straight or out-return mode.");
            var mode = args[index + 1];
            if (mode != "straight" && mode != "out-return") throw new ArgumentException("Unknown field video mode.");
            if (Array.IndexOf(args, "--autoplay-capture") < 0) throw new ArgumentException("Field video requires isolated --autoplay-capture.");
            foreach (var flag in new[] { "--feel-comparison", "--feel-trial", "--normal-gc-probe", "--smoothness-trace", "--capture-movement", "--capture-priority", "--capture-target-material", "--capture-start-seconds", "--capture-duration-seconds", "--capture-landscape", "--capture-weak-build", "--feel-failure-preview" })
                if (Array.IndexOf(args, flag) >= 0) throw new ArgumentException("Field video owns its route, window and ordinary card policy: " + flag);
            var topology = "field";
            var t = Array.IndexOf(args, "--field-topology");
            if (t >= 0)
            {
                if (Array.LastIndexOf(args, "--field-topology") != t || t + 1 >= args.Length) throw new ArgumentException("--field-topology requires one old or field value.");
                topology = args[t + 1];
                if (topology != "old" && topology != "field") throw new ArgumentException("Unknown field topology.");
            }
            return new FieldVideoCapture(mode, topology);
        }
        public Vector2 Direction(int tick, int tickRate)
        {
            if (tick < 0 || tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            return Mode == "out-return" && tick >= checked(60 * tickRate) ? Vector2.left : Vector2.right;
        }
    }
}
