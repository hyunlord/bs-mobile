using Game.App;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Tests
{
    public sealed class WorldMapCameraTests
    {
        static WorldMapChunk Chunk(int x, int y) => new WorldMapChunk
        {
            WorldId = "test", ContentHash = new string('a', 64), X = x, Y = y, Size = 32,
            Tiles = new[] { new WorldMapTile { X = x * 32, Y = y * 32, Terrain = "plain", Passable = true } },
            Targets = new WorldMapTarget[0]
        };
        [Test] public void RefreshRetainsDragAndZoomUntilNavigationChangesOrigin()
        {
            var camera = new WorldMapCameraState();
            var model = camera.Observe(Chunk(0, 0), 12, 12, new Vector2(354, 370));
            model.Pan(new Vector2(50, 20)); model.StepZoom(1, model.VisibleCentre);
            var offset = model.Offset; var zoom = model.Zoom;
            Assert.That(camera.Observe(Chunk(0, 0), 12, 12, new Vector2(354, 370)), Is.SameAs(model));
            Assert.That(model.Offset, Is.EqualTo(offset)); Assert.That(model.Zoom, Is.EqualTo(zoom));
            camera.Observe(Chunk(1, 0), 32, 12, new Vector2(354, 370));
            Assert.That(camera.Model, Is.Not.SameAs(model)); Assert.That(camera.Model.Zoom, Is.EqualTo(zoom));
        }
        [Test] public void UnobservedAndClearedWorldCannotRetainCameraOrPendingNavigation()
        {
            var camera = new WorldMapCameraState(); camera.Observe(Chunk(1, 2), 32, 64, new Vector2(354, 370));
            camera.RequestedChunkMove = Vector2Int.right; camera.Observe(null, 0, 0, Vector2.zero);
            Assert.That(camera.Model, Is.Null); Assert.That(camera.Chunk, Is.Null); Assert.That(camera.RequestedChunkMove, Is.Null);
        }
        [Test] public void FarObjectsAreCulledAndUnsupportedTargetsDoNotBecomeGuessedResources()
        {
            var model = new EstateMapModel(32, 32); model.SetViewport(new Vector2(354, 370)); model.CentreOn(new Vector2(16, 16));
            Assert.That(WorldMapCameraState.Visible(model, new Vector2(16, 16), 0), Is.True);
            Assert.That(WorldMapCameraState.Visible(model, new Vector2(0, 31), 0), Is.False);
            Assert.That(WorldMapCameraState.TargetRole(new WorldMapTarget { Kind = "resource", ContentId = "unknown" }), Is.Null);
            Assert.That(WorldMapCameraState.TargetRole(new WorldMapTarget { Kind = "resource", ContentId = "meta:iron" }), Is.EqualTo("world-resource-iron"));
        }
        [Test] public void EdgeGestureDoesNotRequestUnobservedNegativeWorldOrTreatTapAsNavigation()
        {
            var chunk = Chunk(1, 1); var model = new EstateMapModel(32, 32);
            model.SetViewport(new Vector2(100, 100)); model.CentreOn(new Vector2(31, 16));
            Assert.That(WorldMapCameraState.EdgeMove(model, chunk, new Vector2(-160, 80)).x, Is.EqualTo(1));
            Assert.That(WorldMapCameraState.EdgeMove(model, chunk, new Vector2(-2, 1)), Is.EqualTo(Vector2Int.zero));
            model.CentreOn(new Vector2(0, 0));
            Assert.That(WorldMapCameraState.EdgeMove(model, Chunk(0, 0), new Vector2(160, -80)), Is.EqualTo(Vector2Int.zero));
        }
        [Test] public void EdgeNavigationCrossesAnEntireChunkInsteadOfRepeatingSevenCellStep()
        {
            var chunk = Chunk(2, 1); var model = new EstateMapModel(32, 32);
            model.SetViewport(new Vector2(354, 370)); model.CentreOn(new Vector2(31, 16));
            var east = WorldMapCameraState.AdjacentOrigin(chunk, model, 7, Vector2Int.right);
            Assert.That(east.x / 32, Is.EqualTo(3)); Assert.That(east.y / 32, Is.EqualTo(1));
            var west = WorldMapCameraState.AdjacentOrigin(chunk, model, 7, Vector2Int.left);
            Assert.That(west.x, Is.EqualTo(57)); Assert.That(west.x / 32, Is.EqualTo(1));
        }
        [TestCase(true)] [TestCase(false)]
        public void PinchReleaseNeverNavigatesAnAdjacentChunk(bool releaseFirstPointerFirst)
        {
            var go = new GameObject("World gesture regression", typeof(RectTransform));
            var events = new GameObject("World gesture events", typeof(EventSystem));
            try
            {
                ((RectTransform)go.transform).sizeDelta = new Vector2(100, 100);
                var model = new EstateMapModel(32, 32); model.SetViewport(new Vector2(100, 100)); model.CentreOn(new Vector2(31, 16));
                var view = go.AddComponent<EstateMapView>(); view.Model = model;
                var drawing = go.AddComponent<WorldMapDrawing>(); drawing.Model = model; drawing.Chunk = Chunk(1, 1);
                var requests = 0; drawing.Move = (x, y) => requests++;
                var first = new PointerEventData(events.GetComponent<EventSystem>()) { pointerId = 1, position = new Vector2(30, 50), dragging = true };
                var second = new PointerEventData(events.GetComponent<EventSystem>()) { pointerId = 2, position = new Vector2(70, 50), dragging = true };
                ExecuteEvents.Execute(go, first, ExecuteEvents.beginDragHandler);
                ExecuteEvents.Execute(go, second, ExecuteEvents.beginDragHandler);
                first.position = new Vector2(-130, 130);
                ExecuteEvents.Execute(go, first, ExecuteEvents.dragHandler);
                ExecuteEvents.Execute(go, releaseFirstPointerFirst ? first : second, ExecuteEvents.endDragHandler);
                Assert.That(requests, Is.Zero, "The first released pinch finger must not navigate.");
                ExecuteEvents.Execute(go, releaseFirstPointerFirst ? second : first, ExecuteEvents.endDragHandler);
                Assert.That(requests, Is.Zero, "A completed multi-pointer gesture remains zoom-only.");
                model.CentreOn(new Vector2(31, 16));
                first.position = new Vector2(30, 50);
                ExecuteEvents.Execute(go, first, ExecuteEvents.beginDragHandler);
                first.position = new Vector2(-130, 130);
                ExecuteEvents.Execute(go, first, ExecuteEvents.endDragHandler);
                Assert.That(requests, Is.EqualTo(1), "A subsequent single-pointer edge drag still navigates.");
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(events); }
        }
        [Test] public void RegisteredObjectRolesPreserveApprovedAnchorsAndAllCastleVariants()
        {
            var art = WorldMapArt.Load();
            foreach (var tier in new[] { 1, 2, 3 })
            {
                var role = "world-castle-tier" + tier;
                Assert.That(art.Has(role), Is.True); Assert.That(art.Get(role).pivot.x / art.Size(role).x, Is.EqualTo(.5f).Within(.001f));
            }
            Assert.That(art.Has("world-status-gathering"), Is.True);
            Assert.That(art.Has("world-status-combat"), Is.True);
        }
    }
}
