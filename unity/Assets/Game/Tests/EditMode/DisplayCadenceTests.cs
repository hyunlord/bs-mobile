using System;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class DisplayCadenceTests
    {
        [Test] public void TargetSixtyExperimentIsExplicitAndCrowdOnly()
        {
            Assert.That(DisplayCadence.ParseCrowdTarget60Experiment(Array.Empty<string>()), Is.False);
            Assert.That(DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "fixture.ssreplay", "--crowd-replay-benchmark", "--crowd-target60-experiment" }), Is.True);
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--crowd-target60-experiment" }));
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "fixture.ssreplay", "--crowd-target60-experiment" }));
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "--crowd-replay-benchmark", "--crowd-target60-experiment" }));
        }
        [TestCase("--offline-replay-capture")] [TestCase("--autoplay-capture")]
        [TestCase("--wave-benchmark-capture")] [TestCase("--crowd-target60-experiment")] [TestCase("--wave-benchmark-mode")]
        public void TargetSixtyExperimentRejectsCaptureDuplicatesAndAlternateModes(string conflict)
        {
            Assert.Throws<ArgumentException>(() => DisplayCadence.ParseCrowdTarget60Experiment(new[] { "--wave-benchmark", "fixture.ssreplay", "--crowd-replay-benchmark", "--crowd-target60-experiment", conflict }));
        }

        [TestCase(60d, 1)] [TestCase(120d, 2)] [TestCase(59.94d, 1)] [TestCase(119.88d, 2)]
        [TestCase(180d, 3)] [TestCase(240d, 4)] [TestCase(144d, 0)] [TestCase(75d, 0)]
        [TestCase(0d, 0)] [TestCase(double.NaN, 0)] [TestCase(double.PositiveInfinity, 0)]
        public void DesktopUsesSixtyRatherThanNativeRefresh(double refresh, int expected)
            => Assert.That(DisplayCadence.SyncInterval(refresh, false), Is.EqualTo(expected));

        [TestCase(60d)] [TestCase(120d)]
        public void MobileUsesTargetFrameRate(double refresh)
            => Assert.That(DisplayCadence.SyncInterval(refresh, true), Is.Zero);

        [Test] public void MonitorChangeRecomputesInterval()
        {
            Assert.That(DisplayCadence.SyncInterval(120, false), Is.EqualTo(2));
            Assert.That(DisplayCadence.SyncInterval(60, false), Is.EqualTo(1));
            Assert.That(DisplayCadence.SyncInterval(120, false), Is.EqualTo(2));
        }
    }
}
