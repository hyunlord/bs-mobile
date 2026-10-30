#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.App;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public sealed partial class EstateMapFlowTests
    {
        [Serializable] sealed class DensityPoint { public float x, y; }
        [Serializable] sealed class DensityPolygon { public DensityPoint[] points; }
        [Serializable] sealed class DensityVertex { public float x, y, u, v, a; }
        [Serializable] sealed class DensityItem
        {
            public string name, role, source, category, texture, filter, shader, unsupported, instanceId, authoredCategory;
            public int order, graphicDepth, authoredDrawOrder; public bool active;
            public string[] clipMasks; public bool clipValid;
            public float inheritedAlpha; public float[] clip, rect, spriteRect, pivot, tint, uvRect;
            public DensityVertex[] vertices; public int[] triangles;
        }
        [Serializable] sealed class DensityExport
        {
            public int width, height; public string camera, geometryProvenance;
            public float[] clip; public DensityPolygon[] land, water;
            public DensityItem[] items; public string[] errors;
            public int authoredGraphics, authoredEmittedInstances, authoredCapturedQuads;
            public bool authoredMeshMappingValid; public string waterDenominatorContract;
        }
        static bool AuthoredQuadIndicesMatch(int vertexCount, int[] indices, int instances)
        {
            if (vertexCount != instances * 4 || indices == null || indices.Length != instances * 6) return false;
            var local = new[] { 0, 1, 2, 0, 2, 3 };
            for (var q = 0; q < instances; q++)
                for (var n = 0; n < local.Length; n++)
                    if (indices[q * 6 + n] != q * 4 + local[n]) return false;
            return true;
        }
        [NUnit.Framework.Test]
        public void AuthoredDensityMappingRequiresCompleteOrderedQuadIndices()
        {
            var two = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            NUnit.Framework.Assert.That(AuthoredQuadIndicesMatch(8, two, 2), NUnit.Framework.Is.True);
            NUnit.Framework.Assert.That(AuthoredQuadIndicesMatch(8, two, 1), NUnit.Framework.Is.False, "Stale emitted-instance mapping must not be guessed.");
            NUnit.Framework.Assert.That(AuthoredQuadIndicesMatch(7, two, 2), NUnit.Framework.Is.False);
            two[6] = 0;
            NUnit.Framework.Assert.That(AuthoredQuadIndicesMatch(8, two, 2), NUnit.Framework.Is.False, "The second quad must not reference the first quad's vertices.");
        }
        void ExportDensity(string path)
        {
            var view = View(); var map = view.Model; var rect = (RectTransform)view.transform;
            var renderCamera = app.Ui.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : app.Ui.Canvas.worldCamera;
            Vector2 ScreenPoint(Vector3 world)
            {
                var p = RectTransformUtility.WorldToScreenPoint(renderCamera, world);
                return new Vector2(p.x, Screen.height - p.y);
            }
            Vector2 GridPoint(Vector2 grid)
            {
                var p = map.GridToViewport(grid);
                return ScreenPoint(rect.TransformPoint(new Vector3(rect.rect.xMin + p.x, rect.rect.yMax - p.y, 0)));
            }
            float[] Bounds(RectTransform transform)
            {
                var corners = new Vector3[4]; transform.GetWorldCorners(corners);
                var p = corners.Select(ScreenPoint).ToArray();
                return new[] { p.Min(v => v.x), p.Min(v => v.y), p.Max(v => v.x), p.Max(v => v.y) };
            }
            void ExportClip(Graphic graphic, DensityItem entry)
            {
                entry.clip = new[] { 0f, 0f, (float)Screen.width, (float)Screen.height };
                entry.clipValid = true; entry.clipMasks = Array.Empty<string>();
                var maskable = graphic as MaskableGraphic;
                if (maskable == null) { entry.unsupported = "Graphic has no MaskableGraphic clipping contract"; return; }
                if (!maskable.maskable) return;
                if (graphic.GetComponentsInParent<Mask>().Any(m => m.isActiveAndEnabled))
                { entry.unsupported = "Stencil Mask alpha requires explicit raster contract"; return; }
                var owner = MaskUtilities.GetRectMaskForClippable(maskable);
                if (owner == null) return;
                var masks = new List<RectMask2D>(); MaskUtilities.GetRectMasksForClip(owner, masks);
                entry.clipMasks = masks.Select(m => $"{m.name};canvas={m.canvasRect};padding={m.padding};softness={m.softness}").ToArray();
                if (masks.Any(m => m.softness != Vector2Int.zero))
                { entry.unsupported = "RectMask2D softness requires fractional clip alpha"; return; }
                var canvases = owner.GetComponentsInParent<Canvas>();
                if (canvases.Length == 0) { entry.unsupported = "RectMask2D has no canvas"; return; }
                // RectMask2D.Canvas uses the outermost Canvas; canvasRect and padding are in that canvas space.
                var canvas = canvases[canvases.Length - 1];
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                { entry.unsupported = "Non-overlay RectMask2D projection needs a separate contract"; return; }
                bool AxisAligned(RectTransform transform)
                {
                    var corners = new Vector3[4]; transform.GetWorldCorners(corners);
                    var p = corners.Select(ScreenPoint).ToArray();
                    return Mathf.Abs(p[0].x-p[1].x)<.001f && Mathf.Abs(p[1].y-p[2].y)<.001f &&
                        Mathf.Abs(p[2].x-p[3].x)<.001f && Mathf.Abs(p[3].y-p[0].y)<.001f;
                }
                if (!AxisAligned((RectTransform)canvas.transform) || masks.Any(m => !AxisAligned(m.rectTransform)))
                { entry.unsupported = "Rotated RectMask2D or canvas requires explicit clip projection"; return; }
                var combined = Clipping.FindCullAndClipWorldRect(masks, out var valid);
                entry.clipValid = valid;
                if (!valid) { entry.clip = new float[4]; return; }
                var a = ScreenPoint(canvas.transform.TransformPoint(new Vector3(combined.xMin,combined.yMin,0)));
                var b = ScreenPoint(canvas.transform.TransformPoint(new Vector3(combined.xMax,combined.yMax,0)));
                entry.clip = new[] { Mathf.Max(0,Mathf.Min(a.x,b.x)), Mathf.Max(0,Mathf.Min(a.y,b.y)),
                    Mathf.Min(Screen.width,Mathf.Max(a.x,b.x)), Mathf.Min(Screen.height,Mathf.Max(a.y,b.y)) };
                entry.clipValid = entry.clip[2]>entry.clip[0] && entry.clip[3]>entry.clip[1];
            }
            DensityPolygon Polygon(float x, float y, float w, float h) => new DensityPolygon {
                points = new[] { new Vector2(x,y), new Vector2(x+w,y), new Vector2(x+w,y+h), new Vector2(x,y+h) }
                    .Select(GridPoint).Select(p => new DensityPoint { x = p.x, y = p.y }).ToArray() };
            var clip = Bounds(rect); var items = new List<DensityItem>(); var errors = new List<string>();
            int authoredGraphics = 0, authoredEmittedInstances = 0, authoredCapturedQuads = 0;
            bool authoredMeshMappingValid = true;
            string AuthoredCategory(string value)
            {
                switch (value)
                {
                    case "ground-decal": return "ground";
                    case "river": return "water";
                    case "building-prop": case "bridge": return "prop";
                    case "prop": case "cluster": case "path": case "exterior": return value;
                    default: return null;
                }
            }
            DensityVertex[] Vertices(Graphic graphic, Vector3[] positions, Vector2[] uv, Color32[] colours, int start, int count)
            {
                var result = new DensityVertex[count];
                for (var i = 0; i < count; i++)
                {
                    var n = start + i;
                    var screen = ScreenPoint(graphic.rectTransform.TransformPoint(positions[n]));
                    result[i] = new DensityVertex { x = screen.x, y = screen.y, u = uv[n].x, v = uv[n].y, a = colours[n].a / 255f };
                }
                return result;
            }
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(true).OrderBy(g => g.canvasRenderer.absoluteDepth))
            {
                if (graphic.transform == view.transform) continue;
                var tag = graphic.GetComponent<EstateDensityTag>();
                var image = graphic as Image; var raw = graphic as RawImage; var authored = graphic as EstateAuthoredTerrainGraphic;
                var entry = new DensityItem { name = graphic.name, role = tag?.Role ?? "", source = tag?.Source ?? "",
                    order = items.Count, graphicDepth = graphic.canvasRenderer.absoluteDepth, active = graphic.isActiveAndEnabled && !graphic.canvasRenderer.cull,
                    inheritedAlpha = graphic.canvasRenderer.GetInheritedAlpha(), clip = clip, rect = Bounds(graphic.rectTransform),
                    tint = new[] {graphic.color.r,graphic.color.g,graphic.color.b,graphic.color.a}, shader = graphic.materialForRendering?.shader?.name };
                if (authored != null && authored.Ground) entry.source = "terrain";
                if (entry.name == "EstateMap.Night") entry.source = "night";
                else if (entry.name == "EstateMap.Lamps") entry.source = "illumination";
                else if (entry.name == "EstateMap.Ground") entry.source = "terrain";
                if (entry.source == "" && graphic.GetComponentInParent<EstateCarryView>() != null) entry.source = "peasant";
                if (!entry.active || entry.source == "night" || entry.source == "illumination" || entry.source == "terrain" || entry.source == "soil" || entry.source == "yard-ground") { items.Add(entry); continue; }
                var texture = graphic.mainTexture as Texture2D;
                if (image == null && raw == null && authored == null) entry.unsupported = "Non Image/RawImage graphic needs explicit category and mesh contract";
                if (texture == null) entry.unsupported = "Missing Texture2D";
                else
                {
                    entry.texture = AssetDatabase.GetAssetPath(texture); entry.filter = texture.filterMode.ToString();
                    if (string.IsNullOrEmpty(entry.texture) || !entry.texture.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) entry.unsupported = "Texture is not a local source PNG";
                    if (texture.mipmapCount > 1) entry.unsupported = "Mipmapped texture needs level/derivative sampling";
                    if (texture.wrapMode != TextureWrapMode.Clamp) entry.unsupported = "Non-clamp sampler needs explicit wrap semantics";
                }
                if (graphic.GetComponentsInParent<CanvasGroup>().Any(g => !g.enabled)) entry.unsupported = "Disabled CanvasGroup semantics not supported";
                ExportClip(graphic, entry);
                if (graphic.materialForRendering != null && entry.shader != "UI/Default" && !entry.shader.EndsWith("EstateTint", StringComparison.Ordinal)) entry.unsupported = "Unmodelled alpha shader: " + entry.shader;
                if (graphic is EstateTerrainImage terrain && terrain.WaterOnly) entry.unsupported = "WaterOnly shader modifies alpha by sampled RGB; needs color-space-aware sampler";
                if (image?.sprite != null)
                {
                    var sr = image.sprite.rect;
                    entry.spriteRect = new[] {sr.x,sr.y,sr.width,sr.height}; entry.pivot = new[] {image.sprite.pivot.x,image.sprite.pivot.y};
                }
                if (raw != null) entry.uvRect = new[] {raw.uvRect.x,raw.uvRect.y,raw.uvRect.width,raw.uvRect.height};
                if (authored != null)
                {
                    authoredGraphics++; authoredEmittedInstances += authored.RenderedInstances.Count;
                }
                var mesh = graphic.canvasRenderer.GetMesh();
                if (mesh == null)
                {
                    entry.unsupported = "CanvasRenderer has no current mesh";
                    if (authored != null)
                    {
                        authoredMeshMappingValid = false; errors.Add(graphic.name + ": " + entry.unsupported);
                    }
                }
                else
                {
                    var positions = mesh.vertices; var uv = mesh.uv; var colours = mesh.colors32;
                    if (uv.Length != positions.Length || colours.Length != positions.Length) entry.unsupported = "Mesh attributes incomplete";
                    else entry.vertices = Vertices(graphic, positions, uv, colours, 0, positions.Length);
                    entry.triangles = mesh.triangles;
                    if (authored != null)
                    {
                        var emitted = authored.RenderedInstances;
                        var mappingValid = texture != null && entry.vertices != null &&
                            AuthoredQuadIndicesMatch(positions.Length, entry.triangles, emitted.Count) &&
                            emitted.Select(i => i.id).Distinct().Count() == emitted.Count;
                        if (!mappingValid)
                        {
                            authoredMeshMappingValid = false;
                            entry.unsupported = $"Authored emitted-instance/mesh mismatch: instances={emitted.Count}, vertices={positions.Length}, indices={entry.triangles.Length}";
                            errors.Add(graphic.name + ": " + entry.unsupported);
                        }
                        else
                        {
                            var assets = authored.Layout.assets.ToDictionary(a => a.id, StringComparer.Ordinal);
                            for (var q = 0; q < emitted.Count; q++)
                            {
                                var instance = emitted[q]; var asset = assets[instance.assetId];
                                var vertices = Vertices(graphic, positions, uv, colours, q * 4, 4);
                                var category = AuthoredCategory(instance.category);
                                var piece = new DensityItem {
                                    name = graphic.name + ":" + instance.id, role = instance.assetId, source = "authored-" + instance.category,
                                    category = category, instanceId = instance.id, authoredCategory = instance.category,
                                    order = items.Count, graphicDepth = entry.graphicDepth, authoredDrawOrder = instance.drawOrder,
                                    active = entry.active, inheritedAlpha = entry.inheritedAlpha, tint = entry.tint,
                                    shader = entry.shader, texture = entry.texture, filter = entry.filter,
                                    clip = entry.clip, clipMasks = entry.clipMasks, clipValid = entry.clipValid,
                                    rect = new[] { vertices.Min(v => v.x), vertices.Min(v => v.y), vertices.Max(v => v.x), vertices.Max(v => v.y) },
                                    spriteRect = new[] { (float)asset.rect[0], texture.height - asset.rect[1] - asset.rect[3], asset.rect[2], asset.rect[3] },
                                    pivot = new[] { asset.anchor[0], asset.size[1] - asset.anchor[1] },
                                    vertices = vertices, triangles = entry.triangles.Skip(q * 6).Take(6).Select(index => index - q * 4).ToArray(),
                                    unsupported = category == null ? "Unmapped authored category: " + instance.category : entry.unsupported
                                };
                                if (piece.unsupported != null) errors.Add(piece.name + ": " + piece.unsupported);
                                items.Add(piece); authoredCapturedQuads++;
                            }
                            continue;
                        }
                    }
                }
                items.Add(entry);
            }
            var document = new DensityExport { width = Screen.width, height = Screen.height, clip = clip,
                camera = $"zoom={map.Zoom};scale={map.Scale};centre={map.ViewportToGrid(map.VisibleCentre)};topInset={map.TopInset};bottomInset={map.BottomInset};frame={Time.frameCount}",
                geometryProvenance = "Actual CanvasRenderer mesh positions/UV/vertex alpha in absolute Graphic depth and emitted quad order; authored IDs are supplied by the same OnPopulateMesh call. Full map viewport, bars excluded. Logical river cells are provenance only, not the strict visual-water denominator.",
                authoredGraphics = authoredGraphics, authoredEmittedInstances = authoredEmittedInstances, authoredCapturedQuads = authoredCapturedQuads,
                authoredMeshMappingValid = authoredMeshMappingValid, waterDenominatorContract = "approved-reference-visual-water-alpha128; never substitute logical river cells",
                land = app.EstateMap.Config.Zones.Select(z => Polygon(z.X,z.Y,z.Width,z.Height)).ToArray(),
                water = EstateR8Layout.Load().riverCells.Select(c => Polygon(c.x,c.y,1,1)).ToArray(),
                items = items.ToArray(), errors = errors.ToArray() };
            File.WriteAllText(path + ".density.json", JsonUtility.ToJson(document, true));
        }
    }
}
#endif
