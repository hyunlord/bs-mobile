using UnityEngine;

namespace Game.App
{
    // Presentation-only view; dimensions and drawable cells always come from the observed AOI.
    public sealed class WorldMapCameraState
    {
        public EstateMapModel Model { get; private set; }
        public WorldMapChunk Chunk { get; private set; }
        int originX = -1, originY = -1;
        public Vector2Int? RequestedChunkMove { get; set; }

        public void Clear() { Model = null; Chunk = null; originX = originY = -1; RequestedChunkMove = null; }

        public EstateMapModel Observe(WorldMapChunk chunk, int viewX, int viewY, Vector2 viewport)
        {
            if (chunk == null) { Clear(); return null; }
            var changed = Chunk == null || Chunk.WorldId != chunk.WorldId || Chunk.ContentHash != chunk.ContentHash
                || Chunk.X != chunk.X || Chunk.Y != chunk.Y || Chunk.Size != chunk.Size;
            var zoom = Model?.Zoom ?? EstateMapModel.Neighbourhood;
            Chunk = chunk;
            if (changed) Model = new EstateMapModel(chunk.Size, chunk.Size);
            Model.SetViewport(viewport);
            if (changed || originX != viewX || originY != viewY)
            {
                var side = Mathf.Min(PortraitMetaPanels.WorldVisibleSide, chunk.Size);
                Model.Restore(zoom, new Vector2(viewX - chunk.X * chunk.Size + side * .5f, viewY - chunk.Y * chunk.Size + side * .5f));
            }
            originX = viewX; originY = viewY;
            return Model;
        }

        public static Vector2Int EdgeMove(EstateMapModel model, WorldMapChunk chunk, Vector2 dragDelta)
        {
            if (model == null || chunk == null || dragDelta.sqrMagnitude < 24 * 24) return Vector2Int.zero;
            var grid = EstateMapModel.Unproject(new Vector2(-dragDelta.x, dragDelta.y) / model.Scale);
            var centre = model.ViewportToGrid(model.VisibleCentre);
            var dx = grid.x > 0 && centre.x >= chunk.Size - 3 ? 1 : grid.x < 0 && centre.x <= 3 ? -1 : 0;
            var dy = grid.y > 0 && centre.y >= chunk.Size - 3 ? 1 : grid.y < 0 && centre.y <= 3 ? -1 : 0;
            if (Mathf.Abs(grid.x) >= Mathf.Abs(grid.y)) dy = dx == 0 ? dy : 0; else dx = dy == 0 ? dx : 0;
            if (chunk.X == 0 && dx < 0) dx = 0;
            if (chunk.Y == 0 && dy < 0) dy = 0;
            return new Vector2Int(dx, dy);
        }

        public static string TargetRole(WorldMapTarget target)
        {
            if (target == null) return null;
            // No tier exists in the response: use the neutral city silhouette without displaying a numeric level.
            if (target.Kind == "city") return "world-castle-tier1";
            if (target.Kind == "resource")
                return target.ContentId == "meta:grain" ? "world-resource-grain" : target.ContentId == "meta:timber" ? "world-resource-timber" : target.ContentId == "meta:iron" ? "world-resource-iron" : null;
            if (target.Kind == "monster")
                return target.ContentId == "slg:encounter_field_mites" ? "world-monster-field-mites" : target.ContentId == "slg:encounter_wood_wasps" ? "world-monster-wasps" : target.ContentId == "slg:encounter_hill_rubble" ? "world-monster-rubble-golems" : null;
            if (target.Kind == "stronghold")
                return target.ContentId == "slg:encounter_ruin_marker" ? "world-stronghold-ruins" : target.ContentId == "slg:encounter_riverside_watch" ? "world-stronghold-riverside" : null;
            return null;
        }

        public static bool Visible(EstateMapModel model, Vector2 local, float margin)
        {
            var p = model.GridToViewport(local);
            return p.x >= -margin && p.y >= -margin && p.x <= model.Viewport.x + margin && p.y <= model.Viewport.y + margin;
        }
    }
}
