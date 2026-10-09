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
            Game.View.ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            ArtPreparation.Prepare();
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

        public static void VerifyProfile()
        {
            Game.View.ArtCatalog.ProfileName = CanonicalContent.ProfileName;
            var catalog = CanonicalContent.CreateCatalog();
            if (CanonicalContent.ProfileName == "wave-1a")
            {
                if (catalog.WaveRuntime == null || catalog.Weapons.Count != 5 || catalog.Tools.Count != 4 || catalog.Enemies.Count != 7 || catalog.WaveRuntime.Items.Count != 8 || catalog.WaveRuntime.Evolutions.Count != 3 || catalog.Runtime != null || catalog.FirstPlayable != null)
                    throw new BuildFailedException("Unity requires the isolated wave-1a catalog.");
            }
            else if (CanonicalContent.ProfileName != "first-playable" || catalog.Tuning.DurationTicks != 27000 || catalog.Tuning.TickRate != 30 ||
                catalog.FirstPlayable == null || catalog.Runtime == null || catalog.Weapons.Count != 10 || catalog.Tools.Count != 8 ||
                catalog.Enemies.Count != 13 || catalog.Runtime.Charters.Count != 8 || catalog.Runtime.Items.Count != 30 || catalog.Runtime.Evolutions.Count != 8)
                throw new BuildFailedException("Unity requires the complete first-playable catalog.");
            using (var hash = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = File.ReadAllBytes(Path.Combine(RepoRoot, "data/profiles/" + CanonicalContent.ProfileName + ".json"));
                var actual = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
                if (!string.Equals(actual, CanonicalContent.ProfileHash, StringComparison.Ordinal))
                    throw new BuildFailedException("Generated first-playable profile hash is stale.");
            }
        }

        public static ReplayVerification VerifyReplayFixture(ReplayDocument replay, int seed)
        {
            VerifyProfile();
            if (replay.Header.Options.DataHash != CanonicalContent.DataHash || replay.Header.Options.Run.Seed != seed ||
                replay.End.Tick != CanonicalContent.CreateCatalog().Tuning.DurationTicks || replay.End.Kind != ReplayEndKind.Duration)
                throw new BuildFailedException("Replay fixture identity/duration is stale for first-playable.");
            // DataHash covers all profiles: replay against the actual catalog to prove matching rules.
            return ReplayRunner.Verify(CanonicalContent.CreateCatalog(), CanonicalContent.DataHash, replay);
        }

        public static void VerifyGenerated()
        {
            var start = new ProcessStartInfo("/bin/bash") { WorkingDirectory = RepoRoot, UseShellExecute = false };
            start.Arguments = "tools/prepare-unity.sh --verify";
            VerifyProfile();
            using (var process = Process.Start(start))
            {
                if (process == null) throw new BuildFailedException("Cannot verify canonical generation.");
                process.WaitForExit();
                if (process.ExitCode != 0) throw new BuildFailedException("Generated Unity bridge is stale; run tools/prepare-unity.sh.");
            }
        }

        public static void Android()
        {
            BuildAndroid(true);
        }

        public static void AndroidRelease()
        {
            BuildAndroid(false);
        }

        public static void ConfigureMac()
        {
#if UNITY_STANDALONE_OSX
            Configure();
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
                throw new BuildFailedException("macOS Standalone module is not installed.");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.hyunlord.sowsiege");
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Standard);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 360;
            PlayerSettings.defaultScreenHeight = 780;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;
            EditorUserBuildSettings.development = false;
            AssetDatabase.SaveAssets();
#else
            throw new BuildFailedException("Run ConfigureMac with -buildTarget OSXUniversal.");
#endif
        }

        public static void MacRelease()
        {
            ConfigureMac();
            VerifyGenerated();
            var output = Path.GetFullPath(Environment.GetEnvironmentVariable("UNITY_MAC_PATH")
                ?? Path.Combine(RepoRoot, "artifacts/unity/Sow and Siege.app"));
            if (!output.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("UNITY_MAC_PATH must end in .app.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = ScenePaths, target = BuildTarget.StandaloneOSX, locationPathName = output,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || !Directory.Exists(output))
                throw new BuildFailedException("macOS Release build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "mac-build-result.json"),
                "{\"result\":\"Succeeded\",\"backend\":\"Mono\",\"architecture\":\"ARM64\",\"development\":false,\"profile\":\"" + CanonicalContent.ProfileName + "\",\"bytes\":" + report.summary.totalSize + "}");
            UnityEngine.Debug.Log("FOUNDATION_MAC_BUILD_SUCCEEDED " + output);
        }

        private static void BuildAndroid(bool development)
        {
            Configure();
            VerifyGenerated();
            var output = Environment.GetEnvironmentVariable("UNITY_APK_PATH") ?? Path.Combine(RepoRoot, "artifacts/unity/sow-siege.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = ScenePaths, target = BuildTarget.Android, locationPathName = output,
                options = development ? BuildOptions.Development : BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || !File.Exists(output))
                throw new BuildFailedException("Android IL2CPP build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-result.json"),
                "{\"result\":\"Succeeded\",\"backend\":\"IL2CPP\",\"architecture\":\"ARM64\",\"development\":" + (development ? "true" : "false") + ",\"bytes\":" + report.summary.totalSize + ",\"apkBytes\":" + new FileInfo(output).Length + "}");
            UnityEngine.Debug.Log("FOUNDATION_ANDROID_BUILD_SUCCEEDED " + output);
        }
    }

    public sealed class CanonicalDataBuildProcessor : BuildPlayerProcessor
    {
        public override void PrepareForBuild(BuildPlayerContext context)
        {
            ArtPreparation.Prepare();
            FoundationBuild.VerifyGenerated();
            if ((context.BuildPlayerOptions.options & BuildOptions.Development) != 0)
            {
                for (var seed = 30000; seed < 30005; seed++)
                {
                    var source = Path.Combine(FoundationBuild.RepoRoot, "artifacts/phase1b/replays", seed + ".ssreplay");
                    if (!File.Exists(source)) throw new BuildFailedException("Missing freshly generated replay fixture: " + source);
                    using (var input = File.OpenRead(source))
                    {
                        var replay = ReplayCodec.Read(input);
                        FoundationBuild.VerifyReplayFixture(replay, seed);
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
