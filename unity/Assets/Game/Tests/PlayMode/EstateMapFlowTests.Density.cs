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
            public string name, role, source, texture, filter, shader, unsupported;
            public int order; public bool active;
            public string[] clipMasks; public bool clipValid;
            public float inheritedAlpha; public float[] clip, rect, spriteRect, pivot, tint, uvRect;
            public DensityVertex[] vertices; public int[] triangles;
        }
        [Serializable] sealed class DensityExport
        {
            public int width, height; public string camera, geometryProvenance;
            public float[] clip; public DensityPolygon[] land, water;
            public DensityItem[] items; public string[] errors;
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
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(true).OrderBy(g => g.canvasRenderer.absoluteDepth))
            {
                if (graphic.transform == view.transform) continue;
                var tag = graphic.GetComponent<EstateDensityTag>();
                var image = graphic as Image; var raw = graphic as RawImage;
                var entry = new DensityItem { name = graphic.name, role = tag?.Role ?? "", source = tag?.Source ?? "",
                    order = graphic.canvasRenderer.absoluteDepth, active = graphic.isActiveAndEnabled && !graphic.canvasRenderer.cull,
                    inheritedAlpha = graphic.canvasRenderer.GetInheritedAlpha(), clip = clip, rect = Bounds(graphic.rectTransform),
                    tint = new[] {graphic.color.r,graphic.color.g,graphic.color.b,graphic.color.a}, shader = graphic.materialForRendering?.shader?.name };
                if (entry.name == "EstateMap.Night") entry.source = "night";
                else if (entry.name == "EstateMap.Lamps") entry.source = "illumination";
                else if (entry.name == "EstateMap.Ground") entry.source = "terrain";
                if (entry.source == "" && graphic.GetComponentInParent<EstateCarryView>() != null) entry.source = "peasant";
                if (!entry.active || entry.source == "night" || entry.source == "illumination" || entry.source == "terrain" || entry.source == "soil" || entry.source == "yard-ground") { items.Add(entry); continue; }
                var texture = graphic.mainTexture as Texture2D;
                if (image == null && raw == null) entry.unsupported = "Non Image/RawImage graphic needs explicit category and mesh contract";
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
                var mesh = graphic.canvasRenderer.GetMesh();
                if (mesh == null) entry.unsupported = "CanvasRenderer has no current mesh";
                else
                {
                    var positions = mesh.vertices; var uv = mesh.uv; var colours = mesh.colors32;
                    if (uv.Length != positions.Length || colours.Length != positions.Length) entry.unsupported = "Mesh attributes incomplete";
                    else entry.vertices = positions.Select((p,i) => {
                        var screen = ScreenPoint(graphic.rectTransform.TransformPoint(p));
                        return new DensityVertex {x=screen.x,y=screen.y,u=uv[i].x,v=uv[i].y,a=colours[i].a/255f}; }).ToArray();
                    entry.triangles = mesh.triangles;
                }
                items.Add(entry);
            }
            var document = new DensityExport { width = Screen.width, height = Screen.height, clip = clip,
                camera = $"zoom={map.Zoom};scale={map.Scale};centre={map.ViewportToGrid(map.VisibleCentre)};topInset={map.TopInset};bottomInset={map.BottomInset};frame={Time.frameCount}",
                geometryProvenance = "Runtime config-zone polygons and EstateR8Layout riverCells; compare against DENSE-SCENES-r2 before asserting reference parity. Full map viewport, bars excluded from art composition.",
                land = app.EstateMap.Config.Zones.Select(z => Polygon(z.X,z.Y,z.Width,z.Height)).ToArray(),
                water = EstateR8Layout.Load().riverCells.Select(c => Polygon(c.x,c.y,1,1)).ToArray(),
                items = items.ToArray(), errors = errors.ToArray() };
            File.WriteAllText(path + ".density.json", JsonUtility.ToJson(document, true));
        }
    }
}
#endif
