using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class FieldLandscapeTests
    {
        [TestCase(-12.01f, -2)]
        [TestCase(-12f, -1)]
        [TestCase(-.01f, -1)]
        [TestCase(0f, 0)]
        [TestCase(12f, 1)]
        public void SignedSectorsUseFloor(float coordinate, int expected)
            => Assert.That(FieldLandscape.Sector(coordinate), Is.EqualTo(expected));

        [Test] public void ReturningToSectorRecreatesSamePropsWithoutVisitState()
        {
            var expected = FieldLandscape.Prop(30001, -8, 11, 4);
            for (var i = -1000; i <= 1000; i++) FieldLandscape.Prop(30001, i, -i, 4);
            var actual = FieldLandscape.Prop(30001, -8, 11, 4);
            Assert.That(actual.Position, Is.EqualTo(expected.Position));
            Assert.That(actual.Size, Is.EqualTo(expected.Size));
            Assert.That(FieldLandscape.Prop(30002, -8, 11, 4).Position, Is.Not.EqualTo(expected.Position));
        }

        [Test] public void CreekBoundaryUsesSameGlobalEndpointFromEitherSector()
        {
            for (var sector = -20; sector < 20; sector++)
            {
                var lastSegment = sector * 4 + 3;
                var rightSectorFirst = (sector + 1) * 4;
                Assert.That(FieldLandscape.CreekPoint(30001, lastSegment + 1, -2),
                    Is.EqualTo(FieldLandscape.CreekPoint(30001, rightSectorFirst, -2)));
            }
        }

        [Test] public void WindowUvMapsNegativeWorldAndKeepsViewportInsideGutter()
        {
            var center = new Vector2(-27.2f, -11.7f);
            var window = FieldLandscape.MaskWindow(center, 10.667f, 6);
            Assert.That(window.Contains(center + new Vector2(10.667f, 6)), Is.True);
            Assert.That(window.Contains(center - new Vector2(10.667f, 6)), Is.True);
            var transform = FieldLandscape.MaskTransform(window);
            Assert.That(window.xMin * transform.x + transform.z, Is.EqualTo(0).Within(.00001));
            Assert.That(window.yMax * transform.y + transform.w, Is.EqualTo(1).Within(.00001));
            Assert.That(FieldLandscape.MaskWindow(center + new Vector2(.01f, .01f), 10.667f, 6), Is.EqualTo(window));
        }

        [Test] public void CameraPassesOldEdgesWithoutRectangleClamps()
        {
            var owner = new GameObject("Field camera test");
            try
            {
                var camera = owner.AddComponent<Camera>();
                var follow = new RunCamera(camera, new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800))
                { EndlessField = true, KeepViewportInsideMap = true, PresentationZoom = 1.15f };
                follow.SetMapBounds(12000, 12000);
                follow.Present(new Vector2(-30, -30), 6000, 0);
                Assert.That(camera.transform.position.x, Is.LessThan(-20));
                for (var i = 0; i < 120; i++) follow.Present(new Vector2(-30 - i * .02f, -30), 6000, 1f / 60);
                Assert.That(follow.MapClampCount, Is.Zero);
                Assert.That(camera.orthographicSize, Is.LessThanOrEqualTo(6));
                Assert.That(camera.aspect, Is.LessThanOrEqualTo(16f / 9 + .001f));
                follow.EndlessField = false;
                follow.Present(new Vector2(-30, -30), 6000, 0);
                Assert.That(follow.MapClampCount, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase(1920, 1080)]
        [TestCase(1440, 900)]
        [TestCase(1280, 800)]
        [TestCase(900, 1600)]
        [TestCase(3440, 1440)]
        public void SupportedViewportAndHudStayInsideSpawnEnvelope(int width, int height)
        {
            var owner = new GameObject("Field envelope test");
            var target = new RenderTexture(width, height, 0);
            var camera = owner.AddComponent<Camera>();
            camera.targetTexture = target;
            camera.pixelRect = new Rect(0, 0, width, height);
            try
            {
                var follow = new RunCamera(camera, new WorldCameraSettings(1000, 2400, 6000, 600, 120, 800))
                { EndlessField = true, PresentationZoom = 1.15f, LordViewportAnchor = new Vector2(.60f, .57f) };
                follow.SetMapBounds(12000, 12000);
                var lord = new Vector2(-36, 74);
                var safe = new Rect(0, height * .12f, width, height * .78f);
                follow.Present(lord, 6000, 0, safe);
                Assert.That(camera.aspect, Is.LessThanOrEqualTo(16f / 9 + .001f));
                foreach (var corner in new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up })
                {
                    var point = (Vector2)camera.ViewportToWorldPoint(new Vector3(corner.x, corner.y, 10));
                    Assert.That(Vector2.Distance(point, lord) + .5f + .264f + .6f, Is.LessThan(16.2f));
                }
                Assert.That(follow.MapClampCount, Is.Zero);
                follow.EndlessField = false;
                follow.Present(lord, 6000, 0, safe);
                Assert.That(camera.rect, Is.EqualTo(new Rect(0, 0, 1, 1)));
            }
            finally { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(owner); }
        }
    }
}
