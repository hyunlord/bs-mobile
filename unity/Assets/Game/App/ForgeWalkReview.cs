using System;
using System.IO;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    public sealed class ForgeWalkReview
    {
        [Serializable] public sealed class Definition
        {
            public string contract, profile, direction, dataHash, replaySha256, stateHash;
            public int seed, captureStartTick, endTick;
            public float cameraScale;
        }
        public Definition Case { get; private set; }

        public static ForgeWalkReview Parse(string[] args)
        {
            var at = Array.IndexOf(args, "--forge-walk-review");
            if (at < 0) return null;
            if (at != Array.LastIndexOf(args, "--forge-walk-review") || Array.IndexOf(args, "--offline-replay-capture") < 0 ||
                Array.IndexOf(args, "--hero-production") < 0)
                throw new ArgumentException("Forge walk requires one explicit flag, external hero atlas and offline capture.");
            foreach (var flag in new[] { "--knight-review", "--m1-combat-case", "--m1-weapon-showcase", "--m1-review-seconds", "--camera-scale", "--camera-scale-window", "--crowd-replay-benchmark", "--hero-motion", "--hero-motion-window", "--hero-concept", "--hero-production-terminal-tail", "--autoplay-capture", "--feel-comparison", "--feel-trial", "--wave-benchmark-repeat", "--wave-benchmark-profile", "--wave-benchmark-core-diagnostics", "--wave-benchmark-capture", "--wave-benchmark-mode", "--wave-benchmark-skip-grounding", "--crowd-target60-experiment", "--smoothness-trace" })
                if (Array.IndexOf(args, flag) >= 0) throw new ArgumentException("Forge walk cannot mix review contracts: " + flag);
            var replay = ReadValue(args, "--wave-benchmark");
            if (!Path.IsPathRooted(replay) || Path.GetExtension(replay) != ".ssreplay")
                throw new ArgumentException("Forge walk requires an absolute replay path.");
            if (ReadValue(args, "--hero-production-direction") != "E")
                throw new ArgumentException("Forge walk contains only the authored east direction.");
            var value = JsonUtility.FromJson<Definition>(File.ReadAllText(Path.ChangeExtension(replay, ".json")));
            if (value == null || value.contract != "forge-walk-review-v1" || value.profile != "wave-1a" || value.seed != 30001 ||
                value.captureStartTick != 0 || value.endTick != 900 || value.direction != "E" || value.cameraScale != 1.4f ||
                !ValidHash(value.dataHash) || !ValidHash(value.replaySha256) || !ValidHash(value.stateHash))
                throw new InvalidDataException("Invalid thirty-second Forge walk fixture contract.");
            return new ForgeWalkReview { Case = value };
        }

        public void ValidateReplay(ReplayDocument replay, string replaySha, string dataHash)
        {
            var options = replay.Header.Options;
            if (replaySha != Case.replaySha256 || dataHash != Case.dataHash || options.DataHash != dataHash ||
                replay.End.Tick != Case.endTick || replay.End.StateHash != Case.stateHash || replay.End.Kind != ReplayEndKind.Quit ||
                options.Run.Seed != Case.seed || options.Run.Scenario != "normal" || !options.Run.ManualCards ||
                options.Run.Policy != "mixed" || options.Run.PeopleRule != null || options.Run.Movement != null || options.Run.TargetMaterial != null ||
                options.InitialAimMode != AimMode.Movement || options.Field == null || options.Field.Version != 1 ||
                options.HostRewards != null || options.MeleeAim != null || options.Feel == null || !options.Feel.FasterLord ||
                options.Feel.ImmediateContact || options.Feel.ReadableHealth)
                throw new InvalidDataException("Forge walk replay, catalog or ordinary-run options differ from its fixture.");
            var advances = 0;
            foreach (var command in replay.Commands)
            {
                if (command.Kind == ReplayCommandKind.Advance)
                {
                    if (command.Input.X != 1000 || command.Input.Y != 0) throw new InvalidDataException("Forge walk requires constant east movement.");
                    advances++;
                }
                else if (command.Kind != ReplayCommandKind.ChooseCard)
                    throw new InvalidDataException("Forge walk allows only ordinary movement and earned card selection.");
            }
            if (advances != 900) throw new InvalidDataException("Forge walk must contain exactly thirty seconds of accepted input.");
        }

        static string ReadValue(string[] args, string flag)
        {
            var at = Array.IndexOf(args, flag);
            if (at < 0 || at != Array.LastIndexOf(args, flag) || at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Expected one value for " + flag);
            return args[at + 1];
        }
        static bool ValidHash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
    }
}
