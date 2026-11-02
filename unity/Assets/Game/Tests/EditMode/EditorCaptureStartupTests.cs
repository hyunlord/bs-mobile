using Game.Editor;
using NUnit.Framework;

namespace Tests.EditMode
{
    public sealed class EditorCaptureStartupTests
    {
        [Test]
        public void BusyFalseDoesNotReplaceStartupDelayBarrier()
        {
            Assert.That(EditorCaptureStartup.Evaluate(false, false, false, 1, true), Is.EqualTo(CaptureStartupState.Wait));
        }

        [Test]
        public void EmptyDatabaseListCannotPassVacuousReadyResult()
        {
            Assert.That(EditorCaptureStartup.Evaluate(true, false, false, 0, true), Is.EqualTo(CaptureStartupState.Wait));
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        public void BusyOrUnreadyIndexKeepsEditMode(bool busy, bool ready)
        {
            Assert.That(EditorCaptureStartup.Evaluate(true, false, busy, 1, ready), Is.EqualTo(CaptureStartupState.Wait));
        }

        [Test]
        public void UnexpectedPlayTransitionFailsEvenBeforeDelayBarrier()
        {
            Assert.That(EditorCaptureStartup.Evaluate(false, true, true, 0, false), Is.EqualTo(CaptureStartupState.Failed));
        }

        [Test]
        public void NonemptyReadyIndexesAfterStartupPermitCapture()
        {
            Assert.That(EditorCaptureStartup.Evaluate(true, false, false, 1, true), Is.EqualTo(CaptureStartupState.Ready));
        }
    }
}
