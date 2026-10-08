using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class CameraSafeAreaTests
    {
        [TestCase(900, 1600)]
        [TestCase(1080, 1080)]
        public void MapCornersKeepWholeActorInsideHudAndNotchExclusions(int width, int height)
        {
            var owner = new GameObject("Safe framing camera");
            var camera = owner.AddComponent<Camera>();
            var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
            var viewport = new Rect(0, 0, width, height); camera.pixelRect = viewport;
            var safe = new Rect(48, 180, width - 72, height - 180 - 240);
            try
            {
                Assert.That(camera.pixelRect, Is.EqualTo(viewport), "The test requires the requested physical viewport.");
                foreach (var corner in new[] { Vector2.zero, new Vector2(24, 0), new Vector2(0, 24), new Vector2(24, 24) })
                {
                    var follow = new RunCamera(camera, new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800));
                    follow.SetMapBounds(24000, 24000); follow.Present(corner, 6000, 0, safe);
                    foreach (var offset in new[] { new Vector2(-0.4f, -0.08f), new Vector2(0.4f, 0.94f) })
                    {
                        var point = camera.WorldToScreenPoint(corner + offset);
                        Assert.That(point.x, Is.InRange(safe.xMin, safe.xMax));
                        Assert.That(point.y, Is.InRange(safe.yMin, safe.yMax));
                    }
                    Assert.That(camera.pixelRect, Is.EqualTo(viewport), "The world still fills the viewport.");
                    var halfHeight = camera.orthographicSize; var halfWidth = halfHeight * camera.aspect;
                    var scale = halfHeight * 2 / height; var position = camera.transform.position;
                    Assert.That(position.x - halfWidth, Is.GreaterThanOrEqualTo(-1 - safe.xMin * scale - 0.001f));
                    Assert.That(position.x + halfWidth, Is.LessThanOrEqualTo(25 + (width - safe.xMax) * scale + 0.001f));
                    Assert.That(position.y - halfHeight, Is.GreaterThanOrEqualTo(-1 - safe.yMin * scale - 0.001f));
                    Assert.That(position.y + halfHeight, Is.LessThanOrEqualTo(25 + (height - safe.yMax) * scale + 0.001f));
                    follow.SetVisualOffset(new Vector2(0.03f, -0.02f)); follow.Present(corner, 6000, 0, safe);
                    Assert.That(Vector3.Distance(camera.transform.position, position), Is.LessThan(0.00001f), "Shake must not accumulate into the safe framing offset.");
                }
            }
            finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(owner); }
        }

        [Test]
        public void InteriorLordTargetsUsableCenterWithinAnOffsetViewport()
        {
            var owner = new GameObject("Offset viewport camera"); var camera = owner.AddComponent<Camera>();
            var target = new RenderTexture(1024, 1700, 24); camera.targetTexture = target;
            var viewport = new Rect(60, 40, 900, 1600); camera.pixelRect = viewport;
            var safe = new Rect(108, 220, 828, 1180);
            try
            {
                var actualViewport = camera.pixelRect;
                Assert.That(actualViewport.x, Is.EqualTo(viewport.x).Within(0.001f));
                Assert.That(actualViewport.y, Is.EqualTo(viewport.y).Within(0.001f));
                Assert.That(actualViewport.width, Is.EqualTo(viewport.width).Within(0.001f));
                Assert.That(actualViewport.height, Is.EqualTo(viewport.height).Within(0.001f));
                var follow = new RunCamera(camera, new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800));
                follow.SetMapBounds(24000, 24000); follow.Present(new Vector2(12, 12), 6000, 0, safe);
                var point = camera.WorldToScreenPoint(new Vector3(12, 12));
                Assert.That(point.x, Is.EqualTo(safe.center.x).Within(0.01f));
                Assert.That(point.y, Is.EqualTo(safe.center.y).Within(0.01f));
            }
            finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(owner); }
        }
    }
}
