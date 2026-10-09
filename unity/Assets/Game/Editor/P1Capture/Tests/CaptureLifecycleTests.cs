using System.Reflection;
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
                Call(run, "OnApplicationFocus", false);
                Assert.That(Flag(run, "focusLost"), Is.True);
                Call(run, "OnApplicationPause", true);
                Assert.That(Flag(run, "applicationPaused"), Is.True);
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
                Call(run, "OnApplicationFocus", false);
                Call(run, "OnApplicationPause", true);
                Assert.That(notifications, Is.EqualTo(2));
                Assert.That(Flag(run, "focusLost"), Is.False);
                Assert.That(Flag(run, "applicationPaused"), Is.False);
                Assert.That(Flag(run, "suspendedInterval"), Is.False);
                run.SetEditorCaptureActive(false);
                Assert.That(Flag(run, "focusLost"), Is.True);
                Assert.That(Flag(run, "applicationPaused"), Is.True);
            }
            finally { Object.DestroyImmediate(host); }
        }

        static bool Flag(RunCoordinator run, string name) => (bool)typeof(RunCoordinator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(run);
        static void Call(RunCoordinator run, string name, bool value) => typeof(RunCoordinator).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(run, new object[] { value });
    }
}
