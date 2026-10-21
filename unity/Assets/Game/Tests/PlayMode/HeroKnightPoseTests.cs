using System.Collections;
using System.IO;
using Game.View;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public sealed class HeroKnightPoseTests
    {
        [Test]
        public void KnightUsesSeparateArtWithCalibratedSolesAndHistoricalBRemainsAvailable()
        {
            var knight = HeroLiveRigData.Load("HeroKnight");
            var historical = HeroLiveRigData.Load();
            Assert.That(knight.partsResource, Is.EqualTo("HeroKnight/parts"));
            Assert.That(knight.swordResource, Is.EqualTo("HeroMotion/sword"));
            Assert.That(knight.jointNames, Is.EqualTo(historical.jointNames));
            Assert.That(knight.farSoles.Length, Is.EqualTo(12));
            Assert.That(knight.nearSoles.Length, Is.EqualTo(12));
            Assert.That(historical.farSoles, Is.Null.Or.Empty);
            var texture = Resources.Load<Texture2D>(knight.partsResource);
            Assert.That(texture, Is.Not.Null);
            foreach (var part in knight.sprites)
            {
                if (part.source == "cleanSword") continue;
                Assert.That(part.rect[0], Is.GreaterThanOrEqualTo(0));
                Assert.That(part.rect[1], Is.GreaterThanOrEqualTo(0));
                Assert.That(part.rect[0] + part.rect[2], Is.LessThanOrEqualTo(texture.width));
                Assert.That(part.rect[1] + part.rect[3], Is.LessThanOrEqualTo(texture.height));
            }
        }

        [UnityTest]
        public IEnumerator SevenActionsRenderThroughRealSpriteSkinAtPortraitAspect()
        {
            var output = System.Environment.GetEnvironmentVariable("SOW_HERO_KNIGHT_CAPTURE");
            if (!string.IsNullOrEmpty(output)) Directory.CreateDirectory(output);
            var owner = new GameObject("A knight seven-action pose preview");
            var cameraOwner = new GameObject("A knight portrait preview camera");
            var camera = cameraOwner.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5;
            camera.transform.position = new Vector3(0, .4f, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.24f, .26f, .24f, 1);
            var target = new RenderTexture(390, 844, 24);
            camera.targetTexture = target;
            var rig = new HeroLiveRig(owner.transform, "HeroKnight");
            var names = new[] { "idle", "walk", "moving-swing", "evolved-swing", "hit", "death", "level-up", "walk-near-stride", "walk-far-stride",
                "swing-phase-00", "swing-phase-20", "swing-phase-40", "swing-phase-60", "swing-phase-80",
                "evolved-phase-00", "evolved-phase-20", "evolved-phase-40", "evolved-phase-60", "evolved-phase-80" };
            var image = new Texture2D(390, 844, TextureFormat.RGB24, false);
            try
            {
                for (var action = 0; action < names.Length; action++)
                {
                    var gaitFrame = action == 7 ? 0 : action == 8 ? 6 : 2;
                    var gait = new HeroGaitSample(gaitFrame, gaitFrame / 12f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, false, HeroGaitReleaseReason.None);
                    for (var frame = 0; frame < 3; frame++)
                    {
                        rig.Present(Vector2.zero, Vector2.one * 1.4f, gait, (action >= 1 && action <= 4) || action >= 7,
                            action >= 9 ? ((action - 9) % 5) * .2f : action == 2 || action == 3 ? .35f : -1, action == 3 || action >= 14,
                            action == 4 ? .45f : -1, action == 4 ? new Color(1, .32f, .28f) : Color.white,
                            action == 5 ? 1 : -1, action == 6 ? .5f : -1);
                        yield return null;
                    }
                    Assert.That(rig.DeformedSkinCount, Is.EqualTo(14), names[action]);
                    var previous = RenderTexture.active;
                    try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 390, 844), 0, 0); image.Apply(); }
                    finally { RenderTexture.active = previous; }
                    var painted = 0;
                    var background = image.GetPixel(0, 0);
                    foreach (var pixel in image.GetPixels32())
                    {
                        var color = (Color)pixel;
                        if (Mathf.Abs(color.r - background.r) + Mathf.Abs(color.g - background.g) + Mathf.Abs(color.b - background.b) > .12f) painted++;
                    }
                    Assert.That(painted, Is.GreaterThan(300), names[action] + " must render actual textured geometry.");
                    if (!string.IsNullOrEmpty(output)) File.WriteAllBytes(Path.Combine(output, names[action] + ".png"), image.EncodeToPNG());
                }
                if (!string.IsNullOrEmpty(output)) File.WriteAllText(Path.Combine(output, "pose-preview.json"),
                    "{\"kind\":\"Unity SpriteSkin pose preview; staged actions, not gameplay events\",\"resource\":\"HeroKnight\",\"width\":390,\"height\":844,\"canvasWorldSize\":1.4,\"cameraHalfHeight\":5,\"actions\":[\"idle\",\"walk\",\"moving-swing\",\"evolved-swing\",\"hit\",\"death\",\"level-up\"],\"strideExtraSamples\":[\"walk-near-stride\",\"walk-far-stride\"],\"swingSamplePhases\":[0,0.2,0.4,0.6,0.8],\"swingSamplesEach\":5,\"performanceEligible\":false}");
            }
            finally
            {
                rig.Dispose(); camera.targetTexture = null; target.Release();
                Object.Destroy(image); Object.Destroy(target); Object.Destroy(owner); Object.Destroy(cameraOwner);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LevelSaluteKeepsLegsAndDeathSettlesWholeBodyWithoutAllocating()
        {
            var owner = new GameObject("Knight pose layer test");
            var rig = new HeroLiveRig(owner.transform, "HeroKnight");
            try
            {
                var gait = new HeroGaitSample(0, 0, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, false, false, HeroGaitReleaseReason.None);
                rig.Present(Vector2.zero, Vector2.one, gait, true, -1, false, -1, Color.white);
                yield return null;
                yield return null;
                var leg = rig.SampleDeformedVertex(3);
                var sword = rig.SampleDeformedVertex(13);
                rig.Present(Vector2.zero, Vector2.one, gait, true, -1, false, -1, Color.white, -1, .5f);
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(leg, rig.SampleDeformedVertex(3)), Is.LessThan(.0001f));
                Assert.That(Vector3.Distance(sword, rig.SampleDeformedVertex(13)), Is.GreaterThan(.01f));
                rig.Present(Vector2.zero, Vector2.one, gait, false, -1, false, -1, Color.white, 0);
                yield return null;
                yield return null;
                var head = rig.SampleDeformedVertex(8);
                rig.Present(Vector2.zero, Vector2.one, gait, false, -1, false, -1, Color.white, 1);
                yield return null;
                yield return null;
                Assert.That(Vector3.Distance(head, rig.SampleDeformedVertex(8)), Is.GreaterThan(.01f));
                for (var i = 0; i < 10; i++) rig.Present(Vector2.zero, Vector2.one, gait, false, -1, false, -1, Color.white, 1);
                var before = System.GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 60; i++) rig.Present(Vector2.zero, Vector2.one, gait, false, -1, false, -1, Color.white, 1);
                Assert.That(System.GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
            }
            finally { rig.Dispose(); Object.Destroy(owner); }
            yield return null;
        }
    }
}
