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
        [Test] public void ProvenanceRequiresExactReplayRecordedDataAndTerminal()
        {
            var mode = CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark" });
            string D = CrowdReplayBenchmark.DataHash, R = CrowdReplayBenchmark.ReplaySha256, T = CrowdReplayBenchmark.TerminalHash;
            Assert.That(mode.Validate(R, D, D, 11530, T), Is.Null, "same data tree: no warning");
            Assert.Throws<InvalidDataException>(() => mode.Validate("wrong", D, D, 11530, T));
            Assert.Throws<InvalidDataException>(() => mode.Validate(R, "wrong", D, 11530, T), "the recorded data hash stays pinned");
            Assert.Throws<InvalidDataException>(() => mode.Validate(R, D, D, 11529, T));
            Assert.Throws<InvalidDataException>(() => mode.Validate(R, D, D, 11530, "wrong"));
        }
        // ADR0058 보충 (2026-10-21): a different current data tree only warns; the verdict is the run's actual terminal state.
        [Test] public void DifferentCurrentDataHashWithTheSameTerminalPassesWithAWarning()
        {
            var mode = CrowdReplayBenchmark.Parse(new[] { "--wave-benchmark", "fixture", "--crowd-replay-benchmark" });
            const string Current = "DBC4826C7F5D08FB056E6AB7CF2081B26BD6CD13D6A2477BFD76CC4EB6AD3ED7";
            string warning = null;
            Assert.DoesNotThrow(() => warning = mode.Validate(CrowdReplayBenchmark.ReplaySha256, CrowdReplayBenchmark.DataHash, Current, 11530, CrowdReplayBenchmark.TerminalHash));
            Assert.That(warning, Does.Contain("기록 당시 51DA41A2").And.Contain("현재 DBC4826C"));
            Assert.That(CrowdReplayBenchmark.TerminalMatches(CrowdReplayBenchmark.TerminalHash), Is.True, "same terminal state: the run passes");
        }
        [Test] public void DifferentTerminalStateFails()
        {
            Assert.That(CrowdReplayBenchmark.TerminalMatches("D9A96751CE84D2BD3153BA8C1BFA754D0DCA256FABC3E681F0689116E644D413"), Is.False);
            Assert.That(CrowdReplayBenchmark.TerminalMatches(null), Is.False);
        }
        [TestCase(10799, false)] [TestCase(10800, true)] [TestCase(11530, true)] [TestCase(11531, false)]
        public void MeasurementWindowIsExact(int tick, bool expected) => Assert.That(CrowdReplayBenchmark.InWindow(tick), Is.EqualTo(expected));
    }
}
