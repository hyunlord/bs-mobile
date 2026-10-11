using System;
using System.IO;

namespace Game.App
{
    public sealed class CrowdReplayBenchmark
    {
        public const string ReplaySha256 = "4fe0f97f23d20b487730be03ae76f5d7551ec88b787d43566f0cde41cd5a079c";
        public const string DataHash = "51DA41A20DF37E4960AFB157AC7AA83D20C9AB1FC7E6B5E6D85643BC9C438B74";
        public const string TerminalHash = "59805294BB813DA1D34D1CF505F49A42513975DE373696EA247FB1B5B8C43619";
        public const int StartTick = 10800, EndTick = 11530;
        public bool HeroRig { get; }
        CrowdReplayBenchmark(bool heroRig) { HeroRig = heroRig; }
        public static bool InWindow(int tick) => tick >= StartTick && tick <= EndTick;
        public static CrowdReplayBenchmark Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--crowd-replay-benchmark");
            if (index < 0)
            {
                if (Array.IndexOf(args, "--crowd-hero-b") >= 0) throw new ArgumentException("--crowd-hero-b requires --crowd-replay-benchmark.");
                return null;
            }
            if (Array.LastIndexOf(args, "--crowd-replay-benchmark") != index || Array.IndexOf(args, "--wave-benchmark") < 0)
                throw new ArgumentException("Crowd mode requires one flag and --wave-benchmark replay path.");
            foreach (var forbidden in new[] { "--wave-benchmark-mode", "--wave-benchmark-capture", "--autoplay-capture", "--smoothness-trace" })
                if (Array.IndexOf(args, forbidden) >= 0) throw new ArgumentException("Capture-free normal presentation forbids " + forbidden);
            var rig = Array.IndexOf(args, "--crowd-hero-b");
            if (rig >= 0 && Array.LastIndexOf(args, "--crowd-hero-b") != rig) throw new ArgumentException("Duplicate hero rig flag.");
            return new CrowdReplayBenchmark(rig >= 0);
        }
        public void Validate(string replayHash, string dataHash, int terminalTick, string terminalHash)
        {
            if (replayHash != ReplaySha256 || dataHash != DataHash || terminalTick != EndTick || terminalHash != TerminalHash)
                throw new InvalidDataException("Crowd replay provenance or terminal contract mismatch.");
        }
    }
}
