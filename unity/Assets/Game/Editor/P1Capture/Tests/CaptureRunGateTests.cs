using System;
using NUnit.Framework;

namespace Game.P1Capture.Tests
{
    public sealed class CaptureRunGateTests
    {
        [Test]
        public void OrdinaryPausedInitialRunCanWarmRecorder()
        {
            Assert.DoesNotThrow(() => CaptureRunGate.RequirePausedAtZero(0, true));
        }

        [TestCase(1, true)]
        [TestCase(29, true)]
        [TestCase(0, false)]
        public void NeverHideGameplayDuringRecorderPreparation(int tick, bool menuOpen)
        {
            Assert.Throws<InvalidOperationException>(() => CaptureRunGate.RequirePausedAtZero(tick, menuOpen));
        }
    }
}
