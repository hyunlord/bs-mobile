using System.Collections;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class EnemyDrawQueueTests
    {
        static void Add(EnemyDrawQueue queue, Texture2D texture, float y, Color color, float upperOpacity = 1)
            => queue.Add(texture, Vector2.zero, Vector2.one, Vector2.one * .5f, new Rect(0, 0, 1, 1),
                0, color, 0, 0, upperOpacity, y);

        [TestCase(24, 1)]
        [TestCase(411, 1)]
        [TestCase(511, 1)]
        [TestCase(512, 2)]
        [TestCase(800, 2)]
        [TestCase(1023, 3)]
        public void SameAtlasAcrossYBandsOnlySplitsAtInstanceLimit(int count, int expected)
        {
            var owner = new GameObject("Enemy batching structure");
            var camera = owner.AddComponent<Camera>(); camera.orthographic = true;
            try
            {
                using (var queue = new EnemyDrawQueue())
                {
                    queue.BeginFrame();
                    for (var i = 0; i < count; i++) Add(queue, Texture2D.whiteTexture, i % 20, Color.white);
                    queue.Flush(Resources.Load<Shader>("WorldSprite"), camera);
                    Assert.That(queue.SubmittedInstances, Is.EqualTo(count));
                    Assert.That(queue.DrawCalls, Is.EqualTo(expected));
                    queue.BeginFrame(); queue.Flush(Resources.Load<Shader>("WorldSprite"), camera);
                    Assert.That(queue.DrawCalls, Is.Zero);
                }
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [UnityTest]
        public IEnumerator MixedAtlasPainterOrderKeepsHeroAboveAndUpperBodyTransparent()
        {
            var owner = new GameObject("Enemy painter order"); var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -10);
            camera.transparencySortMode = UnityEngine.TransparencySortMode.Default;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(128, 128, 24); camera.targetTexture = target;
            var otherAtlas = new Texture2D(1, 1); otherAtlas.SetPixel(0, 0, Color.white); otherAtlas.Apply();
            var shader = Resources.Load<Shader>("WorldSprite");
            using (var queue = new EnemyDrawQueue())
            using (var hero = new SpriteBatch(Texture2D.whiteTexture, shader, camera, GameVisualTokens.LordLayer))
            {
                for (var frame = 0; frame < 3; frame++)
                {
                    queue.BeginFrame(); hero.BeginFrame();
                    // Scrambled input; A/B/A must stay three runs after sorting. The near actor's top is translucent (#171).
                    Add(queue, Texture2D.whiteTexture, 0, Color.blue, .25f);
                    Add(queue, Texture2D.whiteTexture, 2, Color.red);
                    Add(queue, otherAtlas, 1, Color.green);
                    hero.Add(Vector2.zero, Vector2.one * .15f, Vector2.one * .5f, new Rect(0, 0, 1, 1), 0, Color.magenta);
                    hero.Flush(); queue.Flush(shader, camera); yield return null;
                }
                Assert.That(queue.DrawCalls, Is.EqualTo(3), "Interleaved atlases cannot be merged across a foreground actor.");
                var previous = RenderTexture.active; RenderTexture.active = target;
                var capture = new Texture2D(128, 128, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); capture.Apply(); RenderTexture.active = previous;
                var foot = capture.GetPixel(64, 40); var upper = capture.GetPixel(64, 85); var lord = capture.GetPixel(64, 64);
                Assert.That(foot.b, Is.GreaterThan(.9f)); Assert.That(foot.r + foot.g, Is.LessThan(.1f));
                Assert.That(upper.g, Is.GreaterThan(upper.b), "The green actor behind must remain visible through the near upper body.");
                Assert.That(upper.b, Is.GreaterThan(.1f)); Assert.That(upper.r, Is.LessThan(.1f));
                Assert.That(lord.r, Is.GreaterThan(.9f)); Assert.That(lord.b, Is.GreaterThan(.9f)); Assert.That(lord.g, Is.LessThan(.1f));
                Object.Destroy(capture);
            }
            camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(otherAtlas); Object.Destroy(owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SameAtlasSortsInstancesByYInsideOneDraw()
        {
            var owner = new GameObject("Enemy instance order"); var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(32, 32, 24); camera.targetTexture = target;
            using (var queue = new EnemyDrawQueue())
            {
                for (var frame = 0; frame < 3; frame++)
                {
                    queue.BeginFrame();
                    Add(queue, Texture2D.whiteTexture, -1, Color.blue);
                    Add(queue, Texture2D.whiteTexture, 1, Color.red);
                    Add(queue, Texture2D.whiteTexture, 0, Color.green);
                    queue.Flush(Resources.Load<Shader>("WorldSprite"), camera); yield return null;
                }
                Assert.That(queue.DrawCalls, Is.EqualTo(1));
                var previous = RenderTexture.active; RenderTexture.active = target;
                var capture = new Texture2D(32, 32, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); capture.Apply(); RenderTexture.active = previous;
                Assert.That(capture.GetPixel(16, 16).b, Is.GreaterThan(.9f));
                Object.Destroy(capture);
            }
            camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CapacitySplitCannotReverseForegroundInstance()
        {
            var owner = new GameObject("Enemy chunk order"); var camera = owner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 1; camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var target = new RenderTexture(32, 32, 24); camera.targetTexture = target;
            using (var queue = new EnemyDrawQueue())
            {
                for (var frame = 0; frame < 3; frame++)
                {
                    queue.BeginFrame();
                    // Equal Y keeps insertion order, including the outline/foreground at the 511 boundary.
                    for (var i = 0; i < 512; i++) Add(queue, Texture2D.whiteTexture, 0, i == 511 ? Color.blue : Color.red);
                    queue.Flush(Resources.Load<Shader>("WorldSprite"), camera); yield return null;
                }
                Assert.That(queue.DrawCalls, Is.EqualTo(2));
                var previous = RenderTexture.active; RenderTexture.active = target;
                var capture = new Texture2D(32, 32, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); capture.Apply(); RenderTexture.active = previous;
                Assert.That(capture.GetPixel(16, 16).b, Is.GreaterThan(.9f));
                Object.Destroy(capture);
            }
            camera.targetTexture = null; target.Release(); Object.Destroy(target); Object.Destroy(owner);
            yield return null;
        }
    }
}
