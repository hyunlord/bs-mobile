#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Tests.PlayMode
{
    // #375 review render: Astra's B composition drawn by Unity with the game's packed estate art and the ported placement rules
    // (same seed), for a side-by-side with her assembly-390 / comparison-1x. The B board's own pieces (macro ground, church,
    // market, bridge, worker) are review-only files under EstateReview and never ship.
    public sealed class AstraBReviewTests
    {
        const float W = 390, H = 844;
        int priorWidth, priorHeight;
        readonly List<Object> created = new List<Object>();

        [UnitySetUp] public IEnumerator Setup()
        {
            priorWidth = Screen.width; priorHeight = Screen.height;
            PlayModeWindow.SetCustomRenderingResolution((uint)W, (uint)H, "Astra B review"); yield return null; yield return null;
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            foreach (var o in created) if (o != null) Object.Destroy(o);
            PlayModeWindow.SetCustomRenderingResolution((uint)priorWidth, (uint)priorHeight, "Restored test viewport");
            yield return null;
        }

        Sprite Review(string name)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Tests/PlayMode/EstateReview/" + name + ".png");
            Assert.That(texture, Is.Not.Null, name);
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, 0), 100); created.Add(sprite); return sprite;
        }

        static Image Place(RectTransform root, string name, Sprite sprite, double x, double y, double w, double h, float alpha = 1)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2((float)x, -(float)y); rect.sizeDelta = new Vector2((float)w, (float)h);
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; image.color = new Color(1, 1, 1, alpha); return image;
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator RendersAstrasBCompositionWithTheGameArtAndRules()
        {
            var art = EstateArt.Load(); var rules = EstateDecorRules.Load();
            var host = new GameObject("Astra B review", typeof(Canvas)); created.Add(host);
            var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var root = new GameObject("Board", typeof(RectTransform)).GetComponent<RectTransform>(); root.SetParent(host.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1); root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(W, H);
            root.localScale = Vector3.one * Mathf.Min(Screen.width / W, Screen.height / H);

            // assembly.js scene(): macro ground, three large restoration patches at fixed alphas.
            Place(root, "macro", Review("macro-ground"), 0, 0, W, H);
            Place(root, "terrain-restoration", art.Get("terrain-restoration"), -40, 470, 370, 310, .42f);
            Place(root, "terrain-fallow", art.Get("terrain-fallow"), -95, 670, 400, 290, .45f);
            Place(root, "terrain-sage", art.Get("terrain-sage"), -40, 40, 340, 310, .2f);

            var scene = AstraReference.SceneB(); var (objects, _) = AstraDecor.Layout(rules.astra, scene);
            Assert.That(objects.Count, Is.EqualTo(59));
            var yg = rules.yardGround;
            foreach (var b in scene.Buildings)
            {
                var id = b.Kind == "well" ? yg.well : b.Kind == "house" ? yg.house : yg.other;
                Place(root, "yard " + b.Key, art.Get(id), b.X + yg.offsetX * b.W, b.Y + yg.offsetY * b.W, yg.width * b.W, yg.height * b.W, yg.alpha);
            }
            // Everything standing, sorted by y then key/id as assembly.js does.
            var drawables = objects.Select(o => (Y: o.Y, Key: o.Key, Draw: (System.Action)(() => Sprite(root, art.Get(o.Id), o.Id, o.X, o.Y, o.W))))
                .Concat(scene.Buildings.Select(b => (Y: b.Y, Key: b.Key, Draw: (System.Action)(() =>
                {
                    var s = b.W / art.BodyWidth(b.Kind, b.Stage);
                    Place(root, "shadow " + b.Key, art.Shadow(b.Kind, b.Stage), b.X - 128 * s, b.Y - 280 * s, 256 * s, 320 * s);
                    Place(root, "body " + b.Key, art.Body(b.Kind, b.Stage), b.X - 128 * s, b.Y - 280 * s, 256 * s, 320 * s);
                }))))
                .Concat(AstraReference.FixedB.Select(f => (Y: (double)f.Y, Key: f.Id, Draw: (System.Action)(() =>
                {
                    var sprite = f.Id.StartsWith("field-") ? art.Body("field", f.Id.Contains("s2") ? 2 : 3) : Review(f.Id);
                    Sprite(root, sprite, f.Id, f.X, f.Y, f.W);
                }))));
            foreach (var d in drawables.OrderBy(d => d.Y).ThenBy(d => d.Key, System.StringComparer.Ordinal)) d.Draw();
            // assembly.js: a soft blue-grey shade over the lowest quarter.
            var shade = new Texture2D(1, 16, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp }; created.Add(shade);
            for (var i = 0; i < 16; i++) shade.SetPixel(0, i, new Color(.141f, .224f, .278f, .5f * (15 - i) / 15f));
            shade.Apply();
            var gradient = Sprite.Create(shade, new Rect(0, 0, 1, 16), new Vector2(.5f, .5f), 100); created.Add(gradient);
            Place(root, "shade", gradient, 0, 610, W, 234);

            yield return null; yield return null; Canvas.ForceUpdateCanvases();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta")); Directory.CreateDirectory(output);
            var path = Path.Combine(output, "astra-b-unity-390x844.png");
            if (File.Exists(path)) File.Delete(path); ScreenCapture.CaptureScreenshot(path); var deadline = Time.realtimeSinceStartup + 10;
            while (!File.Exists(path) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(File.Exists(path), Is.True);
            File.WriteAllText(path + ".txt", $"surface=Unity Editor PlayMode render of Astra's B board; rules=EstateDecorRules astra (seed {rules.astra.seed}, {rules.astra.styleVersion}); art=EstateArt packed atlases; objects=59 (yard 16, roadside 30, empty-land 13); commit={BuildIdentity.Commit}; sourceDirty={BuildIdentity.SourceDirty}\n");
        }

        // assembly.js sprite(): bottom-centre anchored, height from the art's aspect.
        static void Sprite(RectTransform root, Sprite sprite, string name, double x, double y, double w)
        {
            var h = w * sprite.rect.height / sprite.rect.width;
            Place(root, name, sprite, x - w / 2, y - h, w, h);
        }
    }
}
#endif
