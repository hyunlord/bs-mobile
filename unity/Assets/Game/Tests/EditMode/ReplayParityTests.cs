using System.IO;
using Game.App.Generated;
using Game.Editor;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class ReplayParityTests
    {
        [TestCase(30000)]
        [TestCase(30001)]
        [TestCase(30002)]
        [TestCase(30003)]
        [TestCase(30004)]
        public void MonoVerifiesEntireDotNetRecordedYear(int seed)
        {
            var path = Path.Combine(FoundationBuild.RepoRoot, "artifacts/phase1b/replays", seed + ".ssreplay");
            Assert.That(File.Exists(path), Is.True, "Run tools/check-unity.sh to generate canonical replay fixtures.");
            using (var input = File.OpenRead(path))
            {
                var result = FoundationBuild.VerifyReplayFixture(ReplayCodec.Read(input), seed);
                Assert.That(result.Seed, Is.EqualTo(seed));
                Assert.That(result.Tick, Is.EqualTo(27000));
                Assert.That(result.EndKind, Is.EqualTo(ReplayEndKind.Duration));
                var output = Path.Combine(FoundationBuild.RepoRoot, "artifacts/unity", "mono-" + seed + ".txt");
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, result.Seed + "\t" + result.Tick + "\t" + result.StateHash + "\n");
            }
        }
    }
}
