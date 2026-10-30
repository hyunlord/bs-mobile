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

namespace Game.BarracksCapture
{
    [InitializeOnLoad]
    public static class BarracksCapture
    {
        const string Active = "SowSiege.BarracksCapture.Active";
        const string Async = "SowSiege.BarracksCapture.Async";
        static bool attached;
        static BarracksCapture()
        {
            if (SessionState.GetBool(Active, false))
            {
                RunCoordinator.MetaDirectoryOverride = Path.Combine(Output, "local-profile");
                RunCoordinator.PreferencePrefixOverride = "sowsiege.barracks-capture." + SessionState.GetString(Active + ".id", "") + ".";
            }
            EditorApplication.update += () =>
            {
                if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying || attached) return;
                attached = true;
                new GameObject("Explicit barracks capture").AddComponent<BarracksCaptureRunner>();
            };
        }
        public static void Start()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit mode.");
            var directory = Output;
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Capture output must be empty.");
            if (!File.Exists(Environment.GetEnvironmentVariable("BARRACKS_CAPTURE_TOKEN_FILE"))) throw new IOException("Private token file is required.");
            Directory.CreateDirectory(directory);
            SessionState.SetBool(Async, ShaderUtil.allowAsyncCompilation);
            SessionState.SetString(Active + ".id", Guid.NewGuid().ToString("N"));
            SessionState.SetBool(Active, true);
            EditorSceneManager.OpenScene("Assets/Scenes/Boot.unity");
            PlayModeWindow.SetCustomRenderingResolution(390, 844, "Barracks QA");
            EditorApplication.isPlaying = true;
        }
        internal static string Output => Environment.GetEnvironmentVariable("BARRACKS_CAPTURE_DIR") ?? throw new InvalidOperationException("Explicit own artifact output is required.");
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
