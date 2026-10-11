using System;
using SowSiege.Core;

namespace Game.App
{
    public sealed class FeelTrialOptions
    {
        public const int Seed = 30001;
        public const int DurationSeconds = 180;
        public const string InputPolicy = "growth-evasive-policy-v1";
        public string Mode { get; }
        public bool Feedback => Mode == "speed-feedback" || Mode == "all";
        public FeelExperimentOptions Rules => new FeelExperimentOptions(true, Mode == "speed-contact" || Mode == "all", Mode == "all");
        FeelTrialOptions(string mode) { Mode = mode; }
        public static FeelTrialOptions Parse(string[] args)
        {
            var index = Array.IndexOf(args, "--feel-trial");
            if (index < 0) return null;
            if (Array.LastIndexOf(args, "--feel-trial") != index || index + 1 >= args.Length)
                throw new ArgumentException("--feel-trial requires one mode.");
            var mode = args[index + 1];
            if (mode != "speed" && mode != "speed-feedback" && mode != "speed-contact" && mode != "all")
                throw new ArgumentException("Unknown feel trial mode.");
            foreach (var flag in args)
                if (flag.StartsWith("--capture", StringComparison.Ordinal) || flag == "--autoplay-capture" || flag == "--golden-minute" || flag == "--normal-gc-probe" || flag == "--smoothness-trace" || flag.StartsWith("--wave-benchmark", StringComparison.Ordinal) || flag == "--visual-review")
                    throw new ArgumentException("Feel trial cannot run with recording or another diagnostic mode: " + flag);
            return new FeelTrialOptions(mode);
        }
    }

    public sealed class FeelTrialDamage
    {
        long lastEvent = -1;
        public int Hits { get; private set; }
        public long LostHealth { get; private set; }
        public void Observe(WaveEvent value)
        {
            if (value.Id <= lastEvent) return;
            lastEvent = value.Id;
            if (value.Kind != "lord-hit" || value.Amount <= 0) return;
            Hits++;
            LostHealth += value.Amount;
        }
    }
}
