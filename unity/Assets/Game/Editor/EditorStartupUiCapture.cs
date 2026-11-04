using System;
using System.Collections;
using System.IO;
using Game.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor
{
    [InitializeOnLoad]
    public static class EditorStartupUiCapture
    {
        const string Active = "SowSiege.StartupUiCapture.Active";
        static bool attached;
        static string Output => Environment.GetEnvironmentVariable("STARTUP_CAPTURE_DIR") ??
            throw new InvalidOperationException("Explicit startup capture directory required.");

        static EditorStartupUiCapture()
        {
            if (SessionState.GetBool(Active, false)) ConfigureProfile();
            EditorApplication.update += Poll;
        }

        static void ConfigureProfile()
        {
            RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
            RunCoordinator.PreferencePrefixOverride = "sowsiege.startup-capture." + SessionState.GetString(Active + ".id", "") + ".";
        }

        public static void Start()
        {
            if (Directory.Exists(Output)) throw new IOException("Use a new startup capture output.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "startup.txt"), "entryEditorSeconds=" + EditorApplication.timeSinceStartup +
                " playing=" + EditorApplication.isPlayingOrWillChangePlaymode + " compiling=" + EditorApplication.isCompiling +
                " updating=" + EditorApplication.isUpdating + "\nOriginal Search exception reproduction remains separate from successful guarded startup.\n");
            EditorCaptureStartup.WaitUntilReady(Begin, Fail);
        }

        static void Begin()
        {
            File.AppendAllText(Path.Combine(Output, "startup.txt"), "readyEditorSeconds=" + EditorApplication.timeSinceStartup + "\n");
            SessionState.SetFloat(Active + ".deadline", (float)(EditorApplication.timeSinceStartup + 60));
            SessionState.SetBool(Active, true);
            SessionState.SetString(Active + ".id", Guid.NewGuid().ToString("N"));
            ConfigureProfile();
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Search startup UI QA");
            EditorApplication.isPlaying = true;
        }

        static void Poll()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Active + ".deadline", 0))
            { Fail(new TimeoutException("Startup UI capture timeout.")); return; }
            if (!EditorApplication.isPlaying || attached) return;
            var app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>();
            if (app == null || app.Ui == null) return;
            attached = true;
            EditorUiPngCapture.BeginSession();
            app.StartCoroutine(Guard(Capture(app)));
        }

        static IEnumerator Capture(RunCoordinator app)
        {
            while (app.Ui.GetComponentsInChildren<Button>().Length == 0) yield return null;
            yield return null; yield return null;
            yield return EditorUiPngCapture.Capture(Path.Combine(Output, "title-focus.txt"), texture =>
            {
                if (texture.width != 390 || texture.height != 844) throw new InvalidOperationException("Unexpected GameView size.");
                File.WriteAllBytes(Path.Combine(Output, "startup-title-390x844.png"), texture.EncodeToPNG());
            }, Fail);
            Finish(true, "Guarded startup and foreground GameView PNG completed. Original Search exception is NOT_REPRODUCED by this guarded run.");
        }

        static IEnumerator Guard(IEnumerator steps)
        {
            while (true)
            {
                object next;
                try { if (!steps.MoveNext()) yield break; next = steps.Current; }
                catch (Exception error) { Fail(error); yield break; }
                if (next is IEnumerator nested) yield return Guard(nested); else yield return next;
            }
        }

        static void Fail(Exception error) { Debug.LogException(error); Finish(false, error.ToString()); }
        static void Finish(bool success, string reason)
        {
            if (success && !SessionState.GetBool(Active, false)) return;
            SessionState.SetBool(Active, false);
            File.WriteAllText(Path.Combine(Output, "result.txt"), (success ? "PASS " : "FAIL ") + reason);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
