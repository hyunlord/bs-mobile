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
        [Serializable] public sealed class Placed { public string id, rail, zone; public float u, v; public int[] reserve = Array.Empty<int>(); public string with; public float withU, withV; }
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
        public string Role, Source; public Vector2 Grid; public float Width, Aspect = -1; public Color Colour = Color.white;
        public EstateDrawLayer Layer; public int SiteId = -1; public int BuildingId = -1; public string BuildingArt; public int Stage; public float SortY; public string Key = "";
    }

    public sealed class EstatePlan
    {
        public readonly List<EstateDrawItem> Items = new List<EstateDrawItem>();
        public readonly HashSet<Vector2Int> Road = new HashSet<Vector2Int>();
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
        static Vector2 P(Vector2 grid) => EstateMapModel.Project(grid);
        static Vector2Int CellOf(double x, double y) => Vector2Int.FloorToInt(EstateMapModel.Unproject(new Vector2((float)x, (float)y)));

        public static EstatePlan Plan(EstateDecorRules rules, EstateArt art, AccountEstateConfig config, MetaEstateState estate)
        {
            var plan = new EstatePlan();
            var buildingScale = config.Version == "m3a-prototype-r7" ? 1.5f : rules.buildingScale;
            var kinds = config.Buildings.ToDictionary(k => k.Kind, StringComparer.Ordinal);
            bool OpenCell(Vector2Int c) => config.Zones.Any(z => estate.OpenZones.Contains(z.Id) && c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            bool AnyZone(Vector2Int c) => config.Zones.Any(z => c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            var buildings = estate.Buildings.OrderBy(b => b.Id).ToArray();
            var taken = new HashSet<Vector2Int>();
            foreach (var b in buildings) for (var u = 0; u < kinds[b.Kind].Width; u++) for (var v = 0; v < kinds[b.Kind].Height; v++) taken.Add(new Vector2Int(b.X + u, b.Y + v));
            var reserved = new HashSet<Vector2Int>(taken);
            foreach (var piece in rules.placed)
                for (var u = 0; piece.reserve.Length == 4 && u < piece.reserve[2]; u++) for (var v = 0; v < piece.reserve[3]; v++) reserved.Add(new Vector2Int(piece.reserve[0] + u, piece.reserve[1] + v));

            // Roads follow free open cells, so a building between two doors cannot break the path (#389).
            Vector2Int Door(MetaEstateBuilding b)
            {
                var outside = new Vector2Int(b.X + kinds[b.Kind].Width - 1, b.Y + kinds[b.Kind].Height);
                return OpenCell(outside) ? outside : outside + Vector2Int.down;
            }
            var routes = new List<double[][]>();
            var origin = buildings.FirstOrDefault(b => kinds[b.Kind].StartsComplete);
            if (origin != null)
                foreach (var target in buildings.Where(b => b.Id != origin.Id && OpenCell(Door(b))))
                {
                    if (config.Version == "m3a-prototype-r7")
                    {
                        var path = RoadPath(Door(origin), Door(target), c => OpenCell(c) && !taken.Contains(c));
                        foreach (var cell in path) plan.Road.Add(cell);
                        if (path.Count > 0) routes.Add(path.Select(c => { var p = P(c + new Vector2(.5f, .5f)); return new double[] { p.x, p.y }; }).ToArray());
                    }
                    else
                    {
                        // Historical compact layouts can have doors inside neighbouring footprints. Preserve their
                        // original road/decor routes rather than dropping those routes when BFS rejects an endpoint.
                        Vector2Int start = Door(origin), end = Door(target), corner = new Vector2Int(end.x, start.y);
                        foreach (var cell in Walk(start, corner).Concat(Walk(corner, end)))
                            if (OpenCell(cell) && !taken.Contains(cell)) plan.Road.Add(cell);
                        routes.Add(new[] { start, corner, end }.Distinct().Select(c => { var p = P(c + new Vector2(.5f, .5f)); return new double[] { p.x, p.y }; }).ToArray());
                    }
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
            for (var u = plan.Area.xMin + t.patchCells; u < plan.Area.xMax - t.patchCells; u += t.patchCells)
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
            foreach (var cell in plan.Road.OrderBy(c => c.x).ThenBy(c => c.y))
                plan.Items.Add(new EstateDrawItem { Role = sg.ground, Source = "road", Grid = cell + new Vector2(.5f, .5f), Width = sg.roadWidth * 2 * EstateMapModel.HalfWidth, Aspect = sg.aspect,
                    Colour = new Color(1, 1, 1, sg.roadAlpha), Layer = EstateDrawLayer.Terrain, Key = $"road-{cell.x}-{cell.y}" });

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
                    role = rules.trees.ids[(int)(AstraDecor.Rand(rules.astra, "tree-variant", o.Key) * rules.trees.ids.Length)];
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
            foreach (var b in buildings)
            {
                var kind = kinds[b.Kind]; var centre = new Vector2(b.X + kind.Width * .5f, b.Y + kind.Height * .5f);
                // An unbuilt site stands on painted earth; a built one in open land is lit (stakes and rope art: requested from Astra).
                if (b.Level == 0 && estate.Active?.BuildingId != b.Id)
                    plan.Items.Add(new EstateDrawItem { Role = sg.ground, Source = "site", Grid = centre + new Vector2(.15f, .15f), Width = buildingScale * sg.siteWidth * (kind.Width + kind.Height) * EstateMapModel.HalfWidth,
                        Aspect = sg.aspect, Colour = new Color(1, 1, 1, sg.siteAlpha), Layer = EstateDrawLayer.Ground, Key = $"site-{b.Id:00}" });
                else if (OpenCell(new Vector2Int(b.X, b.Y)))
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
                    AddSite(plan, b.Id, "frontier-ruin", centre + new Vector2(-.22f, -.22f), 70, "foundation");
                    AddSite(plan, b.Id, "frontier-broken-fence", centre + new Vector2(.35f, -.3f), 43, "fence");
                    AddSite(plan, b.Id, "rocks", centre + new Vector2(-.35f, .35f), 22, "stones");
                    AddSite(plan, b.Id, "grass", centre + new Vector2(.45f, .35f), 22, "grass", rules.Colour("fallow"));
                    continue;
                }
                // Every construction site uses the authored empty furrows and the UI's work sign, not a faded finished body.
                if (building) { artKind = "field"; level = 0; }
                plan.Items.Add(new EstateDrawItem { Source = "building", BuildingId = b.Id, BuildingArt = artKind,
                    Stage = building ? rules.Stage(rules.fieldFenceKind, 0) : rules.Stage(b.Kind, level), Grid = centre,
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
                    Add(plan, rules.peasants.farm, "peasant", new Vector2(b.X + .6f, b.Y + kind.Height + .2f), art, Color.white);
                }
                if (b.Kind == rules.peasants.carryKind) Add(plan, rules.peasants.carry, "peasant", new Vector2(b.X + kind.Width + .3f, b.Y + kind.Height * .6f), art, Color.white);
            }
            if (estate.Active != null)
            {
                var b = buildings.First(x => x.Id == estate.Active.BuildingId); var kind = kinds[b.Kind];
                Add(plan, rules.peasants.build, "peasant", new Vector2(b.X - .3f, b.Y + kind.Height + .1f), art, Color.white);
            }

            // Terrain skeleton pieces placed explicitly (river, bridge with its front rail, paddock) when their zone exists.
            foreach (var piece in rules.placed.Where(p => config.Zones.Any(z => z.Id == p.zone)))
            {
                var grid = new Vector2(piece.u, piece.v); var state = StateAt(grid) ?? "fallow";
                var closed = state == "closed"; var ground = art != null && art.Has(piece.id) && art.Layer(piece.id) == "ground";
                var layer = ground ? EstateDrawLayer.Terrain : closed ? EstateDrawLayer.Veiled : EstateDrawLayer.Standing;
                var colour = ground && !closed ? Color.white : rules.Colour(state);
                Add(plan, piece.id, "placed", grid, art, colour, layer);
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
                        Add(plan, wood.ids[(int)(variant * wood.ids.Length)], "wood", at, art, rules.Colour("closed"), EstateDrawLayer.Veiled);
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
                    Colour = new Color(0, night.landmarkBrightness, 1, 1),
                    Layer = EstateDrawLayer.Landmark, SortY = item.SortY, Key = "landmark-" + item.Key });
            return plan;
        }

        static void Add(EstatePlan plan, string role, string source, Vector2 grid, EstateArt art, Color colour, EstateDrawLayer layer = EstateDrawLayer.Standing)
        {
            var width = art != null && art.Has(role) ? art.DisplayWidth(role) : 64;
            plan.Items.Add(new EstateDrawItem { Role = role, Source = source, Grid = grid, Width = width > 0 ? width : 64, Colour = colour, Layer = layer, SortY = P(grid).y, Key = source + "-" + role + "-" + grid });
        }

        static void AddSite(EstatePlan plan, int siteId, string role, Vector2 grid, float width, string part, Color? colour = null)
        {
            plan.Items.Add(new EstateDrawItem { Role = role, Source = "reclaimable-site", SiteId = siteId, Grid = grid,
                Width = width, Colour = colour ?? Color.white, Layer = EstateDrawLayer.Standing, SortY = P(grid).y,
                Key = $"site-{siteId:00}-{part}" });
        }

        static IEnumerable<Vector2Int> Walk(Vector2Int from, Vector2Int to)
        {
            var cell = from; yield return cell;
            while (cell != to) { cell += new Vector2Int(Math.Sign(to.x - cell.x), Math.Sign(to.y - cell.y)); yield return cell; }
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
