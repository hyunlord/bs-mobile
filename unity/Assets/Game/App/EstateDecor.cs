using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // View-only estate dressing rules (Resources/EstateDecorRules.json, estate-decor-r1, #372/#375). Not under data/, so Core
    // content, saves and replay hashes are unchanged. Every number here is a presentation hypothesis [가설] kept in one place.
    [Serializable]
    public sealed class EstateDecorRules
    {
        public const string ResourceName = "EstateDecorRules", Version = "estate-decor-r1";
        public string version;
        public AstraDecorRules astra = new AstraDecorRules();
        public KindArt[] kindArt = Array.Empty<KindArt>();
        public Tint[] tints = Array.Empty<Tint>();
        public float buildingScale = 1;
        public float constructionSaturation = .55f, constructionAlpha = .85f;
        public YardGround yardGround = new YardGround();
        public Restoration restoration = new Restoration();
        public Terrain terrain = new Terrain();
        public Trees trees = new Trees();
        public Frontier frontier = new Frontier();
        public ClosedLand closedLand = new ClosedLand();
        public Night night = new Night();
        public Sites sites = new Sites();
        public Placed[] placed = Array.Empty<Placed>();
        public Peasants peasants = new Peasants();
        public string fieldFenceKind = "m3a:field", fieldFenceCorner = "field-fence-corner";

        // Core kind to Astra art kind; `stages[level]` is the art stage for that Core level (level 0 = ruin site).
        [Serializable] public sealed class KindArt { public string kind, art; public int[] stages = Array.Empty<int>(); }
        // Restoration colour [가설]: saturation, brightness, light (1 - M2 darkness).
        [Serializable] public sealed class Tint { public string state; public float saturation = 1, brightness = 1, light = 1; }
        [Serializable] public sealed class YardGround { public string well = "yard-stone", house = "yard-grass", other = "yard-earth"; public float offsetX = -.64f, offsetY = -.35f, width = 1.28f, height = .48f, alpha = .72f; }
        [Serializable] public sealed class Restoration { public int restoredRadius = 2, sageRadius = 5; }
        [Serializable] public sealed class Terrain { public int patchCells = 6; public float patchScale = 1.5f, alpha = .92f; public string restored = "terrain-restoration", sage = "terrain-sage", fallow = "terrain-fallow"; }
        [Serializable] public sealed class Trees { public string[] ids = Array.Empty<string>(); public float share; public string[] states = Array.Empty<string>(); }
        [Serializable] public sealed class Frontier { public string[] ids = Array.Empty<string>(); }
        // Land outside the open zones (closed zones and the margin around the estate) is painted like open land, cooler, and lies
        // under the night (key art C, m3-estate-keyarts-r2): terrain patches, rivers from `placed`, frontier traces, dead trees
        // and woods on a jittered lattice.
        [Serializable] public sealed class ClosedLand
        {
            public float sageShare = .3f;
            public int every = 4;
            public float jitter = .6f, density = .55f, deadTreeShare = .35f;
            public string deadTree = "frontier-dead-tree";
            public Wood[] woods = Array.Empty<Wood>();
        }
        // The night (#379): one continuous darkness by distance from lit (open) land, key art C's blue-grey. `margin` cells of
        // painted land surround the estate; the first view fits the open zones plus `openingMargin` cells; lamps are soft warm
        // light around built buildings in open land.
        [Serializable] public sealed class Night
        {
            public int[] colour = { 54, 61, 73 }, ground = { 78, 82, 86 }, lampColour = { 229, 201, 127 };
            public float edgeAlpha = .5f, fullAlpha = .75f, fullCells = 2.5f, insideCells = .5f, areaFadeCells = 3;
            public int texelsPerCell = 4, margin = 10, nightOverhang = 6;
            public float openingMargin = .75f, lampAlpha = .24f, homeLampCells = 3.2f, lampCells = 2.2f;
            public float landmarkBrightness = .4f;
            public Color Colour => new Color32((byte)colour[0], (byte)colour[1], (byte)colour[2], 255);
            public Color Ground => new Color32((byte)ground[0], (byte)ground[1], (byte)ground[2], 255);
            public Color Lamp => new Color32((byte)lampColour[0], (byte)lampColour[1], (byte)lampColour[2], (byte)Mathf.RoundToInt(lampAlpha * 255));
            // Darkness at signed distance `d` cells from the lit edge (negative inside lit land).
            public float Alpha(float d) => d <= -insideCells ? 0 : d <= 0 ? edgeAlpha * Mathf.SmoothStep(0, 1, (d + insideCells) / insideCells)
                : Mathf.Lerp(edgeAlpha, fullAlpha, Mathf.SmoothStep(0, 1, Mathf.Min(1, d / fullCells)));
        }
        // Unbuilt sites and roads stand on Astra's painted earth instead of flat diamonds (#379).
        [Serializable] public sealed class Sites { public string ground = "yard-earth"; public float siteWidth = 1.15f, roadWidth = 1.4f, aspect = .5f, siteAlpha = .9f, roadAlpha = .75f; }
        [Serializable] public sealed class Wood { public string zone; public string[] ids = Array.Empty<string>(); public float share = .7f; }
        // Explicit terrain-skeleton pieces (rivers, bridges, a paddock): grid point of the pivot, reserved cells, layer.
        [Serializable] public sealed class Placed { public string id, rail, zone; public float u, v, rotation; public int[] reserve = Array.Empty<int>(); public string with; public float withU, withV; }
        [Serializable] public sealed class Peasants { public string farm = "peasant-farm", carry = "peasant-carry", build = "peasant-build", carryKind = "meta:granary"; }

        static EstateDecorRules loaded;

        public static EstateDecorRules Load()
        {
            if (loaded != null) return loaded;
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) throw new InvalidOperationException("Estate decoration rules are missing.");
            return loaded = Parse(asset.text);
        }

        public static EstateDecorRules Parse(string json)
        {
            var rules = JsonUtility.FromJson<EstateDecorRules>(json);
            if (rules == null || rules.version != Version) throw new InvalidOperationException("Unsupported estate decoration rules.");
            if (rules.astra.slotX.Length != rules.astra.slotY.Length || rules.astra.roadsideStep <= 0) throw new InvalidOperationException("Bad yard slots or roadside step.");
            return rules;
        }

        public Tint TintOf(string state) => tints.FirstOrDefault(t => t.state == state) ?? new Tint { state = state };

        // The vertex colour the EstateTint shader reads: (saturation, brightness, light, alpha).
        public Color Colour(string state, float alpha = 1) { var t = TintOf(state); return new Color(t.saturation, t.brightness, t.light, alpha); }

        public KindArt ArtOf(string coreKind) => kindArt.FirstOrDefault(k => k.kind == coreKind);

        public int Stage(string coreKind, int level)
        {
            var art = ArtOf(coreKind); if (art == null || art.stages.Length == 0) return 1;
            return art.stages[Mathf.Clamp(level, 0, art.stages.Length - 1)];
        }
    }

    // Veiled: standing art on closed land, drawn in foot order before the closed-land veil covers it.
    public enum EstateDrawLayer { Terrain, Veiled, Landmark, Ground, Standing, Foreground }

    // One sprite on the map: art id (or a building body), its pivot's grid point, width in scale-1 canvas units, colour for the
    // tint shader and an optional height/width override (yard grounds are stretched ellipses).
    public sealed class EstateDrawItem
    {
        public bool IsometricGround; public bool WaterOnly; public float Rotation;
        public bool IsometricRoad; public int RoadQuarterTurns;
        public Vector2[] RoadPoints; public float RoadWidth; public RectInt[] RoadFootprints;
        public string Role, Source; public Vector2 Grid; public float Width, Aspect = -1; public Color Colour = Color.white;
        public EstateDrawLayer Layer; public int SiteId = -1; public int BuildingId = -1; public string BuildingArt; public int Stage; public float SortY; public string Key = "";
    }

    public sealed class EstatePlan
    {
        public readonly List<EstateDrawItem> Items = new List<EstateDrawItem>();
        public Vector2[] CarryRoute = Array.Empty<Vector2>();
        public readonly List<EstateResidentJob> Residents = new List<EstateResidentJob>();
        public readonly HashSet<Vector2Int> Road = new HashSet<Vector2Int>();
        public readonly Dictionary<int, int> SiteVariants = new Dictionary<int, int>();
        // Grid rectangle that is painted and covered by the night: the zones' bounds plus the night margin.
        public RectInt Area;
        // Warm lamp light around built buildings in open land: grid centre and radius in cells.
        public readonly List<(Vector2 Grid, float Cells)> Lamps = new List<(Vector2, float)>();
        public readonly List<AstraObject> Astra = new List<AstraObject>();
        public readonly Dictionary<Vector2Int, string> States = new Dictionary<Vector2Int, string>();
        // Restoration state of a cell: restored / sage / fallow on open land, closed in closed zones, null off the zones.
        public Func<Vector2Int, string> StateOf = _ => null;
        public AstraScene Scene;
        public IEnumerable<EstateDrawItem> In(EstateDrawLayer layer) => Items.Where(i => i.Layer == layer);
        public int Count(string source) => Items.Count(i => i.Source == source);
    }

    // Deterministic dressing of the estate map from the rules, the config grid and the estate's buildings (#375). Astra's yard,
    // roadside and empty-land rules run unchanged (AstraDecor) on the estate projected to scale-1 screen units; the grid supplies
    // roads, restoration states and the terrain skeleton. Same inputs, same plan.
    public static class EstatePlanner
    {
        static readonly string[] SiteNames = { "stone-foundation", "broken-fence", "charred-frame", "weedy-field", "stone-corner", "survey-stakes" };
        static Vector2 P(Vector2 grid) => EstateMapModel.Project(grid);
        static Vector2Int CellOf(double x, double y) => Vector2Int.FloorToInt(EstateMapModel.Unproject(new Vector2((float)x, (float)y)));

        public static EstatePlan Plan(EstateDecorRules rules, EstateArt art, AccountEstateConfig config, MetaEstateState estate)
        {
            var plan = new EstatePlan();
            var pieces = EstateR8Layout.Pieces(rules, config);
            var buildingScale = EstateR8Layout.UsesPlazaRendering(config) ? 1.5f : rules.buildingScale;
            var kinds = config.Buildings.ToDictionary(k => k.Kind, StringComparer.Ordinal);
            bool OpenCell(Vector2Int c) => config.Zones.Any(z => estate.OpenZones.Contains(z.Id) && c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            bool AnyZone(Vector2Int c) => config.Zones.Any(z => c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            var buildings = estate.Buildings.OrderBy(b => b.Id).ToArray();
            // Colour the nearby-site graph in stable ID order. Prefer an unused neighbouring layout over ID modulo,
            // which repeated the granary and field. Completed sites retain their assignment so neighbours do not change art.
            foreach (var site in buildings)
            {
                var usage = new int[SiteNames.Length];
                foreach (var other in buildings)
                    if (plan.SiteVariants.TryGetValue(other.Id, out var used) &&
                        Mathf.Max(Mathf.Abs(site.X - other.X), Mathf.Abs(site.Y - other.Y)) <= 4) usage[used]++;
                var variant = site.Id % usage.Length;
                for (var candidate = 0; candidate < usage.Length; candidate++)
                    if (usage[candidate] < usage[variant]) variant = candidate;
                plan.SiteVariants.Add(site.Id, variant);
            }
            var taken = new HashSet<Vector2Int>();
            foreach (var b in buildings) for (var u = 0; u < kinds[b.Kind].Width; u++) for (var v = 0; v < kinds[b.Kind].Height; v++) taken.Add(new Vector2Int(b.X + u, b.Y + v));
            var reserved = new HashSet<Vector2Int>(taken);
            if (config.Version == EstateR8Layout.Version)
                foreach (var cell in EstateR8Layout.Load().riverCells) reserved.Add(new Vector2Int(cell.x, cell.y));
            foreach (var piece in pieces)
                for (var u = 0; piece.reserve.Length == 4 && u < piece.reserve[2]; u++) for (var v = 0; v < piece.reserve[3]; v++) reserved.Add(new Vector2Int(piece.reserve[0] + u, piece.reserve[1] + v));

            // Roads follow free open cells, so a building between two doors cannot break the path (#389).
            Vector2Int Door(MetaEstateBuilding b)
            {
                var outside = new Vector2Int(b.X + kinds[b.Kind].Width - 1, b.Y + kinds[b.Kind].Height);
                return OpenCell(outside) ? outside : outside + Vector2Int.down;
            }
            var routes = new List<double[][]>();
            var ribbonRoutes = new List<List<Vector2Int>>();
            var origin = buildings.FirstOrDefault(b => kinds[b.Kind].StartsComplete);
            if (origin != null && EstateR8Layout.UsesPlazaRendering(config))
            {
                var openCells = config.Zones.Where(z => estate.OpenZones.Contains(z.Id))
                    .SelectMany(z => Enumerable.Range(0, z.Width * z.Height)
                        .Select(i => new Vector2Int(z.X + i % z.Width, z.Y + i / z.Width)))
                    .Where(c => !taken.Contains(c)).ToHashSet();
                var bridge = EstateR8Layout.RoadBridge(pieces, config);
                Vector2Int? bridgeCell = bridge == null ? null : new Vector2Int(Mathf.FloorToInt(bridge.u), Mathf.FloorToInt(bridge.v));
                if (config.Version == EstateR8Layout.Version && bridgeCell.HasValue)
                {
                    // The road is visible through unreclaimed land, but cannot open or build on those cells.
                    foreach (var cell in RoadPath(Door(origin), bridgeCell.Value, c => AnyZone(c) && !taken.Contains(c))) openCells.Add(cell);
                }
                if (bridgeCell.HasValue) openCells.Add(bridgeCell.Value);
                foreach (var path in TrunkRoads(Door(origin), buildings.Where(b => b.Id != origin.Id).Select(Door), openCells, bridgeCell))
                {
                    ribbonRoutes.Add(path);
                    foreach (var cell in path) plan.Road.Add(cell);
                    routes.Add(path.Select(c => { var p = P(c + new Vector2(.5f, .5f)); return new double[] { p.x, p.y }; }).ToArray());
                }
            }
            else if (origin != null)
                foreach (var target in buildings.Where(b => b.Id != origin.Id && OpenCell(Door(b))))
                {
                    // Historical compact layouts retain their original decoration routes and road coordinates.
                    Vector2Int start = Door(origin), end = Door(target), corner = new Vector2Int(end.x, start.y);
                    foreach (var cell in Walk(start, corner).Concat(Walk(corner, end)))
                        if (OpenCell(cell) && !taken.Contains(cell)) plan.Road.Add(cell);
                    routes.Add(new[] { start, corner, end }.Distinct().Select(c => { var p = P(c + new Vector2(.5f, .5f)); return new double[] { p.x, p.y }; }).ToArray());
                }

            // Restoration: tended land (built sites and roads) is in colour, fading through sage to fallow (#375) [가설].
            var tended = buildings.Where(b => b.Level > 0).SelectMany(b => Enumerable.Range(0, kinds[b.Kind].Width * kinds[b.Kind].Height)
                .Select(i => new Vector2Int(b.X + i % kinds[b.Kind].Width, b.Y + i / kinds[b.Kind].Width))).Concat(plan.Road).ToArray();
            string State(Vector2Int c)
            {
                if (!AnyZone(c)) return null;
                if (!OpenCell(c)) return "closed";
                var d = tended.Length == 0 ? int.MaxValue : tended.Min(t => Mathf.Max(Mathf.Abs(t.x - c.x), Mathf.Abs(t.y - c.y)));
                return d <= rules.restoration.restoredRadius ? "restored" : d <= rules.restoration.sageRadius ? "sage" : "fallow";
            }
            string StateAt(Vector2 grid) { var c = Vector2Int.FloorToInt(grid); if (!plan.States.TryGetValue(c, out var s)) plan.States[c] = s = State(c); return s; }
            plan.StateOf = c => StateAt(c);

            // The painted area: the zones' bounds plus the night margin, aligned to terrain blocks from the grid origin.
            var t = rules.terrain; var cl = rules.closedLand; var m = rules.night.margin;
            int minU = config.Zones.Min(z => z.X) - m, minV = config.Zones.Min(z => z.Y) - m, maxU = config.Zones.Max(z => z.X + z.Width) + m, maxV = config.Zones.Max(z => z.Y + z.Height) + m;
            minU = Mathf.FloorToInt(minU / (float)t.patchCells) * t.patchCells; minV = Mathf.FloorToInt(minV / (float)t.patchCells) * t.patchCells;
            plan.Area = new RectInt(minU, minV, maxU - minU, maxV - minV);
            // Terrain patches over the whole area, one per patchCells block, picked by the block centre's state; land outside
            // the open zones mixes fallow and sage by seed and takes the cool closed colour.
            // Patches spill past their block, so the outermost ring of blocks stays bare: every patch ends inside the night's
            // opaque rim at the area's edge.
            if (EstateR8Layout.UsesPlazaRendering(config))
            {
                const int tileCells = 8;
                for (var u = plan.Area.xMin; u < plan.Area.xMax; u += tileCells)
                    for (var v = plan.Area.yMin; v < plan.Area.yMax; v += tileCells)
                        plan.Items.Add(new EstateDrawItem { Role = "ground-grass-r4", Source = "terrain", Grid = new Vector2(u + tileCells * .5f, v + tileCells * .5f),
                            Width = tileCells * 2 * EstateMapModel.HalfWidth, Aspect = .5f, IsometricGround = true,
                            Colour = Color.white, Layer = EstateDrawLayer.Terrain, Key = $"grass-r4-{u}-{v}" });
                AddSoilPatches(plan, rules, art, OpenCell, reserved);
            }
            else for (var u = plan.Area.xMin + t.patchCells; u < plan.Area.xMax - t.patchCells; u += t.patchCells)
                for (var v = plan.Area.yMin + t.patchCells; v < plan.Area.yMax - t.patchCells; v += t.patchCells)
                {
                    var centre = new Vector2(u + t.patchCells * .5f, v + t.patchCells * .5f); var state = StateAt(centre) ?? "closed"; var dark = state == "closed";
                    if (dark) state = AstraDecor.Rand(rules.astra, "closed-terrain", $"{u}-{v}") < cl.sageShare ? "sage" : "fallow";
                    var role = state == "restored" ? t.restored : state == "sage" ? t.sage : t.fallow;
                    plan.Items.Add(new EstateDrawItem { Role = role, Source = "terrain", Grid = centre, Width = t.patchCells * 2 * EstateMapModel.HalfWidth * t.patchScale,
                        Colour = dark ? rules.Colour("closed", t.alpha) : new Color(1, 1, 1, t.alpha), Layer = EstateDrawLayer.Terrain, Key = $"terrain-{u}-{v}" });
                }
            // Roads: Astra's painted earth along each grid path instead of flat diamonds.
            var sg = rules.sites;
            if (EstateR8Layout.UsesPlazaRendering(config))
            {
                for (var index = 0; index < ribbonRoutes.Count; index++)
                {
                    var path = ribbonRoutes[index];
                    if (path.Count < 2) continue;
                    var points = path.Select(c => (Vector2)c + new Vector2(.5f, .5f)).ToArray();
                    var bridge = EstateR8Layout.RoadBridge(pieces, config);
                    if (index == 2 && bridge != null) points[points.Length - 1] = new Vector2(bridge.u, bridge.v);
                    var projected = points.Select(P).ToArray();
                    var centre = new Vector2((projected.Min(p => p.x) + projected.Max(p => p.x)) * .5f,
                        (projected.Min(p => p.y) + projected.Max(p => p.y)) * .5f);
                    var width = projected.Max(p => p.x) - projected.Min(p => p.x) + 48;
                    var height = projected.Max(p => p.y) - projected.Min(p => p.y) + 48;
                    plan.Items.Add(new EstateDrawItem { Role = "road-straight", Source = "road", Grid = EstateMapModel.Unproject(centre),
                        Width = width, Aspect = height / width, IsometricRoad = true, RoadPoints = points, RoadWidth = (index <= 2 ? 18 : 11) / (390f / 768f),
                        RoadFootprints = buildings.Select(b => new RectInt(b.X, b.Y, kinds[b.Kind].Width, kinds[b.Kind].Height)).ToArray(),
                        Colour = Color.white, Layer = EstateDrawLayer.Terrain, Key = $"road-ribbon-{index}" });
                }
            }
            else foreach (var cell in plan.Road.OrderBy(c => c.x).ThenBy(c => c.y))
                plan.Items.Add(new EstateDrawItem { Role = sg.ground, Source = "road",
                    Grid = cell + new Vector2(.5f, .5f), Width = sg.roadWidth * 2 * EstateMapModel.HalfWidth,
                    Aspect = sg.aspect, Colour = new Color(1, 1, 1, Mathf.Max(.9f, sg.roadAlpha)),
                    Layer = EstateDrawLayer.Terrain, Key = $"road-{cell.x}-{cell.y}" });

            // Astra's scene: built buildings (anchor = footprint centre, w = painted body width at scale 1), the roads, and the open land.
            var scene = new AstraScene();
            foreach (var b in buildings)
            {
                var kind = kinds[b.Kind]; var artKind = rules.ArtOf(b.Kind)?.art; var stage = rules.Stage(b.Kind, b.Level);
                var anchor = P(new Vector2(b.X + kind.Width * .5f, b.Y + kind.Height * .5f));
                var w = art != null && artKind != null && art.HasBuilding(artKind, stage) ? art.BodyWidth(artKind, stage) * .5 * buildingScale : 96 * buildingScale;
                if (b.Level > 0 && artKind != null) scene.Buildings.Add(new AstraBuilding($"estate-{b.Id:00}", artKind, stage, anchor.x, anchor.y, w));
                else scene.Obstacles.Add((anchor.x, anchor.y - w * .25, 1, w * .5));
            }
            scene.Routes.AddRange(routes);
            scene.RoadsideAllowed = (x, y) => { var c = CellOf(x, y); return OpenCell(c) && !reserved.Contains(c); };
            scene.LandAllowed = (x, y) => { var c = CellOf(x, y); return OpenCell(c) && !reserved.Contains(c) && !plan.Road.Contains(c); };
            scene.LandState = (x, y) => { var s = StateAt(EstateMapModel.Unproject(new Vector2((float)x, (float)y))); return s == "closed" ? null : s; };
            var open = config.Zones.Where(z => estate.OpenZones.Contains(z.Id)).ToArray();
            if (open.Length > 0)
            {
                var corners = open.SelectMany(z => new[] { P(new Vector2(z.X, z.Y)), P(new Vector2(z.X + z.Width, z.Y)), P(new Vector2(z.X, z.Y + z.Height)), P(new Vector2(z.X + z.Width, z.Y + z.Height)) }).ToArray();
                float minX = corners.Min(c => c.x), maxX = corners.Max(c => c.x), minY = corners.Min(c => c.y), maxY = corners.Max(c => c.y);
                scene.LandOriginX = minX + 15; scene.LandOriginY = minY + 20;
                scene.LandColumns = Mathf.CeilToInt((maxX - minX) / (float)rules.astra.landSpacingX); scene.LandRows = Mathf.CeilToInt((maxY - minY) / (float)rules.astra.landSpacingY);
            }
            plan.Scene = scene;
            var (objects, _) = AstraDecor.Layout(rules.astra, scene);
            plan.Astra.AddRange(objects);
            foreach (var o in objects)
            {
                var grid = EstateMapModel.Unproject(new Vector2((float)o.X, (float)o.Y)); var role = o.Id; var width = (float)o.W;
                // Tree groups join Astra's empty-land candidates on tended land [가설]; the selection itself stays art-v1.
                if (o.Kind == "empty-land" && rules.trees.ids.Length > 0 && rules.trees.states.Contains(StateAt(grid)) && AstraDecor.Rand(rules.astra, "tree", o.Key) < rules.trees.share)
                {
                    role = TreeRole((int)(AstraDecor.Rand(rules.astra, "tree-variant", o.Key) * 6), false);
                    width = art != null && art.Has(role) && art.DisplayWidth(role) > 0 ? art.DisplayWidth(role) : width;
                }
                plan.Items.Add(new EstateDrawItem { Role = role, Source = o.Kind, Grid = grid, Width = width, Colour = rules.Colour(StateAt(grid) ?? "restored"),
                    Layer = EstateDrawLayer.Standing, SortY = (float)o.Y, Key = o.Key });
            }

            // Buildings: yard ground (as assembly.js), shadow and body; stage art follows the Core level.
            var yg = rules.yardGround;
            foreach (var sb in scene.Buildings)
            {
                var id = sb.Kind == "well" ? yg.well : sb.Kind == "house" ? yg.house : yg.other;
                var grid = EstateMapModel.Unproject(new Vector2((float)sb.X, (float)(sb.Y + yg.offsetY * sb.W + yg.height * .5 * sb.W)));
                plan.Items.Add(new EstateDrawItem { Role = id, Source = "yard-ground", Grid = grid, Width = (float)(yg.width * sb.W), Aspect = yg.height / yg.width,
                    Colour = new Color(1, 1, 1, yg.alpha), Layer = EstateDrawLayer.Ground, Key = sb.Key + "/ground" });
            }
            var night = rules.night;
            var litAreas = open.Select(z => new RectInt(z.X, z.Y, z.Width, z.Height)).ToArray();
            foreach (var b in buildings)
            {
                var kind = kinds[b.Kind]; var centre = new Vector2(b.X + kind.Width * .5f, b.Y + kind.Height * .5f);
                // Approved sites include their own painted foundation; do not add a second ground island.
                if ((b.Level > 0 || estate.Active?.BuildingId == b.Id) && OpenCell(new Vector2Int(b.X, b.Y)))
                    plan.Lamps.Add((centre, kinds[b.Kind].StartsComplete ? night.homeLampCells : night.lampCells));
            }
            foreach (var b in buildings)
            {
                var kind = kinds[b.Kind]; var artKind = rules.ArtOf(b.Kind)?.art; if (artKind == null) continue;
                var building = estate.Active?.BuildingId == b.Id;
                var level = building ? estate.Active.TargetLevel : b.Level;
                var centre = new Vector2(b.X + kind.Width * .5f, b.Y + kind.Height * .5f);
                // Lv0 is a reclaimable footprint, never an already finished building (#389). Each piece follows the
                // existing hit footprint and remains visible when optional decoration is disabled.
                if (b.Level == 0 && !building)
                {
                    var distance = EstateNight.LightDistance(centre, litAreas, EstateR8Layout.UsesPlazaRendering(config));
                    var light = night.Alpha(distance) < night.edgeAlpha ? "lit" : "dark";
                    var role = "lv0-" + SiteNames[plan.SiteVariants[b.Id]] + "-" + light;
                    AddSite(plan, b.Id, role, centre, 136, "approved");
                    continue;
                }
                // Approved construction silhouettes replace the temporary empty-field body (#426).
                if (building)
                {
                    var distance = EstateNight.LightDistance(centre, litAreas, EstateR8Layout.UsesPlazaRendering(config));
                    var role = EstateConstructionArt.Role(b.Kind == rules.fieldFenceKind, EstateConstructionArt.Stage(estate.Active), night.Alpha(distance) < night.edgeAlpha);
                    var completedWidth = buildingScale * (kind.Width + kind.Height) * EstateMapModel.HalfWidth;
                    plan.Items.Add(new EstateDrawItem { Role = role, Source = "construction", SiteId = b.Id, Grid = centre,
                        Width = art == null ? 136 : EstateConstructionArt.CappedWidth(art, role, artKind, rules.Stage(b.Kind, level), completedWidth),
                        Layer = EstateDrawLayer.Standing, SortY = P(centre).y, Key = $"construction-{b.Id}" });
                    continue;
                }
                plan.Items.Add(new EstateDrawItem { Source = "building", BuildingId = b.Id, BuildingArt = artKind,
                    Stage = rules.Stage(b.Kind, level), Grid = centre,
                    Width = buildingScale * (kind.Width + kind.Height) * EstateMapModel.HalfWidth, Colour = Color.white,
                    Layer = EstateDrawLayer.Standing, SortY = P(centre).y, Key = $"estate-{b.Id:00}" });
            }

            // Field fences and work poses (display only; no production meaning).
            foreach (var b in buildings.Where(b => b.Level > 0))
            {
                var kind = kinds[b.Kind];
                if (b.Kind == rules.fieldFenceKind)
                {
                    Add(plan, rules.fieldFenceCorner, "fence", new Vector2(b.X, b.Y), art, rules.Colour(StateAt(new Vector2(b.X, b.Y)) ?? "restored"));
                    if (estate.Active == null || estate.Active.BuildingId != b.Id)
                        plan.Residents.Add(new EstateResidentJob(b.Id, EstateResidentKind.Hoe, new Vector2(b.X + .6f, b.Y + kind.Height + .2f)));
                }
                if (b.Kind == rules.peasants.carryKind && estate.Active == null) Add(plan, rules.peasants.carry, "peasant", new Vector2(b.X + kind.Width + .3f, b.Y + kind.Height * .6f), art, Color.white);
            }
            if (estate.Active != null)
            {
                var b = buildings.First(x => x.Id == estate.Active.BuildingId); var kind = kinds[b.Kind];
                plan.Residents.Add(new EstateResidentJob(b.Id, EstateResidentKind.Hammer, new Vector2(b.X - .3f, b.Y + kind.Height + .1f)));
                var props = new[] { "timber-pile", "stone-pile", "ladder", "wheelbarrow" };
                for (var i = 0; i < 2; i++)
                    Add(plan, "construction-" + props[(b.Id + i) % props.Length], "construction-prop",
                        new Vector2(b.X + .3f + i * .9f, b.Y + kind.Height + .25f), art, Color.white);
                var granary = buildings.FirstOrDefault(x => x.Kind == rules.peasants.carryKind && x.Level > 0);
                if (granary != null && granary.Id != b.Id)
                    plan.CarryRoute = EstateConstructionArt.Route(plan, Door(granary), Door(b));
                if (plan.CarryRoute.Length > 1)
                    plan.Residents.Add(new EstateResidentJob(granary.Id, EstateResidentKind.Carry, plan.CarryRoute[0]));
                if (plan.CarryRoute.Length < 2)
                    Add(plan, "construction-worker-carry", "peasant", new Vector2(b.X + kind.Width + .3f, b.Y + .5f), art, Color.white);
            }

            // Terrain skeleton pieces placed explicitly (river, bridge with its front rail, paddock) when their zone exists.
            foreach (var piece in pieces.Where(p => config.Zones.Any(z => z.Id == p.zone)))
            {
                var grid = new Vector2(piece.u, piece.v); var state = StateAt(grid) ?? "fallow";
                var closed = state == "closed"; var ground = art != null && art.Has(piece.id) && art.Layer(piece.id) == "ground";
                var layer = ground ? EstateDrawLayer.Terrain : closed ? EstateDrawLayer.Veiled : EstateDrawLayer.Standing;
                var colour = ground && !closed ? Color.white : rules.Colour(state);
                Add(plan, piece.id, "placed", grid, art, colour, layer);
                if (piece.id == "riverbank-patch" || piece.id == "river-bend-patch")
                {
                    plan.Items[plan.Items.Count - 1].WaterOnly = true;
                    plan.Items[plan.Items.Count - 1].Rotation = piece.rotation;
                    for (var side = 0; side < 2; side++)
                    {
                        var bank = piece.id == "river-bend-patch" ? (side == 0 ? "inside" : "outside") : "straight";
                        var role = "bank-" + bank + "-0" + (side + 1) + (closed ? "-dark" : "-lit");
                        Add(plan, role, "river-bank", grid + EstateR8Layout.RotateGridOffset(new Vector2(side == 0 ? -.9f : .9f, side == 0 ? .9f : -.9f), piece.rotation), art,
                            Color.white, closed ? EstateDrawLayer.Landmark : EstateDrawLayer.Standing);
                        plan.Items[plan.Items.Count - 1].Rotation = piece.rotation;
                    }
                }
                if (piece.id == "bridge-nw-se-base")
                    for (var side = 0; side < 2; side++)
                        Add(plan, "bank-bridge-0" + (side + 1) + (closed ? "-dark" : "-lit"), "river-bank",
                            grid + new Vector2(side == 0 ? -.7f : .7f, side == 0 ? .7f : -.7f), art,
                            Color.white, closed ? EstateDrawLayer.Landmark : EstateDrawLayer.Standing);
                if (!string.IsNullOrEmpty(piece.rail)) Add(plan, piece.rail, "placed", grid, art, colour, closed ? EstateDrawLayer.Veiled : EstateDrawLayer.Foreground);
                if (!string.IsNullOrEmpty(piece.with) && !closed) Add(plan, piece.with, "peasant", new Vector2(piece.withU, piece.withV), art, Color.white);
            }

            // Land outside the open zones keeps the shape of the land to come (key art C): one candidate per `every` cells of the
            // painted area, jittered by seed, becomes a wood in wooded zones or a frontier trace (dead trees weighted), skipping
            // open and reserved cells.
            var f = rules.frontier;
            for (var u = plan.Area.xMin; u < plan.Area.xMax; u += cl.every)
                for (var v = plan.Area.yMin; v < plan.Area.yMax; v += cl.every)
                {
                    var key = $"closed-{u}-{v}";
                    if (AstraDecor.Rand(rules.astra, "closed-density", key) > cl.density) continue;
                    var at = new Vector2(u + cl.every * (.5f + ((float)AstraDecor.Rand(rules.astra, "closed-x", key) - .5f) * cl.jitter),
                        v + cl.every * (.5f + ((float)AstraDecor.Rand(rules.astra, "closed-y", key) - .5f) * cl.jitter));
                    var cell = Vector2Int.FloorToInt(at);
                    var inset = Mathf.CeilToInt(rules.night.areaFadeCells) + 1;
                    if (cell.x < plan.Area.xMin + inset || cell.y < plan.Area.yMin + inset || cell.x >= plan.Area.xMax - inset || cell.y >= plan.Area.yMax - inset
                        || OpenCell(cell) || reserved.Contains(cell)) continue;
                    var zone = config.Zones.FirstOrDefault(z => cell.x >= z.X && cell.x < z.X + z.Width && cell.y >= z.Y && cell.y < z.Y + z.Height);
                    var wood = zone == null ? null : cl.woods.FirstOrDefault(w => w.zone == zone.Id && w.ids.Length > 0);
                    var variant = AstraDecor.Rand(rules.astra, "closed-variant", key);
                    if (wood != null && AstraDecor.Rand(rules.astra, "closed-wood", key) < wood.share)
                        Add(plan, TreeRole((int)(variant * 6), true), "wood", at, art, Color.white, EstateDrawLayer.Veiled);
                    else if (AstraDecor.Rand(rules.astra, "closed-dead-tree", key) < cl.deadTreeShare || f.ids.Length == 0)
                        Add(plan, cl.deadTree, "frontier", at, art, rules.Colour("closed"), EstateDrawLayer.Veiled);
                    else
                        Add(plan, f.ids[(int)(variant * f.ids.Length)], "frontier", at, art, rules.Colour("closed"), EstateDrawLayer.Veiled);
                }
            // Only large silhouettes rise above the night. Small grass, pebbles and armour traces remain veiled.
            var silhouettes = plan.Items.Where(i => (i.Layer == EstateDrawLayer.Terrain || i.Layer == EstateDrawLayer.Veiled) &&
                (StateAt(i.Grid) == "closed" || StateAt(i.Grid) == null) &&
                (i.Source == "placed" || i.Role == "frontier-ruin")).ToArray();
            foreach (var item in silhouettes)
                plan.Items.Add(new EstateDrawItem { Role = item.Role, Source = "landmark", Grid = item.Grid, Width = item.Width, Aspect = item.Aspect,
                    WaterOnly = item.WaterOnly, Rotation = item.Rotation, Colour = new Color(0, night.landmarkBrightness, 1, 1),
                    Layer = EstateDrawLayer.Landmark, SortY = item.SortY, Key = "landmark-" + item.Key });
            EstateZoneTerrain.Apply(plan, art, config, estate);
            EstateResidentJob.ApplyBudget(plan);
            return plan;
        }

        static string TreeRole(int variant, bool dark) => "estate-tree-" + new[] { "conifer", "broadleaf", "shrub" }[variant / 2] + "-0" + (variant % 2 + 1) + (dark ? "-dark" : "-lit");

        static void Add(EstatePlan plan, string role, string source, Vector2 grid, EstateArt art, Color colour, EstateDrawLayer layer = EstateDrawLayer.Standing)
        {
            var width = art != null && art.Has(role) ? art.DisplayWidth(role) : 64;
            plan.Items.Add(new EstateDrawItem { Role = role, Source = source, Grid = grid, Width = width > 0 ? width : 64, Colour = colour, Layer = layer, SortY = P(grid).y, Key = source + "-" + role + "-" + grid });
        }

        public static IEnumerable<Vector2Int> GroundCanvasCells(Vector2 centre, float width, float aspect, float margin = 0)
        {
            var half = new Vector2(width * .5f, width * aspect * .5f);
            var corners = new[] { new Vector2(-half.x, -half.y), new Vector2(half.x, -half.y), new Vector2(-half.x, half.y), half };
            var grid = corners.Select(p => centre + EstateMapModel.Unproject(p)).ToArray();
            for (var x = Mathf.FloorToInt(grid.Min(p => p.x) - margin + .0001f); x <= Mathf.FloorToInt(grid.Max(p => p.x) + margin - .0001f); x++)
                for (var y = Mathf.FloorToInt(grid.Min(p => p.y) - margin + .0001f); y <= Mathf.FloorToInt(grid.Max(p => p.y) + margin - .0001f); y++)
                    yield return new Vector2Int(x, y);
        }

        static void AddSoilPatches(EstatePlan plan, EstateDecorRules rules, EstateArt art, Func<Vector2Int, bool> open, HashSet<Vector2Int> reserved)
        {
            var occupied = new HashSet<Vector2Int>(reserved);
            occupied.UnionWith(plan.Road);
            for (var x = plan.Area.xMin; x < plan.Area.xMax; x++)
                for (var y = plan.Area.yMin; y < plan.Area.yMax; y++)
                {
                    var cell = new Vector2Int(x, y); var key = $"soil-r4-{x}-{y}";
                    if (!open(cell) || occupied.Contains(cell) || AstraDecor.Rand(rules.astra, "soil-density", key) > .65) continue;
                    var variant = 1 + (int)(AstraDecor.Rand(rules.astra, "soil-variant", key) * 6);
                    var role = "soil-patch-r4-" + variant.ToString("00");
                    var sprite = art.Get(role); var aspect = sprite.rect.height / sprite.rect.width;
                    var centre = cell + new Vector2(.5f, .5f);
                    foreach (var width in new[] { 64f, 48f, 32f, 20f, 12f })
                    {
                        var covered = GroundCanvasCells(centre, width, aspect).ToArray();
                        if (covered.Any(c => !open(c) || occupied.Contains(c)) ||
                            GroundCanvasCells(centre, width, aspect, .3f).Any(plan.Road.Contains)) continue;
                        plan.Items.Add(new EstateDrawItem { Role = role, Source = "soil", Grid = centre, Width = width,
                            Colour = Color.white, Layer = EstateDrawLayer.Terrain, Key = key });
                        occupied.UnionWith(covered); break;
                    }
                }
        }

        static void AddSite(EstatePlan plan, int siteId, string role, Vector2 grid, float width, string part, Color? colour = null, float aspect = -1)
        {
            plan.Items.Add(new EstateDrawItem { Role = role, Source = "reclaimable-site", SiteId = siteId, Grid = grid,
                Width = width, Aspect = aspect, Colour = colour ?? Color.white, Layer = EstateDrawLayer.Ground, SortY = P(grid).y,
                Key = $"site-{siteId:00}-{part}" });
        }

        static IEnumerable<Vector2Int> Walk(Vector2Int from, Vector2Int to)
        {
            var cell = from; yield return cell;
            while (cell != to) { cell += new Vector2Int(Math.Sign(to.x - cell.x), Math.Sign(to.y - cell.y)); yield return cell; }
        }

        public static (string Role, int QuarterTurns) RoadStyle(Vector2Int cell, HashSet<Vector2Int> road)
        {
            bool Has(int direction) => road.Contains(cell + RoadSteps[direction % 4]);
            var count = RoadSteps.Count(step => road.Contains(cell + step));
            if (count >= 3)
            {
                // The source fork has a south stem and two north arms. Rotate its missing side to the empty cell.
                for (var turn = 0; turn < 4; turn++) if (!Has((turn + 3) % 4)) return ("road-fork", turn);
                return ("road-fork", 0);
            }
            for (var turn = 0; turn < 4; turn++)
                if (Has((turn + 2) % 4) && Has((turn + 3) % 4)) return ("road-bend", turn);
            return ("road-straight", Has(1) || Has(3) ? 1 : 0);
        }

        // r7 only: one plaza trunk, then shortest entrance spurs joining the existing tree once. A new cell may
        // touch only one old road cell, so merging branches cannot form loops or side-by-side duplicate routes.
        public static List<List<Vector2Int>> TrunkRoads(Vector2Int plaza, IEnumerable<Vector2Int> doors, HashSet<Vector2Int> allowed, Vector2Int? bridge = null)
        {
            var routes = new List<List<Vector2Int>>();
            if (!allowed.Contains(plaza)) return routes;
            var road = new HashSet<Vector2Int> { plaza };
            routes.Add(new List<Vector2Int> { plaza });
            var west = allowed.OrderBy(c => c.x).ThenBy(c => Math.Abs(c.y - plaza.y)).ThenBy(c => c.y).First();
            var east = bridge ?? allowed.OrderByDescending(c => c.x).ThenBy(c => Math.Abs(c.y - plaza.y)).ThenBy(c => c.y).First();
            foreach (var end in new[] { west, east }.Concat(doors))
            {
                var path = JoinRoad(end, road, allowed);
                if (path.Count == 0) continue;
                road.UnionWith(path); routes.Add(path);
            }
            return routes;
        }

        static List<Vector2Int> JoinRoad(Vector2Int entrance, HashSet<Vector2Int> road, HashSet<Vector2Int> allowed)
        {
            var path = new List<Vector2Int>();
            if (!allowed.Contains(entrance) || road.Contains(entrance)) return path;
            var parents = new Dictionary<Vector2Int, Vector2Int> { [entrance] = entrance };
            var pending = new Queue<Vector2Int>(); pending.Enqueue(entrance);
            while (pending.Count > 0)
            {
                var cell = pending.Dequeue(); var contacts = 0; var junction = cell;
                foreach (var step in RoadSteps) if (road.Contains(cell + step)) { contacts++; junction = cell + step; }
                if (contacts > 1) continue;
                if (contacts == 1)
                {
                    path.Add(junction);
                    for (var cursor = cell; ; cursor = parents[cursor])
                    { path.Add(cursor); if (cursor == entrance) break; }
                    return path;
                }
                foreach (var step in RoadSteps)
                {
                    var next = cell + step;
                    if (!allowed.Contains(next) || road.Contains(next) || parents.ContainsKey(next)) continue;
                    parents.Add(next, cell); pending.Enqueue(next);
                }
            }
            return path;
        }

        // Stable neighbour order gives equal-length paths the same result on every redraw. This is presentation only.
        static readonly Vector2Int[] RoadSteps = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        public static List<Vector2Int> RoadPath(Vector2Int from, Vector2Int to, Func<Vector2Int, bool> allowed)
        {
            var path = new List<Vector2Int>();
            if (!allowed(from) || !allowed(to)) return path;
            var parents = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
            var pending = new Queue<Vector2Int>(); pending.Enqueue(from);
            while (pending.Count > 0 && !parents.ContainsKey(to))
            {
                var cell = pending.Dequeue();
                foreach (var step in RoadSteps)
                {
                    var next = cell + step;
                    if (parents.ContainsKey(next) || !allowed(next)) continue;
                    parents.Add(next, cell); pending.Enqueue(next);
                }
            }
            if (!parents.ContainsKey(to)) return path;
            for (var cell = to; ; cell = parents[cell]) { path.Add(cell); if (cell == from) break; }
            path.Reverse(); return path;
        }
    }
}
