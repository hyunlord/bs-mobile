using System;
using System.IO;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class CrowdReplayBenchmarkTests
    {
        [Test] public void C05RemainsDefaultAndCrowdDefaultsToBaselineHeroA()
        {
            Assert.That(CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture" }), Is.Null);
            Assert.That(CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark" }).HeroRig, Is.False);
            Assert.That(CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark", "--crowd-hero-b" }).HeroRig, Is.True);
        }
        [TestCase("--wave-benchmark-mode")]
        [TestCase("--wave-benchmark-capture")]
        [TestCase("--autoplay-capture")]
        [TestCase("--smoothness-trace")]
        [TestCase("--crowd-replay-benchmark")]
        public void RejectsChangedPacingCaptureAndDuplicateMode(string forbidden)
            => Assert.Throws<ArgumentException>(() => CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark", forbidden }));
        [Test] public void ProvenanceRequiresExactReplayDataAndTerminal()
        {
            var mode = CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark" });
            Assert.DoesNotThrow(() => mode.Validate(CrowdReplayBenchmark.ReplaySha256, CrowdReplayBenchmark.DataHash, 11530, CrowdReplayBenchmark.TerminalHash));
            Assert.Throws<InvalidDataException>(() => mode.Validate("wrong", CrowdReplayBenchmark.DataHash, 11530, CrowdReplayBenchmark.TerminalHash));
            Assert.Throws<InvalidDataException>(() => mode.Validate(CrowdReplayBenchmark.ReplaySha256, "wrong", 11530, CrowdReplayBenchmark.TerminalHash));
            Assert.Throws<InvalidDataException>(() => mode.Validate(CrowdReplayBenchmark.ReplaySha256, CrowdReplayBenchmark.DataHash, 11529, CrowdReplayBenchmark.TerminalHash));
            Assert.Throws<InvalidDataException>(() => mode.Validate(CrowdReplayBenchmark.ReplaySha256, CrowdReplayBenchmark.DataHash, 11530, "wrong"));
        }
        [TestCase(10799, false)] [TestCase(10800, true)] [TestCase(11530, true)] [TestCase(11531, false)]
        public void MeasurementWindowIsExact(int tick, bool expected) => Assert.That(CrowdReplayBenchmark.InWindow(tick), Is.EqualTo(expected));
    }
}
