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
        // Provenance (ADR0058 보충, 2026-10-21): the replay file, the data hash it was recorded with and its recorded terminal tick
        // and state stay pinned. The current data tree may differ from the recorded one; the replay still runs on its own recorded
        // options and passes when its actual terminal state equals TerminalHash (TerminalMatches). A different current data hash is
        // returned as a warning for the log and the result, not a failure. Re-pinning is needed only when the terminal state changes.
        public string Validate(string replayHash, string recordedDataHash, string currentDataHash, int terminalTick, string terminalHash)
        {
            if (replayHash != ReplaySha256 || recordedDataHash != DataHash || terminalTick != EndTick || terminalHash != TerminalHash)
                throw new InvalidDataException("Crowd replay provenance or terminal contract mismatch.");
            return currentDataHash == DataHash ? null : DataHashWarning(currentDataHash);
        }
        public static string DataHashWarning(string currentDataHash) =>
            "crowd-411 data hash differs: 기록 당시 " + Short(DataHash) + "… / 현재 " + Short(currentDataHash) + "…";
        public static bool TerminalMatches(string actualTerminalHash) => actualTerminalHash == TerminalHash;
        static string Short(string hash) => string.IsNullOrEmpty(hash) ? "none" : hash.Substring(0, Math.Min(8, hash.Length));
    }
}
