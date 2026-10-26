using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Tests.PlayMode
{
#if UNITY_EDITOR
    [SetUpFixture]
    public sealed class PlayModeCaptureSession
    {
        bool originalAsyncCompilation;

        [OneTimeSetUp]
        public void BeginSession()
        {
            originalAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Debug.Log($"CAPTURE_SESSION asyncShaderCompilation=false original={originalAsyncCompilation}");
        }

        [OneTimeTearDown]
        public void EndSession()
        {
            ShaderUtil.allowAsyncCompilation = originalAsyncCompilation;
            Debug.Log($"CAPTURE_SESSION_RESTORED asyncShaderCompilation={originalAsyncCompilation}");
        }
    }
#endif
    // Test-only capture surveillance. Compiler-idle is evidence, not a render-completion fence.
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal static class PlayModeCaptureGuard
    {
        internal readonly struct Probe
        {
            public readonly string Label;
            public readonly bool CompilingBefore;
            public readonly int FrameBefore;
            public Probe(string label, bool compiling) { Label = label; CompilingBefore = compiling; FrameBefore = Time.frameCount; }
        }
        [Serializable] sealed class Entry
        {
            public string capture, capturedUtc, domainLoadedUtc, observationInterval;
            public int frameBefore, frameAfter, cyanPixels, captureOrdinalInDomain;
            public bool anythingCompilingBefore, anythingCompilingAfter, precedingDomainReloadObserved;
        }
        const string ReloadPending = "SowSiege.PlayModeCaptureGuard.reloadPending";
        static readonly string DomainLoadedUtc;
        static readonly bool PrecedingReloadObserved;
        static int captureOrdinal;
        static PlayModeCaptureGuard()
        {
            DomainLoadedUtc = DateTime.UtcNow.ToString("O");
#if UNITY_EDITOR
            PrecedingReloadObserved = SessionState.GetBool(ReloadPending, false);
            SessionState.SetBool(ReloadPending, false);
            AssemblyReloadEvents.beforeAssemblyReload += () => SessionState.SetBool(ReloadPending, true);
#endif
        }
        static bool Compiling
        {
            get
            {
#if UNITY_EDITOR
                return ShaderUtil.anythingCompiling;
#else
                return false;
#endif
            }
        }
        public static Probe Begin(string label) => new Probe(label, Compiling);
        public static IEnumerator WaitForShaders(string label)
        {
#if UNITY_EDITOR
            var started = Time.realtimeSinceStartupAsDouble;
            var compilingAtStart = ShaderUtil.anythingCompiling;
            while (ShaderUtil.anythingCompiling)
            {
                if (Time.realtimeSinceStartupAsDouble - started >= 10)
                {
                    var message = $"SHADER_CAPTURE_TIMEOUT capture={label} elapsed=10 anythingCompiling=true frame={Time.frameCount} precedingDomainReloadObserved={PrecedingReloadObserved}";
                    Debug.Log(message);
                    Assert.Fail(message);
                }
                yield return null;
            }
            var readyFrame = Time.frameCount;
            yield return null;
            yield return null;
            Debug.Log($"SHADER_CAPTURE_FRESH_FRAMES capture={label} readyFrame={readyFrame} captureFrame={Time.frameCount} updateYieldCount=2 anythingCompiling={ShaderUtil.anythingCompiling}");
            Debug.Log($"SHADER_CAPTURE_READY capture={label} compilingAtStart={compilingAtStart} anythingCompiling={ShaderUtil.anythingCompiling} waitedSeconds={Time.realtimeSinceStartupAsDouble-started:F3} frame={Time.frameCount}");
#endif
            yield break;
        }
        public static void ReadPixels(Texture2D image, Rect region, int x, int y, string label)
        {
            var probe = Begin(label);
            image.ReadPixels(region, x, y);
            Check(image, probe);
        }
        public static void Check(Texture2D image, Probe probe, string pngPath = null, bool? compilingAfter = null, string observationInterval = "capture-call")
        {
            var observedAfter = compilingAfter ?? Compiling;
            var count = 0;
            foreach (var pixel in image.GetPixels32()) if (pixel.r < 16 && pixel.g > 230 && pixel.b > 230) count++;
            var entry = new Entry
            {
                capture = probe.Label, capturedUtc = DateTime.UtcNow.ToString("O"), domainLoadedUtc = DomainLoadedUtc,
                frameBefore = probe.FrameBefore, frameAfter = Time.frameCount, cyanPixels = count,
                anythingCompilingBefore = probe.CompilingBefore, anythingCompilingAfter = observedAfter, observationInterval = observationInterval,
                precedingDomainReloadObserved = PrecedingReloadObserved, captureOrdinalInDomain = ++captureOrdinal
            };
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/playmode-capture-monitor"));
            Directory.CreateDirectory(output);
            var json = JsonUtility.ToJson(entry);
            File.AppendAllText(Path.Combine(output, "events.jsonl"), json + "\n");
            if (pngPath != null) File.WriteAllText(pngPath + ".cyan.json", json + "\n");
            if (count > 0)
            {
                File.WriteAllBytes(Path.Combine(output, $"cyan-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{captureOrdinal}.png"), image.EncodeToPNG());
                Debug.Log("CYAN_CAPTURE_FAILURE " + json);
            }
            Assert.That(count, Is.Zero, "Cyan capture regression: " + json);
        }
        public static void CheckFile(string path, Probe probe)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { Assert.That(image.LoadImage(File.ReadAllBytes(path)), Is.True, path); Check(image, probe, path, observationInterval: "request-to-file-check"); }
            finally { UnityEngine.Object.Destroy(image); }
        }
    }
}
