using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // Samples each approved large patch across the AOI, not one shrunk patch per square.
    // Only confirmed server cells enter the mesh. No terrain is synthesized beyond the received chunk.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorldMapTerrainGraphic : MaskableGraphic
    {
        public EstateMapModel Model;
        public WorldMapChunk Chunk;
        public Sprite Patch;
        public string Terrain;
        public override Texture mainTexture => Patch == null ? base.mainTexture : Patch.texture;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Model == null || Chunk == null || Patch == null) return;
            var r = Patch.textureRect; var texture = Patch.texture;
            var uvMin = new Vector2(r.x / texture.width, r.y / texture.height);
            var uvSize = new Vector2(r.width / texture.width, r.height / texture.height);
            foreach (var tile in Chunk.Tiles)
            {
                if (Terrain != null && tile.Terrain != Terrain) continue;
                var x = tile.X - Chunk.X * Chunk.Size; var y = tile.Y - Chunk.Y * Chunk.Size;
                if (!WorldMapCameraState.Visible(Model, new Vector2(x + .5f, y + .5f), 96 * Model.Scale)) continue;
                var start = mesh.currentVertCount;
                Add(x, y); Add(x + 1, y); Add(x + 1, y + 1); Add(x, y + 1);
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
            void Add(float x, float y)
            {
                var point = Model.GridToViewport(new Vector2(x, y));
                var uv = uvMin + Vector2.Scale(new Vector2(x / Chunk.Size, 1 - y / Chunk.Size), uvSize);
                mesh.AddVert(new Vector3(point.x, -point.y), color, uv);
            }
        }
    }
}
