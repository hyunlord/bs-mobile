using Game.App;
using NUnit.Framework;
using UnityEngine;

namespace Game.P1Capture.Tests
{
    public sealed class CaptureLifecycleTests
    {
        [Test]
        public void OrdinaryLaunchHonorsFocusLossAndApplicationPause()
        {
            var host = new GameObject("ordinary lifecycle test");
            try
            {
                var run = host.AddComponent<RunCoordinator>();
                run.SendMessage("OnApplicationFocus", false);
                Assert.That(run.EditorCaptureFocusLost, Is.True);
                run.SendMessage("OnApplicationPause", true);
                Assert.That(run.EditorCaptureApplicationPaused, Is.True);
                Assert.That(run.EditorCaptureSuspendedInterval, Is.True);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ExplicitEditorCaptureLogsAndIgnoresLifecycleUntilRestored()
        {
            var host = new GameObject("capture lifecycle test");
            try
            {
                var run = host.AddComponent<RunCoordinator>();
                var notifications = 0;
                run.EditorCaptureLifecycle += (kind, value, ignored) => { Assert.That(ignored, Is.True); notifications++; };
                run.SetEditorCaptureActive(true);
                run.SendMessage("OnApplicationFocus", false);
                run.SendMessage("OnApplicationPause", true);
                Assert.That(notifications, Is.EqualTo(2));
                Assert.That(run.EditorCaptureFocusLost, Is.False);
                Assert.That(run.EditorCaptureApplicationPaused, Is.False);
                Assert.That(run.EditorCaptureSuspendedInterval, Is.False);
                run.SetEditorCaptureActive(false);
                Assert.That(run.EditorCaptureFocusLost, Is.True);
                Assert.That(run.EditorCaptureApplicationPaused, Is.True);
            }
            finally { Object.DestroyImmediate(host); }
        }

    }
}
