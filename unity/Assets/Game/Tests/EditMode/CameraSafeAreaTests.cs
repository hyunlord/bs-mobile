using Game.View;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class CameraSafeAreaTests
    {
        // Recorded normal-projectile-04 phase 0, frames 30-93. No native display timing claim.
        static readonly float[] RecordedFrameIntervals = { 0.007728210f, 0.008202834f, 0.009143835f, 0.007908829f, 0.016285041f, 0.008256002f, 0.009219876f, 0.008243622f, 0.008232292f, 0.008420628f, 0.008328876f, 0.008373746f, 0.008134375f, 0.008537791f, 0.008329167f, 0.008329585f, 0.008293374f, 0.008301793f, 0.007744791f, 0.009002169f, 0.008248873f, 0.008391291f, 0.007779415f, 0.008892255f, 0.008280499f, 0.008322626f, 0.008361876f, 0.007649079f, 0.009015795f, 0.008359120f, 0.008313584f, 0.016785417f, 0.008008709f, 0.008638669f, 0.008261749f, 0.008294664f, 0.008328252f, 0.008342375f, 0.008326084f, 0.008364002f, 0.008344289f, 0.008336997f, 0.016634587f, 0.008333623f, 0.008336331f, 0.008362875f, 0.008291291f, 0.008371089f, 0.007756541f, 0.008939293f, 0.008128040f, 0.008500042f, 0.008307745f, 0.008372506f, 0.008223165f, 0.008310955f, 0.008574589f, 0.016620288f, 0.008264498f, 0.008218297f, 0.007625078f, 0.008368793f, 0.009218339f, 0.008099327f };

        [Test]
        public void VariableFrameIntervalsFollowContinuousLinearTargetThenStationaryTarget()
        {
            var owner=new GameObject("Linear follow regression");
            try
            {
                var camera=owner.AddComponent<Camera>();
                var follow=new RunCamera(camera,new WorldCameraSettings(1000,2400,6000,600,120,800));
                follow.Present(Vector2.zero,2400,0);
                double time=0;
                foreach(var dt in RecordedFrameIntervals)
                {
                    time+=dt;
                    follow.Present(new Vector2((float)time,0),2400,dt);
                    var expected=time-.12*(1-System.Math.Exp(-time/.12));
                    Assert.That(camera.transform.position.x,Is.EqualTo(expected).Within(.000002),"Variable dt must integrate the moving target, not hold its endpoint for the whole frame.");
                }
                var stop=(float)time;
                var initial=camera.transform.position.x;
                time=0;
                foreach(var dt in RecordedFrameIntervals)
                {
                    time+=dt;
                    follow.Present(new Vector2(stop,0),2400,dt);
                    var expected=stop+(initial-stop)*System.Math.Exp(-time/.12);
                    Assert.That(camera.transform.position.x,Is.EqualTo(expected).Within(.000002));
                }
                var before=camera.transform.position;
                follow.Present(new Vector2(stop+1,0),2400,0);
                Assert.That(camera.transform.position,Is.EqualTo(before),"Paused presentation must not advance follow.");
                follow.SnapNextPresentation();follow.Present(new Vector2(4,7),2400,.016f);
                Assert.That(camera.transform.position,Is.EqualTo(new Vector3(4,7,-10)));
                foreach(var dt in RecordedFrameIntervals)follow.Present(new Vector2(4,7),2400,dt);
                Assert.That(camera.transform.position,Is.EqualTo(new Vector3(4,7,-10)),"Snap resets prior target velocity.");
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [Test]
        public void MovingTargetBeyondMapEdgeDoesNotAccumulateUnclampedVelocity()
        {
            var owner=new GameObject("Clamped follow regression");
            try
            {
                var camera=owner.AddComponent<Camera>();camera.aspect=1;
                var follow=new RunCamera(camera,new WorldCameraSettings(1000,2400,6000,600,120,800)){KeepViewportInsideMap=true};
                follow.SetMapBounds(24000,24000);follow.Present(new Vector2(100,12),2400,0);
                var atEdge=camera.transform.position;
                foreach(var dt in RecordedFrameIntervals)
                {
                    follow.Present(new Vector2(200,12),2400,dt);
                    Assert.That(camera.transform.position,Is.EqualTo(atEdge));
                }
                follow.Present(new Vector2(12,12),2400,.016f);
                Assert.That(camera.transform.position.x,Is.LessThan(atEdge.x));
                Assert.That(camera.transform.position.x,Is.GreaterThan(12));
            }
            finally { Object.DestroyImmediate(owner); }
        }

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
