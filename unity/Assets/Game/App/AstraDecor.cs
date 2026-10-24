using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.App
{
    // A line-for-line port of Astra's estate auto-decoration placement (Release m3-estate-autodecor-20261024, assembly.js:
    // yardItems / layout) so the estate map places yards, roadside props and empty-land clusters by the same rules and seed (#375).
    // Coordinates are unscaled screen pixels (x right, y down). The scene supplies what differs between Astra's fixed B
    // composition and the game map: buildings, routes, extra obstacles, the allowed areas and the restoration state; the numbers
    // come from AstraDecorRules (EstateDecorRules.json). Doubles and the same arithmetic order keep results identical to the JS.
    public sealed class AstraBuilding
    {
        public string Key, Kind; public int Stage; public double X, Y, W;
        public AstraBuilding(string key, string kind, int stage, double x, double y, double w) { Key = key; Kind = kind; Stage = stage; X = x; Y = y; W = w; }
    }

    public sealed class AstraObject
    {
        public string Key, Id, Kind, Owner; public double X, Y, W; public uint SeedValue, Priority;
        public override string ToString() => $"{Key} {Id} {X:F6},{Y:F6} w{W:F6}";
    }

    public sealed class AstraScene
    {
        public readonly List<AstraBuilding> Buildings = new List<AstraBuilding>();
        public readonly List<double[][]> Routes = new List<double[][]>();
        // Extra elliptical obstacles: hypot((x - cx) * sx, y - cy) < r.
        public readonly List<(double Cx, double Cy, double Sx, double R)> Obstacles = new List<(double, double, double, double)>();
        public Func<double, double, bool> RoadsideAllowed = (x, y) => true;
        public Func<double, double, bool> LandAllowed = (x, y) => true;
        public Func<double, double, string> LandState = (x, y) => "restored";
        public double LandOriginX, LandOriginY; public int LandColumns, LandRows;
    }

    [Serializable]
    public sealed class AstraDecorRules
    {
        public string styleVersion = "art-v1";
        public long seed = 20261024;
        public KitRule[] kits = Array.Empty<KitRule>();
        public double[] slotX = { -.4, .42, -.12 }, slotY = { 8, 10, 22 };
        public double yardJitterX = 8, yardJitterY = 4, yardRoadClearance = 11;
        public string houseKind = "house";
        public WidthRule[] yardWidths = Array.Empty<WidthRule>(); public double yardWidthDefault = 21;
        public double buildingHalfWidth = .47, buildingTop = .57, buildingBottom = 9;
        public double roadsideStart = 12, roadsideStep = 36, roadsideOffset = 19, roadsideOffsetRange = 7, roadsideDensity = .54, roadsideGrass = .75;
        public double roadsideBuildingClearance = 5, roadsideRoadClearance = 16, roadsideGrassWidth = 17, roadsideRocksWidth = 14;
        public double landSpacingX = 43, landSpacingY = 56, landJitterX = 27, landJitterY = 32, landDensity = .6, landWidth = 68, landWidthRange = 22;
        public double landRoadClearance = 31, landBuildingClearance = 19, landSpacingFactor = .65, landVerticalWeight = 1.5, landSameVariantDistance = 160;
        public int landVariants = 3;

        [Serializable] public sealed class KitRule { public string kind; public string[] props = Array.Empty<string>(); }
        [Serializable] public sealed class WidthRule { public string id; public double width; }
    }

    public static class AstraDecor
    {
        // JS: let h=2166136261; for (...) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0;
        public static uint Hash(string s)
        {
            unchecked { uint h = 2166136261; foreach (var c in s) { h ^= c; h *= 16777619; } return h; }
        }

        // JS: hash([seed, domain, id, n, 'art-v1'].join('|')) / 4294967296
        public static double Rand(AstraDecorRules rules, string domain, string id, int n = 0) =>
            Hash(rules.seed + "|" + domain + "|" + id + "|" + n + "|" + rules.styleVersion) / 4294967296.0;

        static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

        static double SegmentDistance(double x, double y, double[] a, double[] b)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], t = Math.Max(0, Math.Min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / (dx * dx + dy * dy)));
            return Hypot(x - a[0] - dx * t, y - a[1] - dy * t);
        }

        public static double RoadDistance(AstraScene scene, double x, double y)
        {
            double d = 999;
            foreach (var p in scene.Routes) for (var i = 1; i < p.Length; i++) d = Math.Min(d, SegmentDistance(x, y, p[i - 1], p[i]));
            return d;
        }

        public static bool Blocked(AstraDecorRules rules, AstraScene scene, double x, double y, double clearance = 0) =>
            scene.Buildings.Any(b => Math.Abs(x - b.X) < b.W * rules.buildingHalfWidth + clearance && y > b.Y - b.W * rules.buildingTop - clearance && y < b.Y + rules.buildingBottom + clearance)
            || scene.Obstacles.Any(o => Hypot((x - o.Cx) * o.Sx, y - o.Cy) < o.R);

        static double YardWidth(AstraDecorRules rules, string id) => rules.yardWidths.FirstOrDefault(w => w.id == id)?.width ?? rules.yardWidthDefault;

        public static List<AstraObject> YardItems(AstraDecorRules rules, AstraBuilding b)
        {
            var pool = rules.kits.FirstOrDefault(k => k.kind == b.Kind)?.props ?? Array.Empty<string>();
            var count = b.Kind == rules.houseKind ? 1 + (int)Math.Floor(Rand(rules, "yard-count", b.Key) * 2) : pool.Length;
            var selected = pool.Select((id, i) => (id, i, h: Hash(rules.seed + "|yard-choice|" + b.Key + "|" + i)))
                .OrderBy(x => x.h).Take(count).OrderBy(x => x.i);
            return selected.Select(s => new AstraObject
            {
                Key = b.Key + "/yard/" + s.i, Id = s.id,
                X = b.X + rules.slotX[s.i] * b.W + (Rand(rules, "yard-jx", b.Key, s.i) - .5) * rules.yardJitterX,
                Y = b.Y + rules.slotY[s.i] + (Rand(rules, "yard-jy", b.Key, s.i) - .5) * rules.yardJitterY,
                W = YardWidth(rules, s.id), Kind = "yard", Owner = b.Key, SeedValue = Hash(rules.seed + "|yard|" + b.Key + "|" + s.i),
            }).ToList();
        }

        public static (List<AstraObject> Objects, List<(string Key, string Reason)> Skipped) Layout(AstraDecorRules rules, AstraScene scene)
        {
            var objects = new List<AstraObject>(); var skipped = new List<(string, string)>();
            foreach (var b in scene.Buildings)
                foreach (var o in YardItems(rules, b))
                {
                    if (RoadDistance(scene, o.X, o.Y) < rules.yardRoadClearance) { skipped.Add((o.Key, "road-reserved")); continue; }
                    objects.Add(o);
                }

            for (var ri = 0; ri < scene.Routes.Count; ri++)
            {
                var slot = 0; var p = scene.Routes[ri];
                for (var si = 1; si < p.Length; si++)
                {
                    double[] a = p[si - 1], bb = p[si]; double dx = bb[0] - a[0], dy = bb[1] - a[1], len = Hypot(dx, dy);
                    for (var s = rules.roadsideStart; s < len; s += rules.roadsideStep)
                        foreach (var side in new[] { -1, 1 })
                        {
                            var key = $"road-{ri}-{si}-{slot++}-{side}";
                            double t = s / len, off = rules.roadsideOffset + Rand(rules, "road", key) * rules.roadsideOffsetRange;
                            double x = a[0] + dx * t - dy / len * off * side, y = a[1] + dy * t + dx / len * off * side;
                            if (!scene.RoadsideAllowed(x, y) || Blocked(rules, scene, x, y, rules.roadsideBuildingClearance) || RoadDistance(scene, x, y) < rules.roadsideRoadClearance) continue;
                            if (Rand(rules, "road-density", key) > rules.roadsideDensity) continue;
                            var id = Rand(rules, "road-type", key) < rules.roadsideGrass ? "grass" : "rocks";
                            objects.Add(new AstraObject { Key = key, Id = id, X = x, Y = y, W = id == "rocks" ? rules.roadsideRocksWidth : rules.roadsideGrassWidth, Kind = "roadside" });
                        }
                }
            }

            var candidates = new List<AstraObject>();
            for (var cy = 0; cy < scene.LandRows; cy++)
                for (var cx = 0; cx < scene.LandColumns; cx++)
                {
                    var key = $"land-{cx}-{cy}";
                    double x = scene.LandOriginX + cx * rules.landSpacingX + (Rand(rules, "land-x", key) - .5) * rules.landJitterX;
                    double y = scene.LandOriginY + cy * rules.landSpacingY + (Rand(rules, "land-y", key) - .5) * rules.landJitterY;
                    if (!scene.LandAllowed(x, y) || RoadDistance(scene, x, y) < rules.landRoadClearance || Blocked(rules, scene, x, y, rules.landBuildingClearance)) continue;
                    if (Rand(rules, "land-density", key) > rules.landDensity) continue;
                    var state = scene.LandState(x, y); if (state == null) continue;
                    var variant = (int)Math.Floor(Rand(rules, "cluster", key) * rules.landVariants);
                    var w = rules.landWidth + Rand(rules, "width", key) * rules.landWidthRange;
                    candidates.Add(new AstraObject { Key = key, Id = $"cluster-{state}-{variant + 1}", X = x, Y = y, W = w, Kind = "empty-land", Priority = Hash(rules.seed + "|priority|" + key) });
                }
            foreach (var o in candidates.OrderBy(c => c.Priority).ThenBy(c => c.Key, StringComparer.Ordinal))
            {
                if (objects.Any(q => q.Kind == "empty-land" && (Hypot(o.X - q.X, (o.Y - q.Y) * rules.landVerticalWeight) < (o.W + q.W) * rules.landSpacingFactor
                    || q.Id == o.Id && Hypot(o.X - q.X, o.Y - q.Y) < rules.landSameVariantDistance))) continue;
                objects.Add(o);
            }
            return (objects, skipped);
        }
    }
}
