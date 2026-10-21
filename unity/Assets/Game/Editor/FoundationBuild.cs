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
            if (CanonicalContent.IsWave)
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

        public static ReplayVerification VerifyWaveReplayFixture(ReplayDocument replay, int seed)
        {
            VerifyProfile();
            if (CanonicalContent.ProfileName != "wave-1a" || replay.Header.Options.DataHash != CanonicalContent.DataHash ||
                replay.Header.Options.Run.Seed != seed || replay.End.Tick != 3000 || replay.End.Kind != ReplayEndKind.Quit ||
                replay.Commands.Any(command => command.Kind != ReplayCommandKind.Advance && command.Kind != ReplayCommandKind.ChooseCard))
                throw new BuildFailedException("Wave fixture must contain 3000 ordinary input ticks for the current catalog and seed.");
            return ReplayRunner.Verify(CanonicalContent.CreateCatalog(), CanonicalContent.DataHash, replay);
        }

        public static string MeleeAimFixtureDirectory => Environment.GetEnvironmentVariable("MELEE_AIM_REPLAY_FIXTURES")
            ?? Path.Combine(RepoRoot, "artifacts/m1-256/editor-replays");

        public static ReplayVerification VerifyMeleeAimReplayFixture(ReplayDocument replay, int seed)
        {
            VerifyProfile();
            var catalog = CanonicalContent.CreateCatalog();
            if (CatalogRunOptions.MeleeAim(catalog) == null || replay.Header.FormatVersion != ReplayCodec.MeleeAimVersion ||
                replay.Header.Options.MeleeAim?.Version != 1 || replay.Header.Options.DataHash != CanonicalContent.DataHash ||
                replay.Header.Options.Run.Seed != seed || replay.End.Tick != 600 || replay.End.Kind != ReplayEndKind.Quit ||
                replay.Commands.Any(command => command.Kind != ReplayCommandKind.Advance && command.Kind != ReplayCommandKind.ChooseCard))
                throw new BuildFailedException("Melee aim review fixture must contain 600 ordinary input ticks and v6 options for this catalog and seed.");
            return ReplayRunner.Verify(catalog, CanonicalContent.DataHash, replay);
        }

        public const string WaveReplayFixtureDirectory = "artifacts/wave1a/editor-replays";
        public static readonly int[] WaveReplayFixtureSeeds = { 30000, 30001, 30002, 30003, 30004 };
        static string WaveReplayFixtureCommand(string directory) =>
            "dotnet run --project core/src/SowSiege.Sim --configuration Release -- interactive-fixtures data \"" + directory + "\" wave-1a";

        // The wave parity tests verify in Mono what .NET recorded, so the fixtures must come from .NET (never from Mono here).
        // When the default fixtures are missing or were recorded for other data, record them once with dotnet; a caller-supplied
        // directory is never overwritten. Failure names the exact command to run.
        public static void EnsureWaveReplayFixtures(string directory, bool generate)
        {
            bool Current() => WaveReplayFixtureSeeds.All(seed =>
            {
                var path = Path.Combine(directory, seed + ".ssreplay");
                if (!File.Exists(path)) return false;
                try { using var input = File.OpenRead(path); return ReplayCodec.Read(input).Header.Options.DataHash == CanonicalContent.DataHash; }
                catch (Exception) { return false; }
            });
            if (Current()) return;
            var command = WaveReplayFixtureCommand(directory);
            if (!generate)
                throw new BuildFailedException("Wave replay fixtures in " + directory + " are missing or were recorded for other data. Run from the repository root: " + command);
            var start = new ProcessStartInfo("/bin/bash")
            {
                WorkingDirectory = RepoRoot, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                Arguments = "-c 'export PATH=\"$HOME/.dotnet:/usr/local/share/dotnet:/opt/homebrew/bin:/usr/local/bin:$PATH\" DOTNET_CLI_TELEMETRY_OPTOUT=1; exec " + command.Replace("'", "'\\''") + "'"
            };
            string output;
            int exit;
            using (var process = Process.Start(start))
            {
                if (process == null) throw new BuildFailedException("Cannot start bash to record wave replay fixtures. Run from the repository root: " + command);
                // Drain both streams in the background so the timeout applies even when dotnet hangs with its pipes open.
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(600000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new BuildFailedException("Recording wave replay fixtures took longer than 10 minutes. Run from the repository root: " + command);
                }
                process.WaitForExit();
                exit = process.ExitCode;
                output = System.Threading.Tasks.Task.WaitAll(new[] { stdout, stderr }, 30000) ? stdout.Result + stderr.Result : "(output streams did not close)";
            }
            if (exit != 0 || !Current())
            {
                var tail = output.Length > 1500 ? output.Substring(output.Length - 1500) : output;
                throw new BuildFailedException("Recording wave replay fixtures with dotnet failed (exit " + exit + "). Run from the repository root: " + command + "\n" + tail);
            }
        }

        public static void VerifyGenerated()
        {
            var start = new ProcessStartInfo("/bin/bash") { WorkingDirectory = RepoRoot, UseShellExecute = false };
            start.Arguments = "tools/prepare-unity.sh --verify --profile " + CanonicalContent.ProfileName;
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
            BuildMac(false);
        }

        public static void MacDiagnostic()
        {
            BuildMac(true);
        }

        private static void BuildMac(bool development)
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
                options = development ? BuildOptions.Development : BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded || !Directory.Exists(output))
                throw new BuildFailedException("macOS build failed: " + report.summary.result);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "mac-build-result.json"),
                "{\"result\":\"Succeeded\",\"backend\":\"Mono\",\"architecture\":\"ARM64\",\"development\":" + (development ? "true" : "false") + ",\"profile\":\"" + CanonicalContent.ProfileName + "\",\"bytes\":" + report.summary.totalSize + "}");
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
                var meleeReview = CatalogRunOptions.MeleeAim(CanonicalContent.CreateCatalog()) != null;
                var firstSeed = meleeReview ? 30001 : 30000;
                var lastSeed = meleeReview ? 30002 : 30005;
                for (var seed = firstSeed; seed < lastSeed; seed++)
                {
                    var source = Path.Combine(meleeReview ? FoundationBuild.MeleeAimFixtureDirectory
                        : Path.Combine(FoundationBuild.RepoRoot, "artifacts/phase1b/replays"), seed + ".ssreplay");
                    if (!File.Exists(source)) throw new BuildFailedException("Missing freshly generated replay fixture: " + source);
                    using (var input = File.OpenRead(source))
                    {
                        var replay = ReplayCodec.Read(input);
                        if (meleeReview) FoundationBuild.VerifyMeleeAimReplayFixture(replay, seed);
                        else if (CanonicalContent.ProfileName == "wave-1a") FoundationBuild.VerifyWaveReplayFixture(replay, seed);
                        else FoundationBuild.VerifyReplayFixture(replay, seed);
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
