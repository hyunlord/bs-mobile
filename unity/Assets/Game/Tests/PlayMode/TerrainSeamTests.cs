using System.Collections;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class TerrainSeamTests
    {
        [UnityTest]
        public IEnumerator MirroredAtlasTilesShareEdgesWithoutSamplingTheAtlasFringe()
        {
            const int pixels = 256;
            var owner = new GameObject("Mirrored terrain camera");
            var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1;
            camera.transform.position = new Vector3(1, 1, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(pixels, pixels, 24); camera.targetTexture = target;
            var atlas = new Texture2D(8, 8, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var capture = new Texture2D(pixels, pixels, TextureFormat.RGB24, false);
            // Blue is a sentinel outside the selected cell; the cell has an asymmetric two-axis gradient.
            for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
                atlas.SetPixel(x, y, x >= 2 && x < 6 && y >= 2 && y < 6
                    ? new Color(0.2f + (x - 2) * 0.2f, 0.2f + (y - 2) * 0.2f, 0, 1)
                    : Color.blue);
            atlas.Apply();
            try
            {
                using (var batch = new SpriteBatch(atlas, Resources.Load<Shader>("WorldSprite"), camera, GameVisualTokens.TerrainLayer))
                {
                    for (var frame = 0; frame < 3; frame++)
                    {
                        batch.BeginFrame();
                        for (var y = 0; y < 2; y++) for (var x = 0; x < 2; x++)
                        {
                            var uv = new Rect(x == 0 ? 0.25f : 0.75f, y == 0 ? 0.25f : 0.75f,
                                x == 0 ? 0.5f : -0.5f, y == 0 ? 0.5f : -0.5f);
                            batch.Add(new Vector2(x, y), Vector2.one, Vector2.zero, uv, 0, Color.white);
                        }
                        batch.Flush(); yield return null;
                    }
                    Assert.That(batch.SubmittedInstances, Is.EqualTo(4));
                    var before = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        capture.ReadPixels(new Rect(0, 0, pixels, pixels), 0, 0); capture.Apply();
                    }
                    finally { RenderTexture.active = before; }
                    for (var i = 0; i < pixels; i++)
                    {
                        AssertSame(capture.GetPixel(127, i), capture.GetPixel(128, i), "Vertical seam at " + i);
                        AssertSame(capture.GetPixel(i, 127), capture.GetPixel(i, 128), "Horizontal seam at " + i);
                    }
                    foreach (var pixel in capture.GetPixels())
                    {
                        Assert.That(pixel.b, Is.LessThan(0.02f), "Mirroring must never sample the blue atlas fringe.");
                        Assert.That(pixel.r + pixel.g, Is.GreaterThan(0.2f), "Adjacent tiles must cover the viewport without black gutters.");
                    }
                    Assert.That(Mathf.Abs(capture.GetPixel(24, 24).r - capture.GetPixel(104, 24).r), Is.GreaterThan(0.2f), "The shader must retain the authored gradient.");
                    Assert.That(Mathf.Abs(capture.GetPixel(24, 24).g - capture.GetPixel(24, 104).g), Is.GreaterThan(0.2f));
                }
            }
            finally
            {
                camera.targetTexture = null; target.Release();
                Object.Destroy(capture); Object.Destroy(target); Object.Destroy(atlas); Object.Destroy(owner);
            }
            yield return null;
        }

        static void AssertSame(Color first, Color second, string context)
        {
            Assert.That(first.r, Is.EqualTo(second.r).Within(2f / 255), context);
            Assert.That(first.g, Is.EqualTo(second.g).Within(2f / 255), context);
            Assert.That(first.b, Is.EqualTo(second.b).Within(2f / 255), context);
        }
    }
}
