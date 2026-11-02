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

namespace Game.AccountCapture
{
    [InitializeOnLoad]
    public static class AccountCapture
    {
        const string Active = "SowSiege.AccountCapture.Active";
        const string Async = "SowSiege.AccountCapture.Async";
        static bool attached;
        static AccountCapture()
        {
            if (SessionState.GetBool(Active, false))
            {
                RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
                RunCoordinator.PreferencePrefixOverride = "sowsiege.account-capture." + SessionState.GetString(Active + ".id", "") + ".";
            }
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Active + ".deadline", float.MaxValue))
                { Finish(false, "Editor capture exceeded 180 seconds."); return; }
                if (attached) return;
                var app = UnityEngine.Object.FindFirstObjectByType<RunCoordinator>();
                if (app == null || app.Ui == null) return;
                attached = true;
                app.StartCoroutine(new AccountCaptureRunner().Execute());
            };
        }
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit mode.");
            var directory = Output;
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Capture output must be empty.");
            if (Environment.GetEnvironmentVariable("ACCOUNT_CAPTURE_DEVICE_LOGIN") != "1" && !File.Exists(Environment.GetEnvironmentVariable("ACCOUNT_CAPTURE_TOKEN_FILE"))) throw new IOException("Private token file is required.");
            Directory.CreateDirectory(directory);
            SessionState.SetBool(Async, ShaderUtil.allowAsyncCompilation);
            SessionState.SetString(Active + ".id", Guid.NewGuid().ToString("N"));
            SessionState.SetBool(Active, true);
            SessionState.SetFloat(Active + ".deadline", (float)(EditorApplication.timeSinceStartup + 180));
            attached = false;
            RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
            RunCoordinator.PreferencePrefixOverride = "sowsiege.account-capture." + SessionState.GetString(Active + ".id", "") + ".";
            ShaderUtil.allowAsyncCompilation = false;
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Account QA");
            EditorApplication.isPlaying = true;
        }
        internal static string Output => Environment.GetEnvironmentVariable("ACCOUNT_CAPTURE_DIR") ?? throw new InvalidOperationException("Explicit own artifact output is required.");
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
