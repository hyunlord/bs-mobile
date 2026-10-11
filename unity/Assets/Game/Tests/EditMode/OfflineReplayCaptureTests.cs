using System;
using Game.App;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class OfflineReplayCaptureTests
    {
        [Test] public void FixedClockReplays730TicksIn1460FramesRegardlessOfWallTime()
        {
            double accumulator = 0;
            var ticks = 0;
            var expected = OfflineReplayCapture.ExpectedFrames(730, 30);
            for (var frame = 0; frame < expected; frame++)
            {
                accumulator += OfflineReplayCapture.FrameSeconds;
                while (accumulator >= 1d / 30) { accumulator -= 1d / 30; ticks++; }
                Assert.That(accumulator * 30, Is.InRange(0d, 1d));
            }
            Assert.That(expected, Is.EqualTo(1460));
            Assert.That(ticks, Is.EqualTo(730));
            Assert.That(accumulator, Is.EqualTo(0d).Within(1e-10));
        }
        [Test] public void DefaultIsCaptureFreeAndOfflineRequiresExactCrowdMode()
        {
            Assert.That(OfflineReplayCapture.Requested(Array.Empty<string>()), Is.False);
            Assert.Throws<ArgumentException>(() => OfflineReplayCapture.Requested(new[] { "--offline-replay-capture" }));
            Assert.That(OfflineReplayCapture.Requested(new[] { "--offline-replay-capture", "--crowd-replay-benchmark" }), Is.True);
        }
        [TestCase("--offline-replay-capture")]
        [TestCase("--wave-benchmark-profile")]
        [TestCase("--wave-benchmark-core-diagnostics")]
        [TestCase("--wave-benchmark-capture")]
        [TestCase("--wave-benchmark-mode")]
        public void RejectsDuplicatesAndPerformanceModes(string flag)
            => Assert.Throws<ArgumentException>(() => OfflineReplayCapture.Requested(new[] { "--offline-replay-capture", "--crowd-replay-benchmark", flag }));
    }
}
