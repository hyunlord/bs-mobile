using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    // UI evidence only. C05 and Recorder retain their independent measurement gates.
    public static class EditorUiPngCapture
    {
        static bool initialFocusUsed;

        public static void BeginSession() { initialFocusUsed = false; }

        public static IEnumerator Capture(string evidencePath, Action<Texture2D> save, Action<Exception> fail)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (fail == null) throw new ArgumentNullException(nameof(fail));
            var expired = false;
            var deadline = EditorApplication.timeSinceStartup + 15;
            Action watchdog = null;
            watchdog = () =>
            {
                if (expired || EditorApplication.timeSinceStartup < deadline) return;
                expired = true;
                EditorApplication.update -= watchdog;
                File.AppendAllText(evidencePath, "FAIL frame/focus deadline; no image published\n");
                fail(new TimeoutException("UI PNG frame/focus deadline exceeded."));
            };
            EditorApplication.update += watchdog;
            try
            {
                for (var attempt = 1; attempt <= 2; attempt++)
                {
                    var focusDeadline = EditorApplication.timeSinceStartup + 5;
                    while (!IsReady() && !expired)
                    {
                        if (!initialFocusUsed && IsNativeForeground())
                        {
                            initialFocusUsed = true;
                            var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true);
                            EditorWindow.GetWindow(gameView).Focus();
                        }
                        if (EditorApplication.timeSinceStartup >= focusDeadline) break;
                        yield return null;
                    }
                    if (expired) yield break;
                    initialFocusUsed = true;
                    if (IsReady()) yield return new WaitForEndOfFrame();
                    if (expired) yield break;
                    var accepted = CaptureAttempt(IsReady, () => ScreenCapture.CaptureScreenshotAsTexture(), save,
                        texture => UnityEngine.Object.Destroy(texture));
                    File.AppendAllText(evidencePath, "attempt=" + attempt + " focusAndRenderBeforeAfter=" + accepted + "\n");
                    if (accepted) yield break;
                }
                throw new InvalidOperationException("UI PNG focus/render gate failed twice; no automatic app activation or scenario replay.");
            }
            finally { EditorApplication.update -= watchdog; }
        }

        // Testable transaction: a failed post-capture sample never publishes the texture.
        public static bool CaptureAttempt<T>(Func<bool> ready, Func<T> capture, Action<T> save, Action<T> release) where T : class
        {
            if (!ready()) return false;
            var image = capture();
            if (image == null) throw new InvalidOperationException("UI PNG capture returned null.");
            try
            {
                if (!ready()) return false;
                save(image);
                return true;
            }
            finally { release(image); }
        }

        static bool IsReady()
        {
            var window = EditorWindow.focusedWindow;
            return window != null && window.GetType().FullName == "UnityEditor.GameView" &&
                IsNativeForeground() && !EditorApplication.isCompiling && !EditorApplication.isUpdating &&
                !ShaderUtil.anythingCompiling && EditorApplication.isPlaying;
        }

        static bool IsNativeForeground()
        {
#if UNITY_EDITOR_OSX
            var workspaceClass = objc_getClass("NSWorkspace");
            if (workspaceClass == IntPtr.Zero) throw new InvalidOperationException("NSWorkspace unavailable.");
            var workspace = SendPointer(workspaceClass, sel_registerName("sharedWorkspace"));
            var application = SendPointer(workspace, sel_registerName("frontmostApplication"));
            if (application == IntPtr.Zero) return false;
            using (var process = Process.GetCurrentProcess())
                return SendInt(application, sel_registerName("processIdentifier")) == process.Id;
#else
            throw new PlatformNotSupportedException("UI PNG foreground proof currently requires macOS Editor.");
#endif
        }

#if UNITY_EDITOR_OSX
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern int SendInt(IntPtr receiver, IntPtr selector);
#endif
    }
}
