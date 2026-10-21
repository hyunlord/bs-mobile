using System;
using System.IO;
using System.Linq;
using Game.App.Generated;
using Game.Editor;
using NUnit.Framework;
using SowSiege.Core;

namespace Tests.EditMode
{
    public sealed class WaveReplayParityTests
    {
        [TestCase(30000)]
        [TestCase(30001)]
        [TestCase(30002)]
        [TestCase(30003)]
        [TestCase(30004)]
        public void MonoVerifiesBoundedNormalInputFromDotNet(int seed)
        {
            if (CanonicalContent.ProfileName != "wave-1a")
                Assert.Ignore("Requires the isolated wave-1a export.");
            // WAVE_REPLAY_FIXTURES points at fixtures recorded elsewhere and is never overwritten; otherwise the default fixtures
            // are recorded with .NET on first use (FoundationBuild.EnsureWaveReplayFixtures), so no manual step is needed.
            var supplied = Environment.GetEnvironmentVariable("WAVE_REPLAY_FIXTURES");
            var directory = supplied ?? Path.Combine(FoundationBuild.RepoRoot, FoundationBuild.WaveReplayFixtureDirectory);
            FoundationBuild.EnsureWaveReplayFixtures(directory, generate: supplied == null);
            using var input = File.OpenRead(Path.Combine(directory, seed + ".ssreplay"));
            var replay = ReplayCodec.Read(input);
            Assert.That(replay.Commands.All(command => command.Kind == ReplayCommandKind.Advance
                || command.Kind == ReplayCommandKind.ChooseCard), Is.True);
            var result = FoundationBuild.VerifyWaveReplayFixture(replay, seed);
            Assert.That(result.Seed, Is.EqualTo(seed));
            Assert.That(result.Tick, Is.EqualTo(3000));
            Assert.That(result.EndKind, Is.EqualTo(ReplayEndKind.Quit));
            Assert.That(result.StateHash, Is.EqualTo(replay.End.StateHash));
            var output = Path.Combine(FoundationBuild.RepoRoot, "artifacts/wave1a/editor-parity");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, seed + ".txt"),
                "scope=3000 normal-input ticks; Unity Editor Mono; no full-run, balance or device acceptance\n"
                + seed + "\t" + result.Tick + "\t" + result.StateHash + "\t" + CanonicalContent.DataHash + "\n");
        }
    }
}
