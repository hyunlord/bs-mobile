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
        public bool FailurePreview { get; }
        public bool WalkLandscape { get; }
        public bool WalkFast { get; }
        public string WalkHeading { get; }
        public string MovementConfiguration { get; }
        public bool Landscape => !Walk || WalkLandscape;
        public string InputProtocol => Walk ? "tick-walk-"+WalkHeading+"-v1" : MovementConfiguration == "evasive" ? "growth-evasive-policy-v1" : "growth-policy-v1";
        public CaptureSegmentWindow Window => new CaptureSegmentWindow(Walk || FailurePreview ? 0 : 360, Walk ? 5 : 30, !Walk && !FailurePreview);

        FeelComparisonCapture(string mode, bool failurePreview, string movement, bool walkLandscape, bool walkFast, string heading) { WalkHeading=heading; Mode = mode; FailurePreview = failurePreview; MovementConfiguration = movement; WalkLandscape=walkLandscape; WalkFast=walkFast; }

        public static FeelComparisonCapture Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--feel-comparison");
            if (index < 0)
            {
                if (Array.IndexOf(args, "--feel-failure-preview") >= 0) throw new ArgumentException("Failure preview requires a rules comparison.");
                if (Array.IndexOf(args,"--feel-walk-landscape")>=0 || Array.IndexOf(args,"--feel-walk-fast")>=0 || Array.IndexOf(args,"--feel-walk-heading")>=0) throw new ArgumentException("Walk options require a walk comparison.");
                return null;
            }
            if (Array.LastIndexOf(args, "--feel-comparison") != index || index + 1 >= args.Length)
                throw new ArgumentException("--feel-comparison requires one comparison mode.");
            var mode = args[index + 1];
            if (mode != "walk-a" && mode != "walk-b" && mode != "rules-off" && mode != "rules-on")
                throw new ArgumentException("Unknown feel comparison mode.");
            if (Array.IndexOf(args, "--autoplay-capture") < 0)
                throw new ArgumentException("Feel comparison requires isolated --autoplay-capture.");
            foreach (var flag in new[] { "--capture-start-seconds", "--capture-duration-seconds", "--capture-landscape", "--capture-weak-build" })
                if (Array.IndexOf(args, flag) >= 0) throw new ArgumentException("Feel comparison owns its window and movement; incompatible flag: " + flag);
            var preview = Array.IndexOf(args, "--feel-failure-preview");
            var movementIndex = Array.IndexOf(args, "--capture-movement");
            var walk = mode == "walk-a" || mode == "walk-b";
            var landscape=Array.IndexOf(args,"--feel-walk-landscape");
            var fast=Array.IndexOf(args,"--feel-walk-fast");
            var headingIndex=Array.IndexOf(args,"--feel-walk-heading");
            var heading="right";
            if(headingIndex>=0)
            {
                if(!walk||Array.LastIndexOf(args,"--feel-walk-heading")!=headingIndex||headingIndex+1>=args.Length)
                    throw new ArgumentException("Walk heading requires one walk-only value.");
                heading=args[headingIndex+1];
                if(heading!="right"&&heading!="up"&&heading!="down"&&heading!="diagonal")throw new ArgumentException("Unknown walk heading.");
            }
            if ((!walk&&(landscape>=0||fast>=0)) || (landscape>=0&&Array.LastIndexOf(args,"--feel-walk-landscape")!=landscape) || (fast>=0&&Array.LastIndexOf(args,"--feel-walk-fast")!=fast)) throw new ArgumentException("Walk options require a walk comparison and may appear once.");
            if (walk && (preview >= 0 || movementIndex >= 0)) throw new ArgumentException("Failure preview and movement configuration are rules-only.");
            if (preview >= 0 && Array.LastIndexOf(args, "--feel-failure-preview") != preview) throw new ArgumentException("Duplicate failure preview flag.");
            var movement = "evasive";
            if (movementIndex >= 0)
            {
                if (Array.LastIndexOf(args, "--capture-movement") != movementIndex || movementIndex + 1 >= args.Length) throw new ArgumentException("Movement requires one default or evasive value.");
                movement = args[movementIndex + 1];
                if (movement != "default" && movement != "evasive") throw new ArgumentException("Movement requires default or evasive.");
            }
            return new FeelComparisonCapture(mode, preview >= 0, movement, landscape>=0, fast>=0, heading);
        }

        public static CaptureSegmentWindow ResolveWindow(string[] args) => Parse(args)?.Window ?? CaptureSegmentWindow.Parse(args);

        public Vector2 Direction(int tick, int tickRate)
        {
            if (tick < 0 || tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (Walk) return WalkHeading=="up"?Vector2.up:WalkHeading=="down"?Vector2.down:WalkHeading=="diagonal"?new Vector2(1,1).normalized:Vector2.right;
            throw new InvalidOperationException("Rules comparison uses the normal growth and evasion policy, not a fixed direction tape.");
        }
    }
}
