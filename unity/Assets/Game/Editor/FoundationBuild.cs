using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using SowSiege.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    public static class FoundationBuild
    {
        public static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
        public static readonly string[] ScenePaths = { "Assets/Scenes/Boot.unity", "Assets/Scenes/Meta.unity", "Assets/Scenes/Run.unity" };

        public static void Configure()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";
            PlayerSettings.companyName = "SowSiege";
            PlayerSettings.productName = "Sow and Siege";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.hyunlord.sowsiege");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel36;
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            if (pipeline == null) throw new InvalidOperationException("Universal 2D template pipeline missing.");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i);
                QualitySettings.renderPipeline = pipeline;
            }
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "Scenes"));
            foreach (var path in ScenePaths)
            {
                if (File.Exists(path)) continue;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.orthographic = true;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Game.View.FoundationStatus.Ground;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (path == ScenePaths[0]) new GameObject("Boot").AddComponent<FoundationBoot>();
                EditorSceneManager.SaveScene(scene, path);
            }
            EditorBuildSettings.scenes = ScenePaths.Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
            AssetDatabase.SaveAssets();
        }

        public static void VerifyGenerated()
        {
            var start = new ProcessStartInfo("/bin/bash") { WorkingDirectory = RepoRoot, UseShellExecute = false };
            start.Arguments = "tools/prepare-unity.sh --verify";
            using (var process = Process.Start(start))
            {
                if (process == null) throw new BuildFailedException("Cannot verify canonical generation.");
                process.WaitForExit();
                if (process.ExitCode != 0) throw new BuildFailedException("Generated Unity bridge is stale; run tools/prepare-unity.sh.");
            }
        }

        public static void Android()
        {
            Configure();
            VerifyGenerated();
            var output = Environment.GetEnvironmentVariable("UNITY_APK_PATH") ?? Path.Combine(RepoRoot, "artifacts/unity/sow-siege.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = ScenePaths, target = BuildTarget.Android, locationPathName = output,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(output))
                throw new BuildFailedException("Android IL2CPP build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-result.json"),
                "{\"result\":\"Succeeded\",\"backend\":\"IL2CPP\",\"architecture\":\"ARM64\",\"bytes\":" + report.summary.totalSize + "}");
            UnityEngine.Debug.Log("FOUNDATION_ANDROID_BUILD_SUCCEEDED " + output);
        }
    }

    public sealed class CanonicalDataBuildProcessor : BuildPlayerProcessor
    {
        public override void PrepareForBuild(BuildPlayerContext context)
        {
            FoundationBuild.VerifyGenerated();
            if ((context.BuildPlayerOptions.options & BuildOptions.Development) != 0)
            {
                for (var seed = 30000; seed < 30005; seed++)
                {
                    var source = Path.Combine(FoundationBuild.RepoRoot, "artifacts/phase1a/replays", seed + ".ssreplay");
                    if (!File.Exists(source)) throw new BuildFailedException("Missing freshly generated replay fixture: " + source);
                    using (var input = File.OpenRead(source))
                    {
                        var replay = ReplayCodec.Read(input);
                        if (replay.Header.Options.DataHash != CanonicalContent.DataHash || replay.Header.Options.Run.Seed != seed)
                            throw new BuildFailedException("Replay fixture identity is stale: " + source);
                    }
                    context.AddAdditionalPathToStreamingAssets(source, "replays/" + seed + ".ssreplay");
                }
            }
            foreach (var file in CanonicalContent.Files)
            {
                var source = Path.Combine(FoundationBuild.RepoRoot, "data", file.RelativePath);
                BundleVerifier.VerifyFile(file, File.ReadAllBytes(source));
                context.AddAdditionalPathToStreamingAssets(source, "data/" + file.RelativePath);
            }
        }
    }
}
