using System;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class WaveGroundingDiagnosticTests
    {
        [Test]
        public void OrdinaryRunsKeepGrounding()
        {
            Assert.That(RunCoordinator.ParseWaveBenchmarkSkipGrounding(Array.Empty<string>()), Is.False);
            Assert.That(RunCoordinator.ParseWaveBenchmarkSkipGrounding(new[] { "--wave-benchmark", "c05.ssreplay" }), Is.False);
        }

        [Test]
        public void ExplicitC05DiagnosticCanSkipGrounding()
            => Assert.That(RunCoordinator.ParseWaveBenchmarkSkipGrounding(new[] { "--wave-benchmark", "c05.ssreplay", "--wave-benchmark-skip-grounding" }), Is.True);

        [TestCase("--crowd-replay-benchmark")]
        [TestCase("--offline-replay-capture")]
        [TestCase("--autoplay-capture")]
        [TestCase("--wave-benchmark-skip-grounding")]
        public void ProductEvidenceAndDuplicateFlagsRejectAblation(string forbidden)
            => Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkSkipGrounding(new[] { "--wave-benchmark", "c05.ssreplay", "--wave-benchmark-skip-grounding", forbidden }));

        [Test]
        public void AblationRequiresBenchmarkAndReplay()
        {
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkSkipGrounding(new[] { "--wave-benchmark-skip-grounding" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkSkipGrounding(new[] { "--wave-benchmark-skip-grounding", "--wave-benchmark" }));
            Assert.Throws<ArgumentException>(() => RunCoordinator.ParseWaveBenchmarkSkipGrounding(new[] { "--wave-benchmark", "--wave-benchmark-skip-grounding" }));
        }
    }
}
