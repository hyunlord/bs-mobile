#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Game.App;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    public sealed partial class EstateMapFlowTests
    {
        [UnityTest, Timeout(90000)]
        public IEnumerator FirstFiveRenderedEstateFramesContainNoCyan()
        {
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("Estate");
            Click("Estate.Settings"); yield return ScreenIs("Settings");
            yield return Reveal(Toggle); Click(Toggle); yield return null;
            Click("Settings.Back");
            Assert.That(app.CurrentScreenName, Is.EqualTo("EstateMap"));
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/m3a7-entry-diagnostic"));
            Directory.CreateDirectory(folder);
            var failedFrames = new System.Collections.Generic.List<string>();
            var log = new StringBuilder("relativeFrame,unityFrame,screen,surface,width,height,cyanPixels\n");
            for (var frame = 0; frame <= 30; frame++)
            {
                Canvas.ForceUpdateCanvases();
                // The preceding update has submitted the new page; read its completed render, not the old Settings buffer.
                yield return null;
                if (frame <= 5 || frame == 30)
                {
                    var gameView = ReadGameView(); Record(gameView, "game-view", frame);
                    var displayedPixels = gameView.GetPixels32(); Object.Destroy(gameView);
                    var capture = ScreenCapture.CaptureScreenshotAsTexture();
                    Record(capture, "screen-capture", frame);
                    if (!displayedPixels.SequenceEqual(capture.GetPixels32())) failedFrames.Add($"{frame}: capture differs from Game View");
                    Object.Destroy(capture);
                    var state = new StringBuilder($"asyncShaderCompilation={ShaderUtil.allowAsyncCompilation}; screen={app.CurrentScreenName}; frame={Time.frameCount}\n");
                    foreach (var g in app.Ui.GetComponentsInChildren<Graphic>())
                    {
                        var m = g.materialForRendering;
                        var actual = g.canvasRenderer.materialCount > 0 ? g.canvasRenderer.GetMaterial(0) : null;
                        state.AppendLine($"{g.name}: shader={m?.shader?.name}, supported={m?.shader?.isSupported}, renderer={actual?.shader?.name}, texture={g.mainTexture?.name}/{g.mainTexture?.GetEntityId()}/{g.mainTexture?.width}x{g.mainTexture?.height}, color={g.color}");
                    }
                    File.WriteAllText(Path.Combine(folder, $"frame-{frame:D2}-state.txt"), state.ToString());
                }
            }
            File.WriteAllText(Path.Combine(folder, "frames.csv"), log.ToString());
            Assert.That(failedFrames, Is.Empty, "Cyan first-entry regression: " + string.Join(";", failedFrames));
            void Record(Texture2D image, string surface, int frame)
            {
                var cyan = image.GetPixels32().Count(c => c.r < 16 && c.g > 230 && c.b > 230);
                File.WriteAllBytes(Path.Combine(folder, $"frame-{frame:D2}-{surface}.png"), image.EncodeToPNG());
                if (cyan != 0) failedFrames.Add($"{frame}/{surface}:{cyan}");
                log.AppendLine($"{frame},{Time.frameCount},{app.CurrentScreenName},{surface},{image.width},{image.height},{cyan}");
            }
        }
        // Editor-only, independent read of the surface Game View displays. No OS screenshot permission or substitute camera.
        static Texture2D ReadGameView()
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            var field = type.GetField("m_RenderTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Update the Game View adapter if Unity changes its render-target field.");
            foreach (var window in Resources.FindObjectsOfTypeAll(type))
            {
                var rt = field.GetValue(window) as RenderTexture;
                if (rt == null || !rt.IsCreated()) continue;
                var prior = RenderTexture.active;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                try { RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); }
                finally { RenderTexture.active = prior; }
                if (SystemInfo.graphicsUVStartsAtTop)
                {
                    var pixels = image.GetPixels32(); var upright = new Color32[pixels.Length];
                    for (var y = 0; y < image.height; y++) Array.Copy(pixels, y * image.width, upright, (image.height - y - 1) * image.width, image.width);
                    image.SetPixels32(upright); image.Apply();
                }
                return image;
            }
            Assert.Fail("Game View render target is required; a camera substitute is not evidence.");
            return null;
        }
    }
}
#endif
