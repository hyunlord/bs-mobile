using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // View-only decoration rules for the estate map (#372, Resources/EstateDecorRules.json). Astra's yard, roadside and empty-land
    // rules will replace the numbers and art roles; the planner below is the frame they plug into. Not under data/, so Core
    // content, saves and replay hashes are unchanged.
    [Serializable]
    public sealed class EstateDecorRules
    {
        public const string ResourceName = "EstateDecorRules", Version = "estate-decor-r0";
        public string version;
        public int seed;
        public YardRule[] yard = Array.Empty<YardRule>();
        public RoadRule road = new RoadRule();
        public ClusterRule clusters = new ClusterRule();

        // Props on the ring of cells around a footprint. The first rule whose kinds contain the building kind (or "*") applies.
        [Serializable] public sealed class YardRule { public string[] kinds = Array.Empty<string>(); public PropRule[] props = Array.Empty<PropRule>(); public int count; }
        // A grid road from the `from` building's door to every other building's door, drawn as ground diamonds, with roadside
        // props every `every` cells.
        [Serializable] public sealed class RoadRule { public string from; public PropRule[] roadside = Array.Empty<PropRule>(); public int every; }
        // Groups on cells left free by footprints, yards and roads.
        [Serializable] public sealed class ClusterRule { public PropRule[] props = Array.Empty<PropRule>(); public int count, minSize, maxSize; public float spread; }
        // `width` is in grid cells (one cell is 64 canvas units wide at scale 1). A flat prop lies on the ground layer under every
        // building; a standing one sorts with the buildings by its foot.
        [Serializable] public sealed class PropRule { public string role; public float width; public bool flat; }

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
            if (rules.clusters.minSize < 1 || rules.clusters.maxSize < rules.clusters.minSize) throw new InvalidOperationException("Bad cluster sizes.");
            if (rules.road.every < 1) throw new InvalidOperationException("Bad roadside spacing.");
            return rules;
        }

        public IEnumerable<string> Roles() =>
            yard.SelectMany(y => y.props).Concat(road.roadside).Concat(clusters.props).Select(p => p.role).Distinct();
    }

    public enum EstateDecorSource { Yard, Road, Roadside, Cluster }

    public readonly struct EstateDecorItem
    {
        public readonly string Role; public readonly Vector2 Grid; public readonly float Width; public readonly bool Flat;
        public readonly EstateDecorSource Source; public readonly int Owner;
        public EstateDecorItem(string role, Vector2 grid, float width, bool flat, EstateDecorSource source, int owner)
        { Role = role; Grid = grid; Width = width; Flat = flat; Source = source; Owner = owner; }
    }

    // Deterministic placement from the rules, the config grid and the estate's buildings. Same inputs, same list; a moved
    // building takes its yard with it, while roads and clusters are recomputed around the new layout.
    public static class EstateDecorPlanner
    {
        // Ring around a footprint, clockwise from its north corner, as offsets from the footprint's top-left cell.
        static IEnumerable<Vector2Int> Ring(int width, int height)
        {
            for (var u = -1; u <= width; u++) yield return new Vector2Int(u, -1);
            for (var v = 0; v <= height; v++) yield return new Vector2Int(width, v);
            for (var u = width - 1; u >= -1; u--) yield return new Vector2Int(u, height);
            for (var v = height - 1; v >= 0; v--) yield return new Vector2Int(-1, v);
        }

        public static List<EstateDecorItem> Plan(EstateDecorRules rules, AccountEstateConfig config, MetaEstateState estate)
        {
            var columns = config.Zones.Max(z => z.X + z.Width); var rows = config.Zones.Max(z => z.Y + z.Height);
            var kinds = config.Buildings.ToDictionary(k => k.Kind, StringComparer.Ordinal);
            var taken = new HashSet<Vector2Int>();
            foreach (var building in estate.Buildings)
                for (var u = 0; u < kinds[building.Kind].Width; u++) for (var v = 0; v < kinds[building.Kind].Height; v++)
                    taken.Add(new Vector2Int(building.X + u, building.Y + v));
            bool Inside(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < columns && c.y < rows;
            bool Open(Vector2Int c) => config.Zones.Any(z => estate.OpenZones.Contains(z.Id) && c.x >= z.X && c.x < z.X + z.Width && c.y >= z.Y && c.y < z.Y + z.Height);
            var items = new List<EstateDecorItem>();
            var buildings = estate.Buildings.OrderBy(b => b.Id).ToArray();

            // Roads first, so yards and clusters keep off them. Doors are the cell in front of a footprint's south-west side.
            Vector2Int Door(MetaEstateBuilding b) => new Vector2Int(b.X + kinds[b.Kind].Width - 1, b.Y + kinds[b.Kind].Height);
            var road = new List<Vector2Int>(); var roadSet = new HashSet<Vector2Int>();
            var origin = buildings.FirstOrDefault(b => b.Kind == rules.road.from);
            if (origin != null && !string.IsNullOrEmpty(rules.road.from))
            {
                var start = Door(origin);
                foreach (var target in buildings.Where(b => b.Id != origin.Id && Open(Door(b))))
                {
                    var end = Door(target); var cell = start;
                    // Along u first, then v: a deterministic L that skips footprint cells it crosses.
                    while (true)
                    {
                        if (Inside(cell) && Open(cell) && !taken.Contains(cell) && roadSet.Add(cell)) road.Add(cell);
                        if (cell == end) break;
                        cell = cell.x != end.x ? new Vector2Int(cell.x + Math.Sign(end.x - cell.x), cell.y) : new Vector2Int(cell.x, cell.y + Math.Sign(end.y - cell.y));
                    }
                }
                // Road cells carry no art role: the page draws them as ground diamonds.
                foreach (var cell in road) items.Add(new EstateDecorItem("", cell, 1, true, EstateDecorSource.Road, -1));
            }
            var used = new HashSet<Vector2Int>(taken); used.UnionWith(roadSet);

            foreach (var building in buildings)
            {
                var kind = kinds[building.Kind];
                var rule = rules.yard.FirstOrDefault(y => y.kinds.Contains(building.Kind)) ?? rules.yard.FirstOrDefault(y => y.kinds.Contains("*"));
                if (rule == null || rule.count <= 0 || rule.props.Length == 0) continue;
                var ring = Ring(kind.Width, kind.Height).ToArray();
                // The ring order starts at a building-relative offset from the seed and the building id, so the yard pattern
                // moves with the building rather than with the grid.
                var first = (int)(Hash(rules.seed, building.Id, 1) % (uint)ring.Length); var placed = 0;
                // Stepping by 5 spreads the props around the ring; it visits every cell when 5 does not divide the ring length.
                var step = ring.Length % 5 == 0 ? 1 : 5;
                for (var i = 0; i < ring.Length && placed < rule.count; i++)
                {
                    var offset = ring[(first + i * step) % ring.Length]; var cell = new Vector2Int(building.X + offset.x, building.Y + offset.y);
                    if (!Inside(cell) || used.Contains(cell)) continue;
                    var prop = rule.props[(int)(Hash(rules.seed, building.Id, 2 + placed) % (uint)rule.props.Length)];
                    items.Add(new EstateDecorItem(prop.role, cell + Jitter(rules.seed, building.Id, 10 + placed), prop.width, prop.flat, EstateDecorSource.Yard, building.Id));
                    used.Add(cell); placed++;
                }
            }

            if (rules.road.roadside.Length > 0)
                for (var i = rules.road.every - 1; i < road.Count; i += rules.road.every)
                {
                    var cell = road[i]; var h = Hash(rules.seed, cell.x * 97 + cell.y, 3);
                    var side = (h & 1) == 0 ? new Vector2Int(cell.x + 1, cell.y) : new Vector2Int(cell.x, cell.y + 1);
                    if (!Inside(side) || used.Contains(side)) side = (h & 1) == 0 ? new Vector2Int(cell.x, cell.y + 1) : new Vector2Int(cell.x + 1, cell.y);
                    if (!Inside(side) || used.Contains(side)) continue;
                    var prop = rules.road.roadside[(int)(h >> 1) % rules.road.roadside.Length];
                    items.Add(new EstateDecorItem(prop.role, side + Jitter(rules.seed, cell.x * 97 + cell.y, 4), prop.width, prop.flat, EstateDecorSource.Roadside, -1));
                    used.Add(side);
                }

            var cluster = rules.clusters;
            if (cluster.count > 0 && cluster.props.Length > 0)
            {
                for (var k = 0; k < cluster.count; k++)
                {
                    var free = new List<Vector2Int>();
                    for (var u = 0; u < columns; u++) for (var v = 0; v < rows; v++) { var c = new Vector2Int(u, v); if (!used.Contains(c)) free.Add(c); }
                    if (free.Count == 0) break;
                    var centre = free[(int)(Hash(rules.seed, k, 5) % (uint)free.Count)];
                    var size = cluster.minSize + (int)(Hash(rules.seed, k, 6) % (uint)(cluster.maxSize - cluster.minSize + 1));
                    // A rejected sample (off the grid or on a used cell) is replaced by the next deterministic candidate, so a
                    // cluster reaches its chosen size unless its neighbourhood runs out of free cells (review on #373).
                    var placed = 0;
                    for (var m = 0; placed < size && m < size * 8; m++)
                    {
                        var angle = Hash(rules.seed, k * 131 + m, 7) / (float)uint.MaxValue * Mathf.PI * 2;
                        var radius = placed == 0 ? 0 : (.5f + Hash(rules.seed, k * 131 + m, 8) / (float)uint.MaxValue * .5f) * cluster.spread * (1 + m / (float)size * .5f);
                        var point = centre + new Vector2(.5f, .5f) + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                        var cell = new Vector2Int(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));
                        if (!Inside(cell) || placed > 0 && used.Contains(cell)) continue;
                        var prop = cluster.props[(int)(Hash(rules.seed, k * 131 + m, 9) % (uint)cluster.props.Length)];
                        items.Add(new EstateDecorItem(prop.role, point, prop.width, prop.flat, EstateDecorSource.Cluster, k));
                        used.Add(cell); placed++;
                    }
                }
            }
            return items;
        }

        static Vector2 Jitter(int seed, int a, int b) =>
            new Vector2(.3f + Hash(seed, a, b) % 1000 / 1000f * .4f, .3f + Hash(seed, a, b + 500) % 1000 / 1000f * .4f);

        // FNV-1a over three integers: stable across platforms and runtimes, unlike System.Random or string hash codes.
        public static uint Hash(int seed, int a, int b)
        {
            var h = Mix(Mix(Mix(2166136261u, seed), a), b);
            return h;
        }

        static uint Mix(uint h, int value)
        {
            for (var shift = 0; shift < 32; shift += 8) { h ^= (uint)(value >> shift) & 0xff; h *= 16777619u; }
            return h;
        }
    }
}
