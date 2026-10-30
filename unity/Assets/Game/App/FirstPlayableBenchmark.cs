using System;
using System.IO;

namespace Game.App
{
    // FP01 is an explicit stress input, not a normal-play or C05 replacement (issue #443).
    public sealed class FirstPlayableBenchmark
    {
        public const string Fixture = "first-playable-fp01";
        public const string ReplaySha256 = "61aa591ea8f05dc1ec7b30968652df654aa423d824b786ede6145915fdc183ba";
        public const string DataHash = "6AC1ACFC4B2E80DD510EE0A41D0039BC05932A5FEF1776CC1B2A4BF2E928C36D";
        public const string TerminalHash = "69E7182A9FB4EBF154E85551F2A2C6696F8EFBD30A87EC3C1046DF593361CDB5";
        public const int StartTick = 19891, EndTick = 20790, TerminalTick = 27000, Commands = 27052;
        FirstPlayableBenchmark() { }
        public static FirstPlayableBenchmark Parse(string[] args, string profile)
        {
            var index = Array.IndexOf(args, "--first-playable-benchmark");
            if (index < 0)
            {
                if (profile == "first-playable" && Array.IndexOf(args, "--wave-benchmark") >= 0)
                    throw new ArgumentException("first-playable performance requires --first-playable-benchmark (FP01).");
                return null;
            }
            if (profile != "first-playable" || index != Array.LastIndexOf(args, "--first-playable-benchmark") || Array.IndexOf(args, "--wave-benchmark") < 0)
                throw new ArgumentException("FP01 requires first-playable, --wave-benchmark and one --first-playable-benchmark flag.");
            foreach (var flag in new[] { "--offline-replay-capture", "--offline-replay-audio", "--wave-benchmark-capture", "--recorded-replay-window", "--crowd-replay-benchmark", "--crowd-target60-experiment", "--m1-combat-case", "--knight-review", "--camera-scale", "--camera-scale-window", "--hero-motion-window", "--wave-benchmark-skip-grounding" })
                if (Array.IndexOf(args, flag) >= 0) throw new ArgumentException("FP01 cannot be combined with " + flag + ".");
            return new FirstPlayableBenchmark();
        }
        public void Validate(string replaySha, string recordedDataHash, string currentDataHash, int tick, string hash, long commands)
        {
            if (replaySha != ReplaySha256 || recordedDataHash != DataHash || currentDataHash != DataHash || tick != TerminalTick || hash != TerminalHash || commands != Commands)
                throw new InvalidDataException("FP01 replay, catalog, terminal or accepted command count differs from the frozen contract.");
        }
    }
}
