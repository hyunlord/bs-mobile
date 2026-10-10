using System;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // Separate from five-second animation comparisons: actual traversal from a fresh run.
    public sealed class FieldVideoCapture
    {
        public string Mode { get; }
        public string Topology { get; }
        public string InputProtocol => Mode == "straight" ? "field-right-60s-v1" : "field-right-60s-steer-home-until-120s-v2";
        public CaptureSegmentWindow Window => CaptureSegmentWindow.FieldTraversal(Mode == "out-return");
        public FieldTraversalEvidence Evidence { get; } = new FieldTraversalEvidence();
        bool observationClosed;
        public void CloseObservation() => observationClosed = true;
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
        public void Observe(int tick, int tickRate, WorldPoint position, WorldPoint home, float unitsPerTick)
        {
            if (tick < 0 || tickRate <= 0 || !(unitsPerTick > 0) || float.IsInfinity(unitsPerTick)) throw new ArgumentOutOfRangeException(nameof(tick));
            if (observationClosed || tick > checked(Window.EndSeconds * tickRate)) return;
            var dx = (double)position.X - home.X; var dy = (double)position.Y - home.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (!Evidence.observed)
            {
                Evidence.observed = true; Evidence.startTick = tick;
                Evidence.startX = position.X; Evidence.startY = position.Y;
                Evidence.homeX = home.X; Evidence.homeY = home.Y;
                Evidence.farthestX = position.X; Evidence.farthestY = position.Y; Evidence.farthestTick = tick;
            }
            if (home.X != Evidence.homeX || home.Y != Evidence.homeY) throw new InvalidOperationException("Traversal home changed during observation.");
            Evidence.endTick = tick; Evidence.endX = position.X; Evidence.endY = position.Y;
            Evidence.endHomeDistance = distance;
            Evidence.arrivalDeadzone = Math.Max(2, unitsPerTick * .25f);
            if (distance > Evidence.farthestHomeDistance)
            {
                Evidence.farthestHomeDistance = distance; Evidence.farthestTick = tick;
                Evidence.farthestX = position.X; Evidence.farthestY = position.Y;
            }
            Evidence.atHomeAtEnd = distance <= Evidence.arrivalDeadzone;
            if (Mode == "out-return" && tick >= checked(60 * tickRate) && Evidence.atHomeAtEnd && !Evidence.arrived)
            {
                Evidence.arrived = true; Evidence.arrivalTick = tick;
            }
        }
        public Vector2 Direction(int tick, int tickRate, WorldPoint position, WorldPoint home, float unitsPerTick)
        {
            Observe(tick, tickRate, position, home, unitsPerTick);
            if (Mode != "out-return" || tick < checked(60 * tickRate)) return Vector2.right;
            if (Evidence.atHomeAtEnd) return Vector2.zero;
            return CaptureArrivalSteering.Direction(new Vector2(home.X - position.X, home.Y - position.Y), unitsPerTick);
        }
    }

    [Serializable]
    public sealed class FieldTraversalEvidence
    {
        public bool observed, arrived, atHomeAtEnd;
        public int startTick, startX, startY, farthestTick, farthestX, farthestY, endTick, endX, endY, homeX, homeY;
        public int arrivalTick = -1;
        public double farthestHomeDistance, endHomeDistance, arrivalDeadzone;
        public string units = "authoritative Core world units; actual positions, not commanded displacement";
        public string boundary = "Arrival requires the observed lord position within speed-aware deadzone after 60 outbound gameplay seconds. Steering uses ordinary pointer input toward immutable home; no teleport, grants or forced survival. Arrival may be absent at death or 120-second limit.";
    }
}
