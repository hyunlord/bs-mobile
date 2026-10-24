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
        // Design #360 section 5: three zoom levels [가설] — the whole estate, a neighbourhood (default, 64×32 cells) and one
        // building (128×64 cells, the source art size). Zero means "fit the whole map into the visible area".
        public static readonly float[] ZoomScales = { 0, 1, 2 };
        public const int WholeMap = 0, Neighbourhood = 1, BuildingDetail = 2;
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
            var scale = ZoomScales[Mathf.Clamp(zoom, 0, ZoomScales.Length - 1)];
            if (scale > 0) return scale;
            if (Viewport.x <= 0 || Viewport.y <= 0) return 1;
            return Mathf.Min(Visible.x / MapExtent.x, Visible.y / MapExtent.y);
        }

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
            zoom = Mathf.Clamp(zoom, 0, ZoomScales.Length - 1);
            if (zoom == Zoom) return false;
            var grid = ViewportToGrid(focus);
            Zoom = zoom; Offset = Project(grid) * Scale - focus; Clamp();
            return true;
        }

        public bool StepZoom(int direction, Vector2 focus) => SetZoom(Zoom + (direction > 0 ? 1 : direction < 0 ? -1 : 0), focus);

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
            var min = MapMin * Scale; var size = MapSize; var visible = Visible;
            Offset = new Vector2(Axis(Offset.x, min.x, size.x, 0, visible.x), Axis(Offset.y, min.y, size.y, TopInset, visible.y));
        }

        static float Axis(float offset, float min, float map, float inset, float visible) =>
            map <= visible ? min - inset - (visible - map) * .5f : Mathf.Clamp(offset, min - inset, min + map - inset - visible);
    }
}
