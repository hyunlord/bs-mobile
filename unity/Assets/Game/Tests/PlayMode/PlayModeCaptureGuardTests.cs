using System.IO;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Tests.PlayMode
{
    public sealed class PlayModeCaptureGuardTests
    {
#if UNITY_EDITOR
        [TestCase(false)]
        [TestCase(true)]
        public void CaptureSessionRestoresOriginalCompilationSetting(bool original)
        {
            var previous = ShaderUtil.allowAsyncCompilation;
            var persistedSetting = EditorSettings.asyncShaderCompilation;
            var session = new PlayModeCaptureSession();
            try
            {
                ShaderUtil.allowAsyncCompilation = original;
                session.BeginSession();
                Assert.That(ShaderUtil.allowAsyncCompilation, Is.False);
                session.EndSession();
                Assert.That(ShaderUtil.allowAsyncCompilation, Is.EqualTo(original));
                Assert.That(EditorSettings.asyncShaderCompilation, Is.EqualTo(persistedSetting), "Runtime switch must not change the persisted project setting.");
            }
            finally { ShaderUtil.allowAsyncCompilation = previous; }
        }

        [UnityTest]
        public IEnumerator CaptureWaitsForTwoUpdateFrames()
        {
            Assert.That(ShaderUtil.allowAsyncCompilation, Is.False, "Session guard must cover every fixture.");
            yield return null;
            var frame = Time.frameCount;
            yield return PlayModeCaptureGuard.WaitForShaders("guard-update-frames-test");
            Assert.That(Time.frameCount - frame, Is.GreaterThanOrEqualTo(2));
        }
#endif
        [TestCase(false)]
        [TestCase(true)]
        public void CaptureGuardRecordsBeforeFailingAndAcceptsCleanPixels(bool cyan)
        {
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/playmode-capture-monitor/self-test"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, cyan ? "intentional-cyan.png" : "clean.png");
            try
            {
                image.SetPixels32(new[] { new Color32(0, (byte)(cyan ? 255 : 0), (byte)(cyan ? 255 : 0), 255),
                    new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255) });
                image.Apply();
                var probe = PlayModeCaptureGuard.Begin("guard-self-test-negative-control-" + cyan);
                if (cyan) Assert.Throws<AssertionException>(() => PlayModeCaptureGuard.Check(image, probe, path, true));
                else PlayModeCaptureGuard.Check(image, probe, path, false);
                var record = File.ReadAllText(path + ".cyan.json");
                Assert.That(record, Does.Contain("\"cyanPixels\":" + (cyan ? "1" : "0")));
                Assert.That(record, Does.Contain("\"anythingCompilingAfter\":" + (cyan ? "true" : "false")));
                Assert.That(record, Does.Contain("\"precedingDomainReloadObserved\":"));
                Assert.That(record, Does.Contain("\"observationInterval\":\"capture-call\""));
            }
            finally { Object.DestroyImmediate(image); }
        }
    }
}
