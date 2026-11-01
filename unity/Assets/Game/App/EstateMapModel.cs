using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.App
{
    // View-only camera of the estate map prototype (#366, isometric since #368). Core keeps integer grid cells (u, v) — the
    // saved X and Y — and this class only projects them: a cell is a 64×32 diamond at scale 1 (source art 128×64), with
    // x = (u − v)·32 and y = (u + v)·16. Viewport coordinates are canvas units from the viewport's top-left, y downward, like
    // PortraitWidgets.Place. Zoom and pan are presentation state: never saved, never a Core input (design #360 section 5).
    public sealed class EstateMapModel
    {
        public const float HalfWidth = 32, HalfHeight = 16;
        // Design #360 section 5 zoom levels [가설], from far to near: two fitted levels — the whole estate (all zones) and the
        // estate's lit land (#379: the first view, open zones plus a ring of night like key art C) — then a neighbourhood (64×32
        // cells) and one building (128×64 cells, the source art size). The fitted pair is ordered by scale: when the lit land
        // with its ring is wider than the whole estate (the r4 centre spans the estate's width), the lit view is the farther.
        public static readonly float[] ZoomScales = { 0, 0, 1, 2 };
        public const int Neighbourhood = 2, BuildingDetail = 3, Levels = 4;
        // Levels of the two fitted views.
        public int WholeMap => UsesBodyCamera ? 0 : FocusIsWider ? 1 : 0;
        public int Opening => UsesBodyCamera ? Neighbourhood : FocusIsWider ? 0 : 1;
        public bool UsesBodyCamera { get; private set; }
        float defaultBodyScale, minimumBodyScale;
        Rect[] allowedCentres = Array.Empty<Rect>();

        [Serializable]
        public sealed class CameraSettings
        {
            public float defaultBuildingWidthPx;
            public float minimumBuildingWidthPx;
            public float dragMarginCells;

            public static CameraSettings Load()
            {
                var asset = Resources.Load<TextAsset>("EstateZoneTerrainSettings");
                if (asset == null) throw new InvalidOperationException("Missing r8 estate camera settings.");
                var settings = JsonUtility.FromJson<CameraSettings>(asset.text);
                if (settings == null || !Positive(settings.defaultBuildingWidthPx) || !Positive(settings.minimumBuildingWidthPx)
                    || settings.minimumBuildingWidthPx > settings.defaultBuildingWidthPx || !Positive(settings.dragMarginCells))
                    throw new InvalidOperationException("Invalid r8 estate camera settings.");
                return settings;
            }
        }

        static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);

        // Body width excludes transparent canvas. Rectangles arrive in ordinal zone-ID order to break equal-distance ties.
        public void ConfigureBodyCamera(float bodyWidth, float canvasWidth, float displayedCanvasWidth,
            CameraSettings settings, IEnumerable<Rect> openedAreas)
        {
            if (!Positive(bodyWidth) || !Positive(canvasWidth) || !Positive(displayedCanvasWidth) || settings == null
                || !Positive(settings.defaultBuildingWidthPx) || !Positive(settings.minimumBuildingWidthPx)
                || settings.minimumBuildingWidthPx > settings.defaultBuildingWidthPx)
                throw new ArgumentException("Invalid r8 reference body or camera widths.");
            var nativeBodyWidth = bodyWidth / canvasWidth * displayedCanvasWidth;
            defaultBodyScale = settings.defaultBuildingWidthPx / nativeBodyWidth;
            minimumBodyScale = settings.minimumBuildingWidthPx / nativeBodyWidth;
            if (defaultBodyScale > ZoomScales[BuildingDetail]) throw new ArgumentException("Reference body exceeds the detail camera scale.");
            var areas = openedAreas.ToArray();
            if (areas.Length == 0 || areas.Any(a => !Positive(a.width) || !Positive(a.height)
                || float.IsNaN(a.x) || float.IsNaN(a.y) || float.IsInfinity(a.x) || float.IsInfinity(a.y)))
                throw new ArgumentException("The r8 camera requires finite opened-zone bounds.");
            allowedCentres = areas; UsesBodyCamera = true;
            if (Viewport.x > 0 && Viewport.y > 0) Clamp();
        }
        float WholeFit => Mathf.Min(Visible.x / MapExtent.x, Visible.y / MapExtent.y);
        float FocusFit => FocusExtent.x > 0 && FocusExtent.y > 0 ? Mathf.Min(1, Mathf.Min(Visible.x / FocusExtent.x, Visible.y / FocusExtent.y)) : WholeFit;
        // The order is read on the whole viewport, without the bars and panels, so opening a panel never swaps the two levels.
        public bool FocusIsWider => FocusExtent.x > 0 && FocusExtent.y > 0 && Viewport.x > 0 && Viewport.y > 0 &&
            Mathf.Min(Viewport.x / FocusExtent.x, Viewport.y / FocusExtent.y) < Mathf.Min(Viewport.x / MapExtent.x, Viewport.y / MapExtent.y);
        // A pinch steps one level once the finger distance has grown or shrunk by this ratio since the last step.
        public const float PinchStepRatio = 1.35f;

        public int Columns { get; }
        public int Rows { get; }
        public Vector2 Viewport { get; private set; }
        // Canvas units covered by the top and bottom bars (or an open panel); clamping keeps the map reachable between them.
        public float TopInset { get; private set; }
        public float BottomInset { get; private set; }
        public int Zoom { get; private set; } = Neighbourhood;
        // Scaled map position shown at the viewport's top-left corner (map origin = projection of cell corner (0, 0)).
        public Vector2 Offset { get; private set; }
        // False until the page first centres the map (on the home building); later visits keep the player's view.
        public bool Framed { get; private set; }

        // `areas` (cell rectangles, e.g. the config's zones) bound what the camera shows: the whole-map zoom fits their projected
        // extent and panning stops at it (#375). Without areas the whole grid diamond is the map.
        public EstateMapModel(int columns, int rows, IEnumerable<RectInt> areas = null)
        {
            Columns = Mathf.Max(1, columns); Rows = Mathf.Max(1, rows);
            var corners = (areas ?? new[] { new RectInt(0, 0, Columns, Rows) })
                .SelectMany(a => new[] { new Vector2(a.xMin, a.yMin), new Vector2(a.xMax, a.yMin), new Vector2(a.xMin, a.yMax), new Vector2(a.xMax, a.yMax) })
                .Select(Project).ToArray();
            if (corners.Length == 0) corners = new[] { Vector2.zero, Project(new Vector2(Columns, Rows)) };
            MapMin = new Vector2(corners.Min(c => c.x), corners.Min(c => c.y));
            MapExtent = new Vector2(corners.Max(c => c.x), corners.Max(c => c.y)) - MapMin;
        }

        public static Vector2 Project(Vector2 grid) => new Vector2((grid.x - grid.y) * HalfWidth, (grid.x + grid.y) * HalfHeight);
        public static Vector2 Unproject(Vector2 point) => new Vector2((point.x / HalfWidth + point.y / HalfHeight) * .5f, (point.y / HalfHeight - point.x / HalfWidth) * .5f);

        // Unscaled bounds of the map: by default the whole grid's diamond, left corner (0, Rows), right corner (Columns, 0),
        // bottom (Columns, Rows).
        public Vector2 MapMin { get; }
        public Vector2 MapExtent { get; }

        public float Scale => ScaleAt(Zoom);
        public Vector2 MapSize => MapExtent * Scale;
        Vector2 Visible => new Vector2(Viewport.x, Mathf.Max(1, Viewport.y - TopInset - BottomInset));

        public float ScaleAt(int zoom)
        {
            zoom = Mathf.Clamp(zoom, 0, Levels - 1);
            if (UsesBodyCamera)
            {
                if (zoom == 1) return minimumBodyScale;
                if (zoom == Neighbourhood) return defaultBodyScale;
                if (zoom == BuildingDetail) return ZoomScales[BuildingDetail];
                return Viewport.x > 0 && Viewport.y > 0 ? WholeFit : 1;
            }
            if (zoom >= Neighbourhood) return ZoomScales[zoom];
            if (Viewport.x <= 0 || Viewport.y <= 0) return 1;
            return zoom == Opening ? FocusFit : WholeFit;
        }

        // Unscaled extent of the focus (grid rectangles, e.g. the open zones plus a margin) that the Opening level fits.
        public Vector2 FocusExtent { get; private set; }
        public Vector2 FocusCentre { get; private set; }

        public void SetFocus(IEnumerable<Rect> areas)
        {
            var corners = areas.SelectMany(a => new[] { new Vector2(a.xMin, a.yMin), new Vector2(a.xMax, a.yMin), new Vector2(a.xMin, a.yMax), new Vector2(a.xMax, a.yMax) }).Select(Project).ToArray();
            if (corners.Length == 0) { FocusExtent = Vector2.zero; return; }
            var min = new Vector2(corners.Min(c => c.x), corners.Min(c => c.y)); var max = new Vector2(corners.Max(c => c.x), corners.Max(c => c.y));
            FocusExtent = max - min; FocusCentre = Unproject((min + max) * .5f);
        }

        // Opens on a remembered view, or on the focus at the Opening level the first time (#379).
        public void Restore(int zoom, Vector2 centre) { Zoom = Mathf.Clamp(zoom, 0, Levels - 1); CentreOn(centre); }

        public void SetViewport(Vector2 size) => SetFrame(size, TopInset, BottomInset);

        public void SetFrame(Vector2 size, float topInset, float bottomInset)
        {
            if (size == Viewport && topInset == TopInset && bottomInset == BottomInset) return;
            // A new screen size keeps the visible centre; a bar or panel opening keeps the map still and only re-clamps it.
            var resized = size != Viewport && Viewport.x > 0 && Viewport.y > 0;
            var centre = resized ? ViewportToGrid(VisibleCentre) : Vector2.zero;
            Viewport = size; TopInset = Mathf.Max(0, topInset); BottomInset = Mathf.Max(0, bottomInset);
            if (resized) CentreOn(centre); else Clamp();
        }

        public Vector2 VisibleCentre => new Vector2(Viewport.x * .5f, TopInset + Visible.y * .5f);

        // Keeps the grid point under `focus` (viewport units) where it is while the zoom changes.
        public bool SetZoom(int zoom, Vector2 focus)
        {
            zoom = Mathf.Clamp(zoom, 0, Levels - 1);
            if (zoom == Zoom) return false;
            var grid = ViewportToGrid(focus);
            Zoom = zoom; Offset = Project(grid) * Scale - focus; Clamp();
            return true;
        }

        public bool StepZoom(int direction, Vector2 focus)
        {
            if (direction == 0) return false;
            if (UsesBodyCamera)
            {
                if (Zoom == WholeMap) return direction > 0 && SetZoom(Opening, focus);
                return SetZoom(Mathf.Clamp(Zoom + (direction > 0 ? 1 : -1), 1, BuildingDetail), focus);
            }
            return SetZoom(Zoom + (direction > 0 ? 1 : -1), focus);
        }

        // Finger distance ratio since the pinch started (or since its last step) to a zoom direction.
        public static int PinchDirection(float ratio) => ratio >= PinchStepRatio ? 1 : ratio <= 1 / PinchStepRatio ? -1 : 0;

        // A drag moves the map with the finger, so the offset moves the other way.
        public void Pan(Vector2 delta) { Offset -= delta; Clamp(); }

        // True when a grid point projects inside the visible band between the insets.
        public bool InView(Vector2 grid)
        {
            var point = GridToViewport(grid);
            return point.x >= 0 && point.x <= Viewport.x && point.y >= TopInset && point.y <= Viewport.y - BottomInset;
        }

        public void CentreOn(Vector2 grid) { Offset = Project(grid) * Scale - VisibleCentre; Clamp(); Framed = true; }

        public Vector2 GridToViewport(Vector2 grid) => Project(grid) * Scale - Offset;
        public Vector2 ViewportToGrid(Vector2 point) => Unproject((point + Offset) / Scale);

        // The cell whose diamond contains the point.
        public bool TryCell(Vector2 point, out Vector2Int cell)
        {
            var grid = ViewportToGrid(point);
            cell = new Vector2Int(Mathf.FloorToInt(grid.x), Mathf.FloorToInt(grid.y));
            return cell.x >= 0 && cell.y >= 0 && cell.x < Columns && cell.y < Rows;
        }

        // The top-left cell of a footprint placed on the tapped cell (its top-left, or its centre for odd sizes), kept inside the grid.
        public Vector2Int Anchor(Vector2Int tapped, int width, int height) => new Vector2Int(
            Mathf.Clamp(tapped.x - (width - 1) / 2, 0, Mathf.Max(0, Columns - width)),
            Mathf.Clamp(tapped.y - (height - 1) / 2, 0, Mathf.Max(0, Rows - height)));

        // Draw order key: the unscaled screen y of a footprint's front (bottom) corner, where its art stands. Ties keep instance order.
        public static float SortY(int u, int v, int width, int height) => (u + width + v + height) * HalfHeight;

        // A map smaller than the visible area is centred in it; a larger one cannot be dragged past its edges.
        void Clamp()
        {
            if (UsesBodyCamera && Zoom != WholeMap)
            {
                var centre = ViewportToGrid(VisibleCentre);
                var nearest = centre; var distance = float.PositiveInfinity;
                foreach (var area in allowedCentres)
                {
                    var candidate = new Vector2(Mathf.Clamp(centre.x, area.xMin, area.xMax), Mathf.Clamp(centre.y, area.yMin, area.yMax));
                    var squared = (candidate - centre).sqrMagnitude;
                    if (squared < distance) { distance = squared; nearest = candidate; }
                }
                Offset = Project(nearest) * Scale - VisibleCentre;
                return;
            }
            var min = MapMin * Scale; var size = MapSize; var visible = Visible;
            Offset = new Vector2(Axis(Offset.x, min.x, size.x, 0, visible.x), Axis(Offset.y, min.y, size.y, TopInset, visible.y));
        }

        static float Axis(float offset, float min, float map, float inset, float visible) =>
            map <= visible ? min - inset - (visible - map) * .5f : Mathf.Clamp(offset, min - inset, min + map - inset - visible);
    }
}
