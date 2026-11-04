using System;
using Game.Editor;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class EditorUiPngCaptureTests
    {
        [Test]
        public void MissingInitialFocusDoesNotCaptureOrPublish()
        {
            Assert.That(EditorUiPngCapture.CaptureAttempt<object>(() => false,
                () => throw new Exception("must not capture"), _ => Assert.Fail("must not save"),
                _ => Assert.Fail("nothing to release")), Is.False);
        }

        [Test]
        public void LosingFocusAfterCaptureReleasesImageWithoutPublishing()
        {
            var reads = 0;
            var released = false;
            Assert.That(EditorUiPngCapture.CaptureAttempt(() => ++reads == 1, () => new object(),
                _ => Assert.Fail("invalid image must not save"), _ => released = true), Is.False);
            Assert.That(released, Is.True);
        }

        [Test]
        public void ValidBeforeAndAfterPublishesExactlyOnceAndReleases()
        {
            var saves = 0;
            var releases = 0;
            Assert.That(EditorUiPngCapture.CaptureAttempt(() => true, () => new object(),
                _ => saves++, _ => releases++), Is.True);
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void SaveFailureReleasesAndPropagatesWithoutRetryingServerScenario()
        {
            var released = false;
            Assert.Throws<InvalidOperationException>(() => EditorUiPngCapture.CaptureAttempt(() => true,
                () => new object(), _ => throw new InvalidOperationException("save failed"), _ => released = true));
            Assert.That(released, Is.True);
        }
    }
}
