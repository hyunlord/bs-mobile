using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    // One ordered atlas mesh: no sprite/Y regrouping, generated positions or per-prop GameObjects.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EstateAuthoredTerrainGraphic : MaskableGraphic
    {
        public EstateMapModel Model;
        public EstateAuthoredLayout.Document Layout;
        public bool Ground;
        readonly List<EstateAuthoredLayout.Instance> rendered = new List<EstateAuthoredLayout.Instance>();
        public IReadOnlyList<EstateAuthoredLayout.Instance> RenderedInstances => rendered;
        Texture2D atlas;
        Dictionary<string, EstateAuthoredLayout.Asset> assets;
        EstateAuthoredLayout.Instance[] ordered;
        public override Texture mainTexture => atlas;
        public void Configure(EstateAuthoredLayout.Document layout, EstateMapModel model, bool ground)
        {
            Layout = layout; Model = model; Ground = ground;
            atlas = Resources.Load<Texture2D>("EstateArt/estate-authored-terrain");
            if (atlas == null) throw new System.InvalidOperationException("Missing approved terrain atlas.");
            assets = layout.assets.ToDictionary(a => a.id, System.StringComparer.Ordinal);
            ordered = EstateAuthoredLayout.Ordered(layout);
            raycastTarget = false; SetVerticesDirty();
        }
        Vector2 Local(Vector2 native)
        {
            var p = Model.GridToViewport(EstateMapModel.Unproject(native)); var r = rectTransform.rect;
            return new Vector2(r.xMin + p.x, r.yMax - p.y);
        }
        void Quad(VertexHelper mesh, EstateAuthoredLayout.Asset asset, Vector2[] native, Vector2[] fractions, float alpha)
        {
            var points = native.Select(Local).ToArray(); var r = rectTransform.rect;
            if (points.All(p => p.x < r.xMin) || points.All(p => p.x > r.xMax) || points.All(p => p.y < r.yMin) || points.All(p => p.y > r.yMax)) return;
            var start = mesh.currentVertCount; var a = asset.rect; var colour = new Color(1, 1, 1, alpha);
            for (var i = 0; i < 4; i++)
                mesh.AddVert(points[i], colour, new Vector2((a[0] + fractions[i].x * a[2]) / atlas.width, 1 - (a[1] + fractions[i].y * a[3]) / atlas.height));
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        static Vector2[] Corners(float x, float y, float width, float height) => new[] { new Vector2(x,y), new Vector2(x+width,y), new Vector2(x+width,y+height), new Vector2(x,y+height) };
        void PaintGround(VertexHelper mesh)
        {
            var bg = Layout.background; var tile = bg.tilePixels;
            var a = EstateMapModel.Project(Model.ViewportToGrid(Vector2.zero));
            var b = EstateMapModel.Project(Model.ViewportToGrid(rectTransform.rect.size));
            var ox = bg.canvasOffset[0]; var oy = bg.canvasOffset[1];
            for (var x = Mathf.FloorToInt((a.x + ox) / tile) * tile - ox; x < b.x + tile; x += tile)
                for (var y = Mathf.FloorToInt((a.y + oy) / tile) * tile - oy; y < b.y + tile; y += tile)
                    Quad(mesh, assets[bg.assetId], Corners(x,y,tile,tile), Corners(0,0,1,1), 1);
            foreach (var z in Layout.zones)
            {
                var r = z.rect;
                for (var x = Mathf.FloorToInt(r[0] / 8) * 8; x < r[0] + r[2]; x += 8)
                    for (var y = Mathf.FloorToInt(r[1] / 8) * 8; y < r[1] + r[3]; y += 8)
                    {
                        var left = Mathf.Max(x,r[0]); var top = Mathf.Max(y,r[1]);
                        var width = Mathf.Min(x+8,r[0]+r[2])-left; var height = Mathf.Min(y+8,r[1]+r[3])-top;
                        Quad(mesh, assets[z.groundAssetId], Corners(left,top,width,height).Select(EstateMapModel.Project).ToArray(),
                            Corners((left-x)/8,(top-y)/8,width/8,height/8), 1);
                    }
            }
        }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); rendered.Clear(); if (Layout == null || atlas == null) return;
            if (Ground) { PaintGround(mesh); return; }
            foreach (var i in ordered)
            {
                var a = assets[i.assetId]; var uv = Corners(0,0,1,1);
                if (i.flipX) for (var k = 0; k < uv.Length; k++) uv[k].x = 1 - uv[k].x;
                var before = mesh.currentVertCount;
                Quad(mesh, a, EstateAuthoredLayout.Corners(a,i), uv, i.opacity);
                if (mesh.currentVertCount > before) rendered.Add(i);
            }
        }
    }
}
