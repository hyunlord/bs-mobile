using System;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace Game.Editor
{
    public enum CaptureStartupState { Wait, Ready, Failed }

    public static class EditorCaptureStartup
    {
        static Request pending;

        public static CaptureStartupState Evaluate(bool startupDrained, bool playing, bool busy, int databaseCount, bool indexesReady)
        {
            if (playing) return CaptureStartupState.Failed;
            if (!startupDrained || busy || databaseCount <= 0 || !indexesReady) return CaptureStartupState.Wait;
            return CaptureStartupState.Ready;
        }

        public static void WaitUntilReady(Action ready, Action<Exception> failed, double timeoutSeconds = 120)
        {
            if (ready == null) throw new ArgumentNullException(nameof(ready));
            if (failed == null) throw new ArgumentNullException(nameof(failed));
            if (double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            if (pending != null) throw new InvalidOperationException("Another capture is waiting for Editor startup.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Wait for capture startup from Edit mode.");
            pending = new Request(ready, failed, timeoutSeconds);
            pending.Start();
        }

        sealed class Request
        {
            readonly Action ready;
            readonly Action<Exception> failed;
            readonly double deadline;
            bool startupDrained;
            bool finished;
            string reason = "startup delay callbacks have not drained";

            public Request(Action ready, Action<Exception> failed, double timeoutSeconds)
            {
                this.ready = ready;
                this.failed = failed;
                deadline = EditorApplication.timeSinceStartup + timeoutSeconds;
            }

            public void Start()
            {
                EditorApplication.delayCall += FirstDelay;
                EditorApplication.update += Poll;
                AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
                Application.logMessageReceived += OnLog;
            }

            void FirstDelay() { EditorApplication.delayCall += SecondDelay; }
            void SecondDelay() { startupDrained = true; }
            void BeforeReload() { Fail(new InvalidOperationException("Assembly reload interrupted capture startup.")); }

            void OnLog(string message, string stack, LogType type)
            {
                if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) &&
                    (message.Contains("UnityEditor.Search") || stack.Contains("UnityEditor.Search")))
                    Fail(new InvalidOperationException("Unity Search startup reported an error; original Editor log is authoritative."));
            }

            void Poll()
            {
                if (finished) return;
                Action continuation = null;
                try
                {
                    if (EditorApplication.timeSinceStartup >= deadline)
                        throw new TimeoutException("Capture startup timed out: " + reason);
                    var playing = EditorApplication.isPlayingOrWillChangePlaymode;
                    var busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
                    var count = 0;
                    var indexesReady = false;
                    if (startupDrained && !playing && !busy)
                    {
                        // #661: IsIndexReady uses All(), so an empty database list is not readiness.
                        foreach (var database in SearchService.EnumerateDatabases())
                            if (database != null) count++;
                        indexesReady = count > 0 && SearchService.IsIndexReady(null);
                        reason = count == 0 ? "Search returned no databases" : "Search indexes are still loading or updating";
                    }
                    else if (startupDrained && busy) reason = "Editor is compiling or importing";
                    if (finished) return;
                    var state = Evaluate(startupDrained, playing, busy, count, indexesReady);
                    if (state == CaptureStartupState.Failed)
                        throw new InvalidOperationException("PlayMode was requested before capture startup became ready.");
                    if (state != CaptureStartupState.Ready) return;
                    Stop();
                    Debug.Log("CAPTURE_STARTUP_READY databases=" + count + "; EditMode startup delay callbacks drained");
                    continuation = ready;
                }
                catch (Exception error) { Fail(error); return; }
                try { continuation(); }
                catch (Exception error) { failed(error); }
            }

            void Fail(Exception error)
            {
                if (finished) return;
                Stop();
                failed(error);
            }

            void Stop()
            {
                finished = true;
                EditorApplication.delayCall -= FirstDelay;
                EditorApplication.delayCall -= SecondDelay;
                EditorApplication.update -= Poll;
                AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
                Application.logMessageReceived -= OnLog;
                pending = null;
            }
        }
    }
}
