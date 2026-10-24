using System.Linq;
using Game.App;
using Game.App.Generated;
using NUnit.Framework;
using SowSiege.Core;
using UnityEngine;

namespace Tests.EditMode
{
    public sealed class EstateMapTests
    {
        static EstateMapModel Model()
        {
            var model = new EstateMapModel(16, 8); model.SetViewport(new Vector2(370, 496)); return model;
        }

        [Test] public void ThreeZoomLevelsShowTheWholeMapANeighbourhoodAndOneBuilding()
        {
            var model = Model();
            Assert.That(model.Zoom, Is.EqualTo(EstateMapModel.Neighbourhood));
            Assert.That(model.CellAt(EstateMapModel.WholeMap), Is.EqualTo(370f / 16).Within(1e-4f), "The whole 16x8 map fits the width.");
            Assert.That(model.CellAt(EstateMapModel.Neighbourhood), Is.EqualTo(370f / 8).Within(1e-4f));
            Assert.That(model.CellAt(EstateMapModel.BuildingDetail), Is.EqualTo(370f / 4).Within(1e-4f));
            var centre = model.Viewport * .5f;
            Assert.That(model.StepZoom(1, centre), Is.True); Assert.That(model.Zoom, Is.EqualTo(EstateMapModel.BuildingDetail));
            Assert.That(model.StepZoom(1, centre), Is.False, "There is no fourth level.");
            Assert.That(model.SetZoom(EstateMapModel.WholeMap, centre), Is.True);
            Assert.That(model.StepZoom(-1, centre), Is.False);
            Assert.That(model.MapSize.x, Is.LessThanOrEqualTo(model.Viewport.x + 1e-3f));
            Assert.That(model.MapSize.y, Is.LessThanOrEqualTo(model.Viewport.y + 1e-3f));
        }

        [Test] public void ZoomKeepsTheGridPointUnderTheFingers()
        {
            var model = Model(); model.SetZoom(EstateMapModel.BuildingDetail, model.Viewport * .5f); model.CentreOn(new Vector2(8, 4));
            var focus = new Vector2(150, 260); var before = model.ViewportToGrid(focus);
            Assert.That(model.StepZoom(-1, focus), Is.True);
            // The 8 rows fit the neighbourhood viewport, so only the horizontal axis can keep its point; rows re-centre.
            Assert.That(model.ViewportToGrid(focus).x, Is.EqualTo(before.x).Within(1e-3f));
        }

        [Test] public void PanStopsAtTheMapEdgesAndCentresAShortAxis()
        {
            var model = Model();
            model.Pan(new Vector2(1000, 1000));
            Assert.That(model.Offset.x, Is.EqualTo(0).Within(1e-3f), "Dragging right cannot reveal space left of the map.");
            Assert.That(model.Offset.y, Is.EqualTo((model.MapSize.y - 496) * .5f).Within(1e-3f), "The 8 rows are shorter than the viewport, so they stay centred.");
            model.Pan(new Vector2(-5000, 0));
            Assert.That(model.Offset.x, Is.EqualTo(model.MapSize.x - 370).Within(1e-3f));
            var grid = model.ViewportToGrid(new Vector2(10, 200));
            Assert.That(model.GridToViewport(grid).x, Is.EqualTo(10).Within(1e-3f));
        }

        [Test] public void PinchStepsOnlyPastTheRatioAndTapsMapToCells()
        {
            Assert.That(EstateMapModel.PinchDirection(1.2f), Is.EqualTo(0));
            Assert.That(EstateMapModel.PinchDirection(1.4f), Is.EqualTo(1));
            Assert.That(EstateMapModel.PinchDirection(.7f), Is.EqualTo(-1));
            var model = Model(); model.CentreOn(Vector2.zero);
            var origin = model.GridToViewport(new Vector2(2.5f, 3.5f));
            Assert.That(model.TryCell(origin, out var cell), Is.True); Assert.That(cell, Is.EqualTo(new Vector2Int(2, 3)));
            Assert.That(model.TryCell(model.GridToViewport(new Vector2(-.5f, 1)), out _), Is.False);
            Assert.That(model.Anchor(new Vector2Int(15, 7), 2, 2), Is.EqualTo(new Vector2Int(14, 6)), "A footprint never hangs off the grid.");
            Assert.That(model.Anchor(new Vector2Int(5, 5), 3, 3), Is.EqualTo(new Vector2Int(4, 4)), "Odd footprints centre on the tapped cell.");
        }

        static EstateMapScreen Screen(out MetaState account)
        {
            var config = CanonicalContent.CreateAccountEstateConfig();
            Assert.That(config, Is.Not.Null, "The Unity export carries the M3a prototype config.");
            var catalog = CanonicalContent.CreateMetaCatalog(); account = MetaEngine.NewGame(catalog);
            return new EstateMapScreen(catalog, config, account);
        }

        [Test] public void TheMapWorksOnAMigratedCopyAndLeavesTheAccountAlone()
        {
            var screen = Screen(out var account); var before = MetaSaveCodec.Encode(account);
            Assert.That(screen.FromSave, Is.False);
            Assert.That(screen.State.Estate, Is.Not.Null);
            Assert.That(screen.State.Estate.Buildings.Length, Is.EqualTo(screen.Config.Buildings.Length));
            Assert.That(screen.Map.Columns, Is.EqualTo(16)); Assert.That(screen.Map.Rows, Is.EqualTo(8));
            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            screen.Select(forge.Id); screen.BeginMove(); screen.Place(new Vector2Int(4, 4));
            Assert.That(screen.PreviewValid, Is.True, screen.PreviewReason);
            Assert.That(screen.Confirm(), Is.True);
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(4)); Assert.That(screen.Building(forge.Id).Y, Is.EqualTo(4));
            Assert.That(screen.Moving, Is.False);
            Assert.That(account.Estate, Is.Null);
            Assert.That(MetaSaveCodec.Encode(account), Is.EqualTo(before), "The prototype never changes the account it was copied from.");
            Assert.That(screen.State.Estate.ElapsedSeconds, Is.EqualTo(0), "Previews and moves credit no time.");
        }

        [Test] public void APlacementShowsCoreReasonsAndCancelKeepsTheOldPlace()
        {
            var screen = Screen(out _);
            var forge = screen.State.Estate.Buildings.Single(b => b.Kind == "meta:forge");
            var well = screen.State.Estate.Buildings.Single(b => b.Kind == "m3a:well_shrine");
            screen.Select(forge.Id); screen.BeginMove();
            Assert.That(screen.PreviewValid, Is.True); Assert.That(screen.PreviewReason, Is.EqualTo("unchanged"));
            Assert.That(screen.ReasonText(screen.PreviewReason), Is.EqualTo("지금 자리입니다"));
            screen.Place(new Vector2Int(well.X, well.Y));
            Assert.That(screen.PreviewValid, Is.False);
            Assert.That(screen.PreviewReason, Is.EqualTo("overlaps:" + well.Id));
            Assert.That(screen.ReasonText(screen.PreviewReason), Is.EqualTo("우물과 겹칩니다"));
            Assert.That(screen.Confirm(), Is.False);
            screen.Place(new Vector2Int(9, 1));
            Assert.That(screen.PreviewReason, Is.EqualTo("outside-open-zone"));
            Assert.That(screen.ReasonText(screen.PreviewReason), Is.EqualTo("열린 구역 밖입니다"));
            screen.Cancel();
            Assert.That(screen.Moving, Is.False);
            Assert.That(screen.Building(forge.Id).X, Is.EqualTo(forge.X)); Assert.That(screen.Building(forge.Id).Y, Is.EqualTo(forge.Y));
        }
    }
}
