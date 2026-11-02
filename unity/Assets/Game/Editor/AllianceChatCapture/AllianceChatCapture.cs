using System;
using System.Collections;
using System.IO;
using System.Linq;
using Game.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Game.AllianceChatCapture
{
    [InitializeOnLoad]
    public static class AllianceChatCapture
    {
        const string Active = "SowSiege.AllianceChatCapture.Active";
        const string Async = "SowSiege.AllianceChatCapture.Async";
        static bool attached;
        static AllianceChatCapture()
        {
            if (SessionState.GetBool(Active, false))
            {
                RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
                RunCoordinator.PreferencePrefixOverride = "sowsiege.alliance-capture." + SessionState.GetString(Active + ".id", "") + ".";
            }
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Active + ".deadline", float.MaxValue))
                { Finish(false, "Editor capture exceeded 240 seconds."); return; }
                if (attached) return;
                var app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>();
                if (app == null || app.Ui == null) return;
                attached = true;
                app.StartCoroutine(new AllianceChatCaptureRunner().Execute());
            };
        }
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit mode.");
            var directory = Output;
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Capture output must be empty.");
            if (Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DEVICE_LOGIN") != "1" && !File.Exists(Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_TOKEN_FILE"))) throw new IOException("Private token file is required.");
            Directory.CreateDirectory(directory);
            Game.Editor.EditorCaptureStartup.WaitUntilReady(BeginCapture, FailStartup);
        }
        static void BeginCapture()
        {
            var directory = Output;
            SessionState.SetBool(Async, ShaderUtil.allowAsyncCompilation);
            SessionState.SetString(Active + ".id", Guid.NewGuid().ToString("N"));
            SessionState.SetBool(Active, true);
            SessionState.SetFloat(Active + ".deadline", (float)(EditorApplication.timeSinceStartup + 240));
            attached = false;
            RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
            RunCoordinator.PreferencePrefixOverride = "sowsiege.alliance-capture." + SessionState.GetString(Active + ".id", "") + ".";
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Alliance QA");
            EditorApplication.isPlaying = true;
        }
        static void FailStartup(Exception error)
        {
            Debug.LogException(error);
            if (SessionState.GetBool(Active, false))
                ShaderUtil.allowAsyncCompilation = SessionState.GetBool(Async, true);
            SessionState.SetBool(Active, false);
            try { File.WriteAllText(Path.Combine(Output, "startup-failed.txt"), error.ToString()); }
            finally { EditorApplication.Exit(1); }
        }
        internal static string Output => Environment.GetEnvironmentVariable("ALLIANCE_CAPTURE_DIR") ?? throw new InvalidOperationException("Explicit own artifact output is required.");
        internal static void Finish(bool success, string reason)
        {
            if (!SessionState.GetBool(Active, false)) return;
            SessionState.SetBool(Active, false);
            ShaderUtil.allowAsyncCompilation = SessionState.GetBool(Async, true);
            File.WriteAllText(Path.Combine(Output, "result.txt"), (success ? "PASS " : "FAIL ") + reason);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
