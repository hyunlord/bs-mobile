#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Game.App;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public sealed partial class EstateMapFlowTests
    {
        [UnityTest, Timeout(180000)]
        public IEnumerator FreshR7LayerCapturesKeepTheSameSceneAndCamera()
        {
            PlayerPrefs.SetInt(prefix + "estatePrototype", 1); PlayerPrefs.Save();
            yield return Boot(); Click("Title.Enter"); yield return ScreenIs("EstateMap");
            Assert.That(app.EstateMap.Config.Version, Is.EqualTo("m3a-prototype-r7"));
            Assert.That(app.EstateMap.FromSave, Is.False);
            Assert.That(Screen.width, Is.EqualTo(390));
            Assert.That(Screen.height, Is.EqualTo(844));
            var view = View();
            var graphics = view.GetComponentsInChildren<Graphic>(true);
            var enabled = graphics.Select(g => g.enabled).ToArray();
            var zoom = view.Model.Zoom;
            var centre = view.Model.ViewportToGrid(view.Model.VisibleCentre);
            var csv = new StringBuilder("index,name,parent,material,texture,stage,initialEnabled,activeInHierarchy\n");
            for (var i = 0; i < graphics.Length; i++)
            {
                var g = graphics[i];
                csv.AppendLine($"{i},{LayerQuote(g.name)},{LayerQuote(g.transform.parent?.name)},{LayerQuote(g.materialForRendering?.name)},{LayerQuote(g.mainTexture?.name)},{LayerStage(g, view.transform)},{enabled[i]},{g.gameObject.activeInHierarchy}");
            }
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../artifacts/portrait-234/editor-meta"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "estate-r7-layer-membership.csv"), csv.ToString());
            // Capture the untouched first entry before disabling anything. UI outside the map and viewport masking stay intact.
            yield return Capture("estate-r7-layers-reference");
            ExportLayerGeometry(graphics, output);
            var labels = new[] { "ground", "night-light", "roads", "sites-decor", "final" };
            try
            {
                for (var stage = 0; stage < labels.Length; stage++)
                {
                    for (var i = 0; i < graphics.Length; i++)
                        graphics[i].enabled = enabled[i] && LayerStage(graphics[i], view.transform) <= stage;
                    Canvas.ForceUpdateCanvases();
                    yield return Capture("estate-r7-layers-" + stage + "-" + labels[stage]);
                    Assert.That(View(), Is.SameAs(view), "Layer isolation must not rebuild the map.");
                    Assert.That(view.GetComponentsInChildren<Graphic>(true), Is.EqualTo(graphics), "Graphic instances and hierarchy order must remain unchanged.");
                    Assert.That(view.Model.Zoom, Is.EqualTo(zoom));
                    Assert.That(view.Model.ViewportToGrid(view.Model.VisibleCentre), Is.EqualTo(centre));
                    if (stage == 2) MeasureRoadCrossSections(graphics, output);
                }
            }
            finally
            {
                for (var i = 0; i < graphics.Length; i++)
                    if (graphics[i] != null) graphics[i].enabled = enabled[i];
                Canvas.ForceUpdateCanvases();
            }
        }

        Camera LayerCamera(Graphic graphic) => graphic.canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : graphic.canvas.worldCamera;
        Vector2 LayerScreen(Graphic graphic, Vector3 local) => RectTransformUtility.WorldToScreenPoint(LayerCamera(graphic), graphic.rectTransform.TransformPoint(local));
        static string LayerNumber(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        void ExportLayerGeometry(Graphic[] graphics, string output)
        {
            var geometry = new StringBuilder("index,name,cull,vertices,rectWorldXMin,rectWorldYMin,rectWorldXMax,rectWorldYMax,rectScreenXMin,rectScreenYMin,rectScreenXMax,rectScreenYMax,meshScreenXMin,meshScreenYMin,meshScreenXMax,meshScreenYMax,rectCentreScreenX,rectCentreScreenY\n");
            var roads = new StringBuilder("index,sample,gridX,gridY,screenX,screenY,normalX,normalY,nominalWidthPx,meshWidthPx\n");
            {
                for (var index = 0; index < graphics.Length; index++)
                {
                    var g = graphics[index];
                    if (!(g is EstateTerrainImage terrain) || !(terrain.IsometricGround || terrain.IsometricRoad || terrain.RoadPoints != null)) continue;
                    var mesh = g.canvasRenderer.GetMesh();
                    if (mesh == null)
                        Assert.That(g.canvasRenderer.cull || !g.isActiveAndEnabled, Is.True, "Visible rendered geometry is required for " + g.name);
                    var corners = new Vector3[4]; g.rectTransform.GetWorldCorners(corners);
                    var rectPixels = corners.Select(p => RectTransformUtility.WorldToScreenPoint(LayerCamera(g), p)).ToArray();
                    var vertices = mesh == null ? Array.Empty<Vector3>() : mesh.vertices;
                    var meshPixels = vertices.Select(p => LayerScreen(g, p)).ToArray();
                    var centre = LayerScreen(g, g.rectTransform.rect.center);
                    geometry.AppendLine(string.Join(",", index, LayerQuote(g.name), g.canvasRenderer.cull, vertices.Length,
                        LayerNumber(corners.Min(p => p.x)), LayerNumber(corners.Min(p => p.y)), LayerNumber(corners.Max(p => p.x)), LayerNumber(corners.Max(p => p.y)),
                        LayerNumber(rectPixels.Min(p => p.x)), LayerNumber(rectPixels.Min(p => p.y)), LayerNumber(rectPixels.Max(p => p.x)), LayerNumber(rectPixels.Max(p => p.y)),
                        meshPixels.Length == 0 ? "NA" : LayerNumber(meshPixels.Min(p => p.x)), meshPixels.Length == 0 ? "NA" : LayerNumber(meshPixels.Min(p => p.y)),
                        meshPixels.Length == 0 ? "NA" : LayerNumber(meshPixels.Max(p => p.x)), meshPixels.Length == 0 ? "NA" : LayerNumber(meshPixels.Max(p => p.y)), LayerNumber(centre.x), LayerNumber(centre.y)));
                    if (terrain.RoadPoints == null || mesh == null) continue;
                    var curve = EstateTerrainImage.RoundedRoad(terrain.RoadPoints);
                    Assert.That(vertices.Length, Is.EqualTo(curve.Count * 9), "Road diagnostic expects the current nine-lane ribbon mesh.");
                    for (var sample = 0; sample < curve.Count; sample++)
                    {
                        var pixel = meshPixels[sample * 9 + 4];
                        var tangent = (meshPixels[Math.Min(curve.Count - 1, sample + 1) * 9 + 4] - meshPixels[Math.Max(0, sample - 1) * 9 + 4]).normalized;
                        var normal = new Vector2(-tangent.y, tangent.x);
                        var localNormal = (vertices[sample * 9 + 8] - vertices[sample * 9]).normalized;
                        var halfWidth = terrain.RoadWidth * terrain.rectTransform.rect.width / terrain.PaintedWidth * .5f;
                        var nominal = Vector2.Distance(LayerScreen(g, vertices[sample * 9 + 4] - localNormal * halfWidth), LayerScreen(g, vertices[sample * 9 + 4] + localNormal * halfWidth));
                        roads.AppendLine(string.Join(",", index, sample, LayerNumber(curve[sample].x), LayerNumber(curve[sample].y), LayerNumber(pixel.x), LayerNumber(pixel.y),
                            LayerNumber(normal.x), LayerNumber(normal.y), LayerNumber(nominal), LayerNumber(Vector2.Distance(meshPixels[sample * 9], meshPixels[sample * 9 + 8]))));
                    }
                }
            }
            File.WriteAllText(Path.Combine(output, "estate-r7-layer-geometry.csv"), geometry.ToString());
            File.WriteAllText(Path.Combine(output, "estate-r7-road-centrelines.csv"), roads.ToString());
            var model = View().Model;
            var frame = new StringBuilder("kind,id,gridX,gridY,screenX,screenY,scale,zoom\n");
            var viewport = (RectTransform)View().transform;
            var viewportGraphic = View().GetComponent<Graphic>();
            void Point(string kind, string id, Vector2 grid)
            {
                var vp = model.GridToViewport(grid);
                var pixel = LayerScreen(viewportGraphic, new Vector2(viewport.rect.xMin + vp.x, viewport.rect.yMax - vp.y));
                frame.AppendLine(string.Join(",", kind, LayerQuote(id), LayerNumber(grid.x), LayerNumber(grid.y), LayerNumber(pixel.x), LayerNumber(pixel.y), LayerNumber(model.Scale), model.Zoom));
            }
            Point("centre", "camera", model.ViewportToGrid(model.VisibleCentre));
            foreach (var zone in app.EstateMap.Config.Zones.Where(app.EstateMap.Open))
                foreach (var corner in new[] { new Vector2(zone.X, zone.Y), new Vector2(zone.X + zone.Width, zone.Y), new Vector2(zone.X + zone.Width, zone.Y + zone.Height), new Vector2(zone.X, zone.Y + zone.Height) }) Point("open-corner", zone.Id, corner);
            File.WriteAllText(Path.Combine(output, "estate-r7-layer-frame.csv"), frame.ToString());
        }

        void MeasureRoadCrossSections(Graphic[] graphics, string output)
        {
            var before = new Texture2D(2, 2); var after = new Texture2D(2, 2);
            var rows = new StringBuilder("index,sample,screenX,screenY,thresholdRgb,changedSamples,spanPx,centreDelta,maxDelta\n");
            try
            {
                Assert.That(before.LoadImage(File.ReadAllBytes(Path.Combine(output, "estate-r7-layers-1-night-light-390x844.png"))), Is.True);
                Assert.That(after.LoadImage(File.ReadAllBytes(Path.Combine(output, "estate-r7-layers-2-roads-390x844.png"))), Is.True);
                Assert.That(after.width, Is.EqualTo(before.width)); Assert.That(after.height, Is.EqualTo(before.height));
                var a = before.GetPixels32(); var b = after.GetPixels32();
                for (var index = 0; index < graphics.Length; index++)
                {
                    if (!(graphics[index] is EstateTerrainImage road) || road.RoadPoints == null) continue;
                    var mesh = road.canvasRenderer.GetMesh();
                    if (mesh == null && road.canvasRenderer.cull) continue;
                    Assert.That(mesh, Is.Not.Null, "Rendered road geometry is required.");
                    var pixels = mesh.vertices.Select(p => LayerScreen(road, p)).ToArray(); var count = pixels.Length / 9;
                    for (var sample = 1; sample < count - 1; sample++)
                    {
                        var centre = pixels[sample * 9 + 4]; var tangent = (pixels[(sample + 1) * 9 + 4] - pixels[(sample - 1) * 9 + 4]).normalized;
                        var normal = new Vector2(-tangent.y, tangent.x); var changed = 0; var lo = 41; var hi = -41; var centreDelta = 0; var maxDelta = 0;
                        for (var offset = -40; offset <= 40; offset++)
                        {
                            var p = centre + normal * offset; var x = Mathf.RoundToInt(p.x); var y = Mathf.RoundToInt(p.y);
                            if (x < 0 || y < 0 || x >= before.width || y >= before.height) continue;
                            var at = y * before.width + x;
                            var delta = Math.Max(Math.Abs(a[at].r - b[at].r), Math.Max(Math.Abs(a[at].g - b[at].g), Math.Abs(a[at].b - b[at].b)));
                            maxDelta = Math.Max(maxDelta, delta); if (offset == 0) centreDelta = delta;
                            if (delta < 12) continue;
                            changed++; lo = Math.Min(lo, offset); hi = Math.Max(hi, offset);
                        }
                        rows.AppendLine(string.Join(",", index, sample, LayerNumber(centre.x), LayerNumber(centre.y), 12, changed, changed == 0 ? 0 : hi - lo + 1, centreDelta, maxDelta));
                    }
                }
                File.WriteAllText(Path.Combine(output, "estate-r7-road-crosssections.csv"), rows.ToString());
            }
            finally { UnityEngine.Object.Destroy(before); UnityEngine.Object.Destroy(after); }
        }

        static int LayerStage(Graphic graphic, Transform viewport)
        {
            var name = graphic.name;
            if (graphic.transform == viewport || name == "EstateMap.Ground" || graphic is EstateTerrainImage ground && ground.IsometricGround || name == "EstateMap.Decor:soil") return 0;
            if (name == "EstateMap.Night" || name == "EstateMap.Lamps") return 1;
            if (name == "EstateMap.Road") return 2;
            if (name == "EstateMap.Terrain" || name.StartsWith("EstateMap.Site", StringComparison.Ordinal) || name.StartsWith("EstateMap.Decor:", StringComparison.Ordinal) ||
                name.StartsWith("EstateMap.Veiled:", StringComparison.Ordinal) || name.StartsWith("EstateMap.Landmark:", StringComparison.Ordinal) ||
                name.StartsWith("EstateMap.Piece:", StringComparison.Ordinal)) return 3;
            return 4;
        }

        static string LayerQuote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
#endif
