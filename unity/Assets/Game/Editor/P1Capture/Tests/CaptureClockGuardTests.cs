using System;
using NUnit.Framework;

namespace Game.P1Capture.Tests
{
    public sealed class CaptureClockGuardTests
    {
        [Test]
        public void EncoderStartupGapIsExcludedOnlyBeforeRunStart()
        {
            var clock = new CaptureClockGuard();
            Assert.DoesNotThrow(() => clock.Validate(0, .486));
            clock.Arm(4, 4.486);
            Assert.DoesNotThrow(() => clock.Validate(184, 184.486));
            Assert.Throws<InvalidOperationException>(() => clock.Validate(185, 185.637));
        }

        [Test]
        public void DriftAcrossCardPausesAndLateFootageRemainsCumulative()
        {
            var clock = new CaptureClockGuard();
            clock.Arm(4, 4.486);
            Assert.DoesNotThrow(() => clock.Validate(64, 64.586));
            Assert.DoesNotThrow(() => clock.Validate(66, 66.586));
            Assert.Throws<InvalidOperationException>(() => clock.Validate(980, 980.637));
        }

        [Test]
        public void ASecondBaselineCannotHideAccumulatedDrift()
        {
            var clock = new CaptureClockGuard();
            clock.Arm(4, 4.486);
            Assert.Throws<InvalidOperationException>(() => clock.Arm(184, 184.7));
        }
    }
}
