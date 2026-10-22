using System.IO;
using Game.App;
using Game.App.Generated;
using Game.Editor;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class MeleeAimReplayParityTests
    {
        [Test]
        public void MonoVerifiesReviewV6FromDotNetWithoutChangingHistoricalFixtures()
        {
            if (CatalogRunOptions.MeleeAim(CanonicalContent.CreateCatalog()) == null)
                Assert.Ignore("Requires the opt-in melee aim catalog export.");
            const int seed = 30001;
            using var input = File.OpenRead(Path.Combine(FoundationBuild.MeleeAimFixtureDirectory, seed + ".ssreplay"));
            var replay = ReplayCodec.Read(input);
            var result = FoundationBuild.VerifyMeleeAimReplayFixture(replay, seed);
            Assert.That(result.Tick, Is.EqualTo(600));
            Assert.That(result.StateHash, Is.EqualTo(replay.End.StateHash));
            Assert.That(replay.Header.FormatVersion, Is.EqualTo(ReplayCodec.MeleeAimVersion));
            var output = Path.Combine(FoundationBuild.RepoRoot, "artifacts/m1-256/editor-parity");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, seed + ".txt"),
                "scope=600 ordinary-input ticks; Unity Editor Mono; not native player or device acceptance\n"
                + seed + "\t" + result.Tick + "\t" + result.StateHash + "\t" + CanonicalContent.DataHash + "\n");
        }
    }
}
