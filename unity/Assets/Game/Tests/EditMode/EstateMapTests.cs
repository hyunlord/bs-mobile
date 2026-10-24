using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Tests.EditMode
{
    public sealed class EstateMapTests
    {
        const float Top = 94, Bottom = 72;
        // The wide prototype map (#374) keeps the r2 layout shifted by (26, 26) into its 20×20 centre zone at (20, 20);
        // (45, 30) is closed land in the village extension.
        static Vector2Int C(int u, int v) => new Vector2Int(u + 26, v + 26);
        static readonly Vector2Int Closed = new Vector2Int(45, 30);
        static EstateMapModel Model()
        {
            var model = new EstateMapModel(16, 8); model.SetFrame(new Vector2(390, 844), Top, Bottom); return model;
        }

        [Test] public void CellsAreSixtyFourByThirtyTwoDiamonds()
        {
            Assert.That(EstateMapModel.Project(new Vector2(1, 0)), Is.EqualTo(new Vector2(32, 16)));
            Assert.That(EstateMapModel.Project(new Vector2(0, 1)), Is.EqualTo(new Vector2(-32, 16)));
            Assert.That(EstateMapModel.Project(new Vector2(1, 1)), Is.EqualTo(new Vector2(0, 32)), "x=(u−v)·32, y=(u+v)·16: one cell is 64 wide and 32 tall.");
            Assert.That(EstateMapModel.Project(new Vector2(5, 3)), Is.EqualTo(new Vector2(64, 128)));
            foreach (var grid in new[] { new Vector2(0, 0), new Vector2(3.25f, 7.5f), new Vector2(15.9f, .1f) })
                Assert.That(Vector2.Distance(EstateMapModel.Unproject(EstateMapModel.Project(grid)), grid), Is.LessThan(1e-4f));
        }

        [Test] public void FourZoomLevelsShowTheWholeMapTheLitLandANeighbourhoodAndOneBuilding()
        {
            var model = Model();
            Assert.That(model.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));
            Assert.That(model.ScaleAt(EstateMapModel.Neighbourhood), Is.EqualTo(1), "Default cells are 64×32 on screen.");
            Assert.That(model.ScaleAt(EstateMapModel.BuildingDetail), Is.EqualTo(2), "Building zoom shows cells at the 128×64 source size.");
            Assert.That(model.ScaleAt(model.WholeMap), Is.EqualTo(390f / (24 * 32)).Within(1e-4f), "The whole 16×8 diamond fits the width.");
            Assert.That(model.ScaleAt(model.Opening), Is.EqualTo(model.ScaleAt(model.WholeMap)), "Without a focus the lit-land level fits the whole map too.");
            var centre = model.VisibleCentre;
            Assert.That(model.StepZoom(1, centre), Is.True); Assert.That(model.StepZoom(1, centre), Is.False, "There is no fifth level.");
            Assert.That(model.SetZoom(model.WholeMap, centre), Is.True); Assert.That(model.StepZoom(-1, centre), Is.False);
            Assert.That(model.GridToViewport(new Vector2(0, 8)).x, Is.GreaterThanOrEqualTo(-1e-3f));
            Assert.That(model.GridToViewport(new Vector2(16, 0)).x, Is.LessThanOrEqualTo(390 + 1e-3f));
        }

        [Test] public void TheWholeMapZoomFitsTheZonesNotTheEmptyGridCorners()
        {
            // The design r3 centre + inner ring (#374): a plus of five 20×20 zones on a 60×60 grid. Its projected extent is
            // 2560×1280 (u − v from −40 to 40, u + v from 20 to 100), so the empty grid corners are not part of the map (#375).
            var zones = new[] { new RectInt(20, 20, 20, 20), new RectInt(40, 20, 20, 20), new RectInt(20, 40, 20, 20), new RectInt(0, 20, 20, 20), new RectInt(20, 0, 20, 20) };
            var model = new EstateMapModel(60, 60, zones); model.SetFrame(new Vector2(390, 844), Top, Bottom);
            Assert.That(model.MapExtent, Is.EqualTo(new Vector2(2560, 1280)));
            Assert.That(model.ScaleAt(model.WholeMap), Is.EqualTo(390f / 2560).Within(1e-5f), "The plus fits the width.");
            model.SetZoom(model.WholeMap, model.VisibleCentre);
            Assert.That(model.GridToViewport(new Vector2(0, 40)).x, Is.EqualTo(0).Within(1e-3f), "West zone's corner at the left edge.");
            Assert.That(model.GridToViewport(new Vector2(60, 20)).x, Is.EqualTo(390).Within(1e-3f), "East zone's corner at the right edge.");
            var top = model.GridToViewport(new Vector2(20, 0)).y; var bottom = model.GridToViewport(new Vector2(60, 40)).y;
            Assert.That(top, Is.GreaterThanOrEqualTo(Top - 1e-3f)); Assert.That(bottom, Is.LessThanOrEqualTo(844 - Bottom + 1e-3f), "Every zone is on screen.");
            Assert.That(new EstateMapModel(16, 8).MapExtent, Is.EqualTo(new Vector2(24 * 32, 24 * 16)), "Without zones the grid diamond is the map.");
        }

        // #379: the first view fits the open land plus a ring of night (key art C). The two fitted levels are ordered by scale:
        // with r4's centre as wide as the estate, the lit land with its ring is the farther view; a narrow open zone puts it
        // between the whole map and the neighbourhood.
        [Test] public void TheLitLandLevelFitsOpenZonesWithARingOfNight()
        {
            var r4 = new[] { new RectInt(20, 20, 20, 20), new RectInt(40, 28, 8, 12), new RectInt(0, 0, 20, 10), new RectInt(0, 10, 28, 10), new RectInt(8, 20, 12, 8),
                new RectInt(32, 40, 8, 12), new RectInt(40, 40, 10, 20), new RectInt(50, 40, 10, 20), new RectInt(60, 48, 8, 12) };
            var model = new EstateMapModel(68, 60, r4); model.SetFrame(new Vector2(390, 844), Top, Bottom);
            model.SetFocus(new[] { new Rect(17.5f, 17.5f, 25, 25) });
            Assert.That(model.FocusIsWider, Is.True); Assert.That((model.Opening, model.WholeMap), Is.EqualTo((0, 1)));
            Assert.That(model.ScaleAt(model.Opening), Is.EqualTo(390f / 1600).Within(1e-5f), "The centre plus 2.5 cells each side fits the width.");
            Assert.That(model.ScaleAt(model.WholeMap), Is.EqualTo(390f / 1280).Within(1e-5f));
            model.Restore(model.Opening, model.FocusCentre);
            Assert.That(model.GridToViewport(new Vector2(20, 40)).x, Is.GreaterThan(30), "Night shows left of the lit centre.");
            Assert.That(model.GridToViewport(new Vector2(40, 20)).x, Is.LessThan(360), "Night shows right of the lit centre.");
            model.SetFocus(new[] { new Rect(25.5f, 25.5f, 9, 9) });
            Assert.That(model.FocusIsWider, Is.False); Assert.That((model.WholeMap, model.Opening), Is.EqualTo((0, 1)));
            Assert.That(model.ScaleAt(model.Opening), Is.InRange(model.ScaleAt(model.WholeMap), 1f), "A small lit land sits between the whole map and the neighbourhood.");
        }

        [Test] public void ZoomKeepsTheGridPointUnderTheFingers()
        {
            var model = Model(); model.SetZoom(EstateMapModel.BuildingDetail, model.VisibleCentre); model.CentreOn(new Vector2(8, 4));
            var focus = new Vector2(150, 400); var grid = model.ViewportToGrid(focus);
            Assert.That(model.StepZoom(-1, focus), Is.True);
            // The neighbourhood diamond is shorter than the visible band, so it re-centres vertically; across, the point stays put.
            Assert.That(model.GridToViewport(grid).x, Is.EqualTo(focus.x).Within(1e-2f));
        }

        [Test] public void PanStopsAtTheDiamondCornersAndKeepsTheMapBetweenTheBars()
        {
            var model = Model();
            model.Pan(new Vector2(1000, 0));
            Assert.That(model.GridToViewport(new Vector2(0, 8)).x, Is.EqualTo(0).Within(1e-3f), "The west corner stops at the left edge.");
            model.Pan(new Vector2(-5000, 0));
            Assert.That(model.GridToViewport(new Vector2(16, 0)).x, Is.EqualTo(390).Within(1e-3f), "The east corner stops at the right edge.");
            var visible = 844 - Top - Bottom;
            Assert.That(model.GridToViewport(Vector2.zero).y, Is.EqualTo(Top + (visible - 384) * .5f).Within(1e-3f), "A short map is centred between the bars.");
            model.SetFrame(model.Viewport, Top, Bottom + 158);
            Assert.That(model.GridToViewport(new Vector2(16, 8)).y, Is.LessThanOrEqualTo(844 - Bottom - 158 + 1e-3f), "An open panel never hides the south corner.");
            model.SetZoom(EstateMapModel.BuildingDetail, model.VisibleCentre);
            model.Pan(new Vector2(0, -5000));
            Assert.That(model.GridToViewport(new Vector2(16, 8)).y, Is.EqualTo(844 - Bottom - 158).Within(1e-3f), "A tall map drags until its south corner clears the panel.");
            model.Pan(new Vector2(0, 5000));
            Assert.That(model.GridToViewport(Vector2.zero).y, Is.EqualTo(Top).Within(1e-3f), "…and until its north corner clears the top bars.");
        }

        [Test] public void TapsHitDiamondsNotRectangles()
        {
            var model = Model(); model.CentreOn(new Vector2(4, 4));
            Assert.That(model.TryCell(model.GridToViewport(new Vector2(2.5f, 3.5f)), out var cell), Is.True); Assert.That(cell, Is.EqualTo(new Vector2Int(2, 3)));
            // Points just inside and just outside the west edge of cell (2, 3)'s diamond.
            Assert.That(model.TryCell(model.GridToViewport(new Vector2(2.02f, 3.98f)), out cell), Is.True); Assert.That(cell, Is.EqualTo(new Vector2Int(2, 3)));
            Assert.That(model.TryCell(model.GridToViewport(new Vector2(1.98f, 3.5f)), out cell), Is.True); Assert.That(cell, Is.EqualTo(new Vector2Int(1, 3)));
            Assert.That(model.TryCell(model.GridToViewport(new Vector2(-.5f, 1)), out _), Is.False);
            Assert.That(EstateMapModel.PinchDirection(1.2f), Is.EqualTo(0));
            Assert.That(EstateMapModel.PinchDirection(1.4f), Is.EqualTo(1));
            Assert.That(EstateMapModel.PinchDirection(.7f), Is.EqualTo(-1));
            Assert.That(model.Anchor(new Vector2Int(15, 7), 2, 2), Is.EqualTo(new Vector2Int(14, 6)), "A footprint never hangs off the grid.");
        }

        [Test] public void DrawOrderFollowsTheFrontCornerThenInstanceOrder()
        {
            Assert.That(EstateMapModel.SortY(0, 0, 2, 2), Is.EqualTo(64));
            Assert.That(EstateMapModel.SortY(6, 0, 2, 2), Is.EqualTo(EstateMapModel.SortY(0, 6, 2, 2)), "Mirrored places tie and fall back to instance order.");
            var screen = Screen(out _);
            var order = screen.State.Estate.Buildings.OrderBy(b => EstateMapModel.SortY(b.X, b.Y, 2, 2)).ThenBy(b => b.Id).Select(b => b.Kind).ToArray();
            Assert.That(order.First(), Is.EqualTo("meta:granary"), "The north corner is drawn first.");
            Assert.That(order.Last(), Is.EqualTo("m3a:watchtower"), "The south corner is drawn last.");
        }

        [Test] public void APinchAllocatesNothingWhileTheFingersMove()
        {
            var host = new GameObject("Estate map view", typeof(RectTransform));
            try
            {
                var rect = (RectTransform)host.transform; rect.sizeDelta = new Vector2(370, 496);
                var view = host.AddComponent<EstateMapView>(); view.Model = new EstateMapModel(16, 8);
                view.Model.SetFrame(rect.rect.size, 0, 0); view.Layout();
                var first = new PointerEventData(null) { pointerId = 1, position = new Vector2(100, 200) };
                var second = new PointerEventData(null) { pointerId = 2, position = new Vector2(200, 200) };
                view.OnBeginDrag(first); view.OnBeginDrag(second);
                void Move(float x) { second.position = new Vector2(x, 200); view.OnDrag(second); }
                for (var i = 0; i < 4; i++) Move(201 + i); // warm up
                var before = System.GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 200; i++) Move(205 + i % 20);
                Move(260); // one zoom step inside the measured window: 160/100 > the step ratio
                var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(view.Model.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail));
                Assert.That(allocated, Is.EqualTo(0), "Pinch and pan must not allocate per drag event (AGENTS.md hot-loop rule).");
            }
            finally { Object.DestroyImmediate(host); }
        }

        static EstateMapScreen Screen(out MetaState account)
        {
            var config = CanonicalContent.CreateAccountEstateConfig();
            Assert.That(config, Is.Not.Null, "The Unity export carries the M3a prototype config.");
            var catalog = CanonicalContent.CreateMetaCatalog(); account = MetaEngine.NewGame(catalog);
            return new EstateMapScreen(catalog, config, account);
        }

        [Test] public void FootprintsAnswerTapsOnEveryCellTheyCover()
        {
            var screen = Screen(out _);
            foreach (var building in screen.State.Estate.Buildings)
                for (var du = 0; du < 2; du++) for (var dv = 0; dv < 2; dv++)
                    Assert.That(screen.BuildingAt(new Vector2Int(building.X + du, building.Y + dv)), Is.EqualTo(building.Id), building.Kind);
            Assert.That(screen.BuildingAt(C(4, 4)), Is.Null);
            Assert.That(screen.BuildingAt(Closed), Is.Null);
        }

        [Test] public void TheIsometricViewKeepsCoreGridCoordinatesAndSaveBytes()
        {
            var screen = Screen(out var account); var accountBytes = MetaSaveCodec.Encode(account); var copyBytes = MetaSaveCodec.Encode(screen.State);
            var model = screen.Map; model.SetFrame(new Vector2(390, 844), Top, Bottom); model.CentreOn(screen.HomeCentre());
            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            // Select, preview two places and cancel: nothing is written, the copy's bytes stay identical.
            screen.Select(forge.Id); screen.BeginMove();
            Assert.That(model.TryCell(model.GridToViewport(C(4, 4) + new Vector2(.5f, .5f)), out var target), Is.True);
            screen.Place(target); screen.Place(C(2, 2)); screen.Cancel(); screen.Select(null);
            Assert.That(MetaSaveCodec.Encode(screen.State), Is.EqualTo(copyBytes), "Viewing and previewing on the isometric map changes no saved byte.");
            // A confirmed move writes the tapped diamond's (u, v) as Core X and Y, unchanged by the projection.
            screen.Select(forge.Id); screen.BeginMove(); screen.Place(target);
            Assert.That(screen.Confirm(), Is.True);
            var result = MetaSaveCodec.Decode(MetaSaveCodec.Encode(screen.State)); Assert.That(result.Valid, Is.True, result.ErrorCode);
            var decoded = result.State.Estate.Buildings.Single(b => b.Id == forge.Id);
            Assert.That(new Vector2Int(decoded.X, decoded.Y), Is.EqualTo(C(4, 4)));
            Assert.That(MetaSaveCodec.Encode(account), Is.EqualTo(accountBytes), "The account the copy came from is never written.");
            Assert.That(screen.State.Estate.ElapsedSeconds, Is.EqualTo(0), "Previews and moves credit no time.");
        }

        [Test] public void APlacementShowsCoreReasonsAndCancelKeepsTheOldPlace()
        {
            var screen = Screen(out _);
            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            var well = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:well_shrine");
            screen.Select(forge.Id); screen.BeginMove();
            Assert.That(screen.PreviewValid, Is.True); Assert.That(screen.ReasonText(screen.PreviewReason), Is.EqualTo("지금 자리입니다"));
            screen.Place(new Vector2Int(well.X, well.Y));
            Assert.That(screen.PreviewValid, Is.False); Assert.That(screen.PreviewReason, Is.EqualTo("overlaps:" + well.Id));
            Assert.That(screen.ReasonText(screen.PreviewReason), Is.EqualTo("우물과 겹칩니다"));
            Assert.That(screen.Confirm(), Is.False);
            screen.Place(Closed);
            Assert.That(screen.ReasonText(screen.PreviewReason), Is.EqualTo("열린 구역 밖입니다"));
            screen.Cancel();
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(forge.X)); Assert.That(screen.Building(forge.Id).Y, Is.EqualTo(forge.Y));
        }

        [Test] public void TheGoalLineShowsTheFirstUnclaimedTaskAndFolds()
        {
            var screen = Screen(out _);
            Assert.That(screen.GoalText(), Is.EqualTo("목표 · 생산 재고를 처음 받기 · 보상 목재 20"));
            Assert.That(screen.GoalCollapsed, Is.False);
            screen.ToggleGoal(); Assert.That(screen.GoalCollapsed, Is.True);
        }
    }
}
