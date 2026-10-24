using UnityEngine;

namespace Game.App
{
    // View-only camera of the M3a-2 estate map prototype (#366). Coordinates are canvas units with the viewport's top-left
    // as the origin and y growing downward, like PortraitWidgets.Place. Zoom and pan are presentation state: they are never
    // saved and never reach Core (design #360 section 5, "확대·드래그 위치는 View 저장이며 전투 해시 입력이 아니다").
    public sealed class EstateMapModel
    {
        // Design #360 section 5: three zoom levels [가설] — the whole estate, a neighbourhood (default) and one building.
        // Zero columns means "fit the whole map"; the other levels show that many grid columns across the viewport.
        public static readonly int[] ZoomColumns = { 0, 8, 4 };
        public const int WholeMap = 0, Neighbourhood = 1, BuildingDetail = 2;
        // A pinch steps one level once the finger distance has grown or shrunk by this ratio since the last step.
        public const float PinchStepRatio = 1.35f;

        public int Columns { get; }
        public int Rows { get; }
        public Vector2 Viewport { get; private set; }
        public int Zoom { get; private set; } = Neighbourhood;
        // Map position (canvas units at the current zoom) shown at the viewport's top-left corner.
        public Vector2 Offset { get; private set; }

        public EstateMapModel(int columns, int rows)
        {
            Columns = Mathf.Max(1, columns); Rows = Mathf.Max(1, rows);
        }

        public float Cell => CellAt(Zoom);
        public Vector2 MapSize => new Vector2(Columns, Rows) * Cell;

        public float CellAt(int zoom)
        {
            if (Viewport.x <= 0 || Viewport.y <= 0) return 1;
            var columns = ZoomColumns[Mathf.Clamp(zoom, 0, ZoomColumns.Length - 1)];
            return columns == 0 ? Mathf.Min(Viewport.x / Columns, Viewport.y / Rows) : Viewport.x / columns;
        }

        public void SetViewport(Vector2 size)
        {
            if (size == Viewport) return;
            var first = Viewport.x <= 0 || Viewport.y <= 0;
            var centre = first ? Vector2.zero : ViewportToGrid(Viewport * .5f);
            Viewport = size;
            if (first) Clamp(); else CentreOn(centre);
        }

        // Keeps the grid point under `focus` (viewport units) where it is while the zoom changes.
        public bool SetZoom(int zoom, Vector2 focus)
        {
            zoom = Mathf.Clamp(zoom, 0, ZoomColumns.Length - 1);
            if (zoom == Zoom) return false;
            var grid = ViewportToGrid(focus);
            Zoom = zoom; Offset = grid * Cell - focus; Clamp();
            return true;
        }

        public bool StepZoom(int direction, Vector2 focus) => SetZoom(Zoom + (direction > 0 ? 1 : direction < 0 ? -1 : 0), focus);

        // Finger distance ratio since the pinch started (or since its last step) to a zoom direction.
        public static int PinchDirection(float ratio) => ratio >= PinchStepRatio ? 1 : ratio <= 1 / PinchStepRatio ? -1 : 0;

        // A drag moves the map with the finger, so the offset moves the other way.
        public void Pan(Vector2 delta) { Offset -= delta; Clamp(); }

        // False until the page first centres the map (on the home building); later visits keep the player's view.
        public bool Framed { get; private set; }
        public void CentreOn(Vector2 grid) { Offset = grid * Cell - Viewport * .5f; Clamp(); Framed = true; }

        public Vector2 GridToViewport(Vector2 grid) => grid * Cell - Offset;
        public Vector2 ViewportToGrid(Vector2 point) => (point + Offset) / Cell;

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

        // A map smaller than the viewport is centred; a larger one cannot be dragged past its edges.
        void Clamp()
        {
            var size = MapSize;
            Offset = new Vector2(Axis(Offset.x, size.x, Viewport.x), Axis(Offset.y, size.y, Viewport.y));
        }

        static float Axis(float offset, float map, float viewport) => map <= viewport ? (map - viewport) * .5f : Mathf.Clamp(offset, 0, map - viewport);
    }
}
