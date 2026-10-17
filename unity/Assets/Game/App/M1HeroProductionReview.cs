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
        public static M1HeroProductionReview Parse(string[] args)
        {
            var at = Array.IndexOf(args,"--hero-production");
            var directionAt = Array.IndexOf(args,"--hero-production-direction");
            if (at < 0)
            {
                if (directionAt >= 0) throw new ArgumentException("Production direction requires --hero-production.");
                return null;
            }
            if (KnightReview.Parse(args) == null) throw new ArgumentException("Production comparison requires --knight-review.");
            var path = ReadValue(args,"--hero-production");
            if (!Path.IsPathRooted(path) || Path.GetExtension(path) != ".json") throw new ArgumentException("Hero production requires an absolute manifest.json path.");
            var direction = directionAt < 0 ? "E" : ReadValue(args,"--hero-production-direction");
            switch (direction)
            {
                case "follow": case "E": case "NE": case "N": case "NW": case "W": case "SW": case "S": case "SE": break;
                default: throw new ArgumentException("Production direction must be a compass direction or follow.");
            }
            using var hash = SHA256.Create();
            var digest = hash.ComputeHash(File.ReadAllBytes(path));
            return new M1HeroProductionReview { ManifestPath = Path.GetFullPath(path), Direction = direction,
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
