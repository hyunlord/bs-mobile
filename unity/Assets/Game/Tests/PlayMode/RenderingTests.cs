using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class RenderingTests
    {
        [UnityTest]
        public IEnumerator Universal2DCameraActuallyRendersAShape()
        {
            yield return SceneManager.LoadSceneAsync("Run");
            var camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            var target = new RenderTexture(64, 64, 24);
            camera.targetTexture = target;
            var source = new Texture2D(2, 2);
            source.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            source.Apply();
            var sprite = Sprite.Create(source, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 1);
            var shape = new GameObject("Render probe").AddComponent<SpriteRenderer>();
            shape.sprite = sprite;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader);
            shape.sharedMaterial = material;
            yield return null;
            yield return null;
            yield return null;
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var capture = new Texture2D(64, 64, TextureFormat.RGB24, false);
            capture.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            capture.Apply();
            RenderTexture.active = previous;
            var center = capture.GetPixel(32, 32);
            var edge = capture.GetPixel(1, 1);
            Assert.That(center.r + center.g + center.b, Is.GreaterThan(edge.r + edge.g + edge.b + 0.05f));
#if UNITY_EDITOR
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/unity"));
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "urp2d-render-probe.png"), capture.EncodeToPNG());
#endif
            camera.targetTexture = null;
            Object.Destroy(shape.gameObject);
            Object.Destroy(material);
            Object.Destroy(sprite);
            Object.Destroy(source);
            Object.Destroy(capture);
            target.Release();
            Object.Destroy(target);
        }
    }
}
