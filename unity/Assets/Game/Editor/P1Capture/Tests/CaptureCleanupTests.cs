using System;
using System.IO;
using NUnit.Framework;

namespace Game.P1Capture.Tests
{
    public sealed class CaptureCleanupTests
    {
        [Test]
        public void PreferenceFailureDoesNotSkipBackgroundRestoreOrFinish()
        {
            var backgroundRestored = false;
            var finished = false;
            var error = CaptureCleanup.Run(
                () => throw new InvalidOperationException("preference verification"),
                () => backgroundRestored = true,
                () => finished = true);
            Assert.That(error, Is.TypeOf<AggregateException>());
            Assert.That(backgroundRestored, Is.True);
            Assert.That(finished, Is.True);
        }

        [Test]
        public void EvidenceWriteFailureDoesNotPreventRemainingCleanup()
        {
            var savedFlag = true;
            var playing = true;
            var error = CaptureCleanup.Run(
                () => throw new IOException("output unavailable"),
                () => savedFlag = false,
                () => playing = false);
            Assert.That(error, Is.TypeOf<AggregateException>());
            Assert.That(savedFlag, Is.False);
            Assert.That(playing, Is.False);
        }
    }
}
