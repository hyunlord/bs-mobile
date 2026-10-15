using System;
using System.IO;
using System.Security.Cryptography;

namespace Game.App
{
    public sealed class M1HeroProductionReview
    {
        public string ManifestPath { get; private set; }
        public string Direction { get; private set; }
        public string ManifestSha256 { get; private set; }
        public int TerminalTailFrames {get;private set;}
        public bool WalkingPilot { get; private set; }
        public static M1HeroProductionReview Parse(string[] args)
        {
            var forge = ForgeWalkReview.Parse(args);
            var at = Array.IndexOf(args,"--hero-production");
            var directionAt = Array.IndexOf(args,"--hero-production-direction");
            if (at < 0)
            {
                if (directionAt >= 0) throw new ArgumentException("Production direction requires --hero-production.");
                return null;
            }
            if (forge == null && M1CombatReview.Parse(args) == null && KnightReview.Parse(args) == null) throw new ArgumentException("Production comparison requires an explicit knight, actual combat or Forge walk review.");
            var path = ReadValue(args,"--hero-production");
            if (!Path.IsPathRooted(path) || Path.GetExtension(path) != ".json") throw new ArgumentException("Hero production requires an absolute manifest.json path.");
            var direction = directionAt < 0 ? "follow" : ReadValue(args,"--hero-production-direction");
            switch (direction)
            {
                case "follow": case "E": case "NE": case "N": case "NW": case "W": case "SW": case "S": case "SE": break;
                default: throw new ArgumentException("Production direction must be a compass direction or follow.");
            }
            var tail=Array.IndexOf(args,"--hero-production-terminal-tail")>=0;
            if(tail && (Array.IndexOf(args,"--offline-replay-capture")<0 || Array.IndexOf(args,"--hero-production-terminal-tail")!=Array.LastIndexOf(args,"--hero-production-terminal-tail")))
                throw new ArgumentException("Terminal presentation tail requires one explicit offline capture flag.");
            using var hash = SHA256.Create();
            var digest = hash.ComputeHash(File.ReadAllBytes(path));
            return new M1HeroProductionReview { WalkingPilot=forge!=null, TerminalTailFrames=tail?60:0, ManifestPath = Path.GetFullPath(path), Direction = direction,
                ManifestSha256 = BitConverter.ToString(digest).Replace("-","").ToLowerInvariant() };
        }
        static string ReadValue(string[] args,string flag)
        {
            var at = Array.IndexOf(args,flag);
            if (at < 0 || at != Array.LastIndexOf(args,flag) || at + 1 >= args.Length || args[at + 1].StartsWith("--",StringComparison.Ordinal)) throw new ArgumentException("Expected one value for " + flag);
            return args[at + 1];
        }
    }
}
