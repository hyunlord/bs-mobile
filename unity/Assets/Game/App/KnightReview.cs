using System;
using System.IO;

namespace Game.App
{
    public sealed class KnightReview
    {
        public const string C05Sha256 = "f8d65a26efa2d7caad0edeec459418f1e269f7aed80980a8c7142cd7481cd9e5";
        public const string DamageSha256 = "07f211d9d8a1e7a22110d7438c29e69106e6bf619cce681b2b89c28527b9cec6";
        public const string DamageTerminalHash = "F57632711CE0294F63A95668695B5281EED15E9788D00C769730A701C52F11F6";
        public bool DamageReplay { get; }
        public bool EdgeWarnings { get; }
        public bool ImpactFeedback { get; }
        KnightReview(bool damage, bool edge, bool impact) { DamageReplay = damage; EdgeWarnings = edge; ImpactFeedback = impact; }
        public static KnightReview Parse(string[] args)
        {
            var knight = Array.IndexOf(args, "--knight-review");
            if (knight < 0)
            {
                if (Array.IndexOf(args, "--knight-damage-replay") >= 0 || Array.IndexOf(args, "--edge-warnings-off") >= 0)
                    throw new ArgumentException("Knight-only diagnostic flags require --knight-review.");
                return null;
            }
            if (knight != Array.LastIndexOf(args, "--knight-review") || Array.IndexOf(args, "--wave-benchmark") < 0)
                throw new ArgumentException("Knight review requires one flag and an explicit fixed replay benchmark.");
            foreach (var incompatible in new[] { "--hero-motion", "--hero-motion-window", "--hero-concept", "--crowd-replay-benchmark", "--feel-comparison", "--feel-trial" })
                if (Array.IndexOf(args, incompatible) >= 0) throw new ArgumentException("Knight review cannot change hero/rules: " + incompatible);
            foreach (var flag in new[] { "--knight-damage-replay", "--edge-warnings-off", "--impact-feedback-baseline" })
                if (Array.IndexOf(args, flag) != Array.LastIndexOf(args, flag)) throw new ArgumentException("Duplicate flag: " + flag);
            return new KnightReview(Array.IndexOf(args, "--knight-damage-replay") >= 0,
                Array.IndexOf(args, "--edge-warnings-off") < 0, Array.IndexOf(args, "--impact-feedback-baseline") < 0);
        }
        public void Validate(string sha, int endTick, string terminalHash)
        {
            if (sha != (DamageReplay ? DamageSha256 : C05Sha256)) throw new InvalidDataException("Knight replay identity mismatch.");
            if (DamageReplay && (endTick != 13643 || terminalHash != DamageTerminalHash)) throw new InvalidDataException("Distinct damage sample terminal identity mismatch.");
        }
    }
}
