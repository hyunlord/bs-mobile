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
        public string ruinState = "fallow";
        public float constructionSaturation = .55f, constructionAlpha = .85f;
        public YardGround yardGround = new YardGround();
        public Restoration restoration = new Restoration();
        public Terrain terrain = new Terrain();
        public Trees trees = new Trees();
        public Frontier frontier = new Frontier();
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
        [Serializable] public sealed class Frontier { public string[] ids = Array.Empty<string>(); public int every = 4, perBlock = 2, block = 8; public float density = .5f; public string edge = "frontier-ink-edge"; public int edgeEvery = 8; public float edgeAlpha = .85f; }
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

    public enum EstateDrawLayer { Terrain, Ground, Standing, Foreground }

    // One sprite on the map: art id (or a building body), its pivot's grid point, width in scale-1 canvas units, colour for the
    // tint shader and an optional height/width override (yard grounds are stretched ellipses).
    public sealed class EstateDrawItem
    {
        public string Role, Source; public Vector2 Grid; public float Width, Aspect = -1; public Color Colour = Color.white;
        public EstateDrawLayer Layer; public int BuildingId = -1; public string BuildingArt; public int Stage; public float SortY; public string Key = "";
    }

    public sealed class EstatePlan
    {
        public readonly List<EstateDrawItem> Items = new List<EstateDrawItem>();
        public readonly HashSet<Vector2Int> Road = new HashSet<Vector2Int>();
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
            var kinds = config.Buildings.ToDictionary(k => k.Kind, StringComparer.Ordinal);
            bool OpenCell(Vector2Int c) => config.Zones.Any(z => estate.OpenZones.Contains(z.Id) && c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            bool AnyZone(Vector2Int c) => config.Zones.Any(z => c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            var buildings = estate.Buildings.OrderBy(b => b.Id).ToArray();
            var taken = new HashSet<Vector2Int>();
            foreach (var b in buildings) for (var u = 0; u < kinds[b.Kind].Width; u++) for (var v = 0; v < kinds[b.Kind].Height; v++) taken.Add(new Vector2Int(b.X + u, b.Y + v));
            var reserved = new HashSet<Vector2Int>(taken);
            foreach (var piece in rules.placed)
                for (var u = 0; piece.reserve.Length == 4 && u < piece.reserve[2]; u++) for (var v = 0; v < piece.reserve[3]; v++) reserved.Add(new Vector2Int(piece.reserve[0] + u, piece.reserve[1] + v));

            // Roads: L-shaped grid paths from the home door to every other building's door in open land.
            Vector2Int Door(MetaEstateBuilding b) => new Vector2Int(b.X + kinds[b.Kind].Width - 1, b.Y + kinds[b.Kind].Height);
            var routes = new List<double[][]>();
            var origin = buildings.FirstOrDefault(b => kinds[b.Kind].StartsComplete);
            if (origin != null)
                foreach (var target in buildings.Where(b => b.Id != origin.Id && OpenCell(Door(b))))
                {
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

            // Terrain patches over open land, one per patchCells block, picked by the block centre's state.
            var t = rules.terrain;
            foreach (var zone in config.Zones.Where(z => estate.OpenZones.Contains(z.Id)))
                for (var u = zone.X; u < zone.X + zone.Width; u += t.patchCells)
                    for (var v = zone.Y; v < zone.Y + zone.Height; v += t.patchCells)
                    {
                        var centre = new Vector2(u + t.patchCells * .5f, v + t.patchCells * .5f); var state = StateAt(centre);
                        var role = state == "restored" ? t.restored : state == "sage" ? t.sage : t.fallow;
                        plan.Items.Add(new EstateDrawItem { Role = role, Source = "terrain", Grid = centre, Width = t.patchCells * 2 * EstateMapModel.HalfWidth * t.patchScale,
                            Colour = new Color(1, 1, 1, t.alpha), Layer = EstateDrawLayer.Terrain, Key = $"terrain-{u}-{v}" });
                    }

            // Astra's scene: built buildings (anchor = footprint centre, w = painted body width at scale 1), the roads, and the open land.
            var scene = new AstraScene();
            foreach (var b in buildings)
            {
                var kind = kinds[b.Kind]; var artKind = rules.ArtOf(b.Kind)?.art; var stage = rules.Stage(b.Kind, b.Level);
                var anchor = P(new Vector2(b.X + kind.Width * .5f, b.Y + kind.Height * .5f));
                var w = art != null && artKind != null && art.HasBuilding(artKind, stage) ? art.BodyWidth(artKind, stage) * .5 : 96;
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
            foreach (var b in buildings)
            {
                var kind = kinds[b.Kind]; var artKind = rules.ArtOf(b.Kind)?.art; if (artKind == null) continue;
                var building = estate.Active?.BuildingId == b.Id;
                var level = building ? estate.Active.TargetLevel : b.Level;
                var centre = new Vector2(b.X + kind.Width * .5f, b.Y + kind.Height * .5f);
                var colour = b.Level == 0 && !building ? rules.Colour(rules.ruinState) : building ? new Color(rules.constructionSaturation, 1, 1, rules.constructionAlpha) : Color.white;
                plan.Items.Add(new EstateDrawItem { Source = "building", BuildingId = b.Id, BuildingArt = artKind, Stage = rules.Stage(b.Kind, level), Grid = centre,
                    Width = (kind.Width + kind.Height) * EstateMapModel.HalfWidth, Colour = colour, Layer = EstateDrawLayer.Standing, SortY = P(centre).y, Key = $"estate-{b.Id:00}" });
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
                var layer = art != null && art.Has(piece.id) && art.Layer(piece.id) == "ground" ? EstateDrawLayer.Terrain : EstateDrawLayer.Standing;
                var colour = state == "closed" ? rules.Colour("closed") : layer == EstateDrawLayer.Terrain ? Color.white : rules.Colour(state);
                Add(plan, piece.id, "placed", grid, art, colour, layer);
                if (!string.IsNullOrEmpty(piece.rail)) Add(plan, piece.rail, "placed", grid, art, colour, EstateDrawLayer.Foreground);
                if (!string.IsNullOrEmpty(piece.with)) Add(plan, piece.with, "peasant", new Vector2(piece.withU, piece.withV), art, Color.white);
            }

            // The frontier: dark traces just inside closed land and the soft ink edge along every open/closed boundary.
            var f = rules.frontier; var perBlock = new Dictionary<Vector2Int, int>();
            foreach (var closed in config.Zones.Where(z => !estate.OpenZones.Contains(z.Id)))
                foreach (var openZone in open)
                    foreach (var (cell, inward, along, edgeIndex) in Boundary(closed, openZone))
                    {
                        var key = $"frontier-{closed.Id}-{edgeIndex}";
                        if (edgeIndex % f.edgeEvery == f.edgeEvery / 2 && !string.IsNullOrEmpty(f.edge))
                            Add(plan, f.edge, "frontier-edge", cell - (Vector2)inward * .5f + new Vector2(.5f, .5f), art, new Color(1, 1, 1, f.edgeAlpha), EstateDrawLayer.Ground);
                        if (edgeIndex % f.every != 0 || f.ids.Length == 0 || AstraDecor.Rand(rules.astra, "frontier", key) > f.density) continue;
                        var depth = 1 + (int)(AstraDecor.Rand(rules.astra, "frontier-depth", key) * 2);
                        var at = cell + inward * depth; var block = new Vector2Int(at.x / f.block, at.y / f.block);
                        perBlock.TryGetValue(block, out var used); if (used >= f.perBlock) continue; perBlock[block] = used + 1;
                        var role = f.ids[(int)(AstraDecor.Rand(rules.astra, "frontier-type", key) * f.ids.Length)];
                        Add(plan, role, "frontier", at + new Vector2(.5f, .5f), art, rules.Colour("closed"));
                    }
            return plan;
        }

        static void Add(EstatePlan plan, string role, string source, Vector2 grid, EstateArt art, Color colour, EstateDrawLayer layer = EstateDrawLayer.Standing)
        {
            var width = art != null && art.Has(role) ? art.DisplayWidth(role) : 64;
            plan.Items.Add(new EstateDrawItem { Role = role, Source = source, Grid = grid, Width = width > 0 ? width : 64, Colour = colour, Layer = layer, SortY = P(grid).y, Key = source + "-" + role + "-" + grid });
        }

        static IEnumerable<Vector2Int> Walk(Vector2Int from, Vector2Int to)
        {
            var cell = from; yield return cell;
            while (cell != to) { cell += new Vector2Int(Math.Sign(to.x - cell.x), Math.Sign(to.y - cell.y)); yield return cell; }
        }

        // Cells of `closed` along its shared edge with `open`, with the inward step (into closed land) and the index along the edge.
        static IEnumerable<(Vector2Int Cell, Vector2Int Inward, Vector2Int Along, int Index)> Boundary(AccountEstateZone closed, AccountEstateZone open)
        {
            if (closed.X == open.X + open.Width || open.X == closed.X + closed.Width)
            {
                var x = closed.X == open.X + open.Width ? closed.X : closed.X + closed.Width - 1; var inward = new Vector2Int(closed.X == open.X + open.Width ? 1 : -1, 0);
                for (int y = Math.Max(closed.Y, open.Y), i = 0; y < Math.Min(closed.Y + closed.Height, open.Y + open.Height); y++, i++) yield return (new Vector2Int(x, y), inward, Vector2Int.up, i);
            }
            if (closed.Y == open.Y + open.Height || open.Y == closed.Y + closed.Height)
            {
                var y = closed.Y == open.Y + open.Height ? closed.Y : closed.Y + closed.Height - 1; var inward = new Vector2Int(0, closed.Y == open.Y + open.Height ? 1 : -1);
                for (int x = Math.Max(closed.X, open.X), i = 0; x < Math.Min(closed.X + closed.Width, open.X + open.Width); x++, i++) yield return (new Vector2Int(x, y), inward, Vector2Int.right, i);
            }
        }
    }
}
