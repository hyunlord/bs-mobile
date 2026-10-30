using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // View-only candidates remain stable when the camera or restoration state changes.
    public static class EstateZoneTerrain
    {
        [Serializable] public sealed class Settings
        {
            public int seed = 20261028;
            public float clusterSpacing = 4, propSpacing = 2, boundaryCells = 12;
            public float clusterDensity = .7f, propDensity = .65f, spriteScale = .75f;
            public float minimumScale = .8f, maximumScale = 1, fogOpacity = .3f;
        }

        public static uint Hash(string value)
        {
            unchecked
            {
                uint result = 2166136261;
                foreach (var c in value) result = (result ^ c) * 16777619;
                return result;
            }
        }

        [Serializable] sealed class River { public Chunk[] chunks; }
        [Serializable] sealed class Chunk { public string lit, dark; public string[] occupiedZones; public float x, y, width, height; }

        [Serializable] sealed class Footprints { public Footprint[] entries; }
        [Serializable] sealed class Footprint { public string role; public float left, top, width, height; }

        static float Unit(string key) => (Hash(key) & 0xffffff) / 16777216f;
        public static string Theme(string zone)
        {
            if (zone.Contains("wood")) return "forest-cluster";
            if (zone.Contains("hill")) return "hill";
            if (zone.Contains("fields")) return "field-cluster";
            if (zone.Contains("fallow")) return "fallow";
            if (zone.Contains("ruins")) return "ruins";
            if (zone.Contains("riverside")) return "shore-accent";
            return "forest-cluster";
        }

        public static bool PreserveExisting(string zone) => zone == "zone:start" || zone == "zone:inner-village" ||
            zone == "zone:inner-windmill" || zone.StartsWith("zone:outskirts-", StringComparison.Ordinal);

        public static Rect ProjectedBounds(Vector2 grid, float width, float aspect, Vector2 pivot)
        {
            var at = EstateMapModel.Project(grid);
            var height = width / aspect;
            return new Rect(at.x - width * pivot.x, at.y - height * (1 - pivot.y), width, height);
        }

        public static bool ReserveCandidate(Rect bounds, List<Rect> candidateBounds, IEnumerable<Rect> blocked, bool reserve)
        {
            if (reserve && candidateBounds.Any(r => r.Overlaps(bounds))) return false;
            // Immutable candidate reservations precede mutable building/road rejection: hide, never refill.
            if (reserve) candidateBounds.Add(bounds);
            return !blocked.Any(r => r.Overlaps(bounds));
        }

        public static void Apply(EstatePlan plan, EstateArt art, AccountEstateConfig config, MetaEstateState estate)
        {
            if (config.Version != EstateR8Layout.Version || art == null) return;
            var text = Resources.Load<TextAsset>("EstateZoneDensity");
            if (text == null) throw new InvalidOperationException("Missing zone density settings.");
            var settings = JsonUtility.FromJson<Settings>(text.text);
            if (settings.clusterSpacing <= 0 || settings.propSpacing <= 0 || settings.boundaryCells < 1 ||
                settings.minimumScale <= 0 || settings.maximumScale < settings.minimumScale || settings.spriteScale <= 0)
                throw new InvalidOperationException("Invalid zone density settings.");
            var riverAsset = Resources.Load<TextAsset>("EstateZoneRiver");
            if (riverAsset == null) throw new InvalidOperationException("Missing approved r2 river placement.");
            var river = JsonUtility.FromJson<River>(riverAsset.text);
            if (river?.chunks == null || river.chunks.Length == 0) throw new InvalidOperationException("Empty approved r2 river placement.");
            // Approved r2 chunks use exactly the same 64x32 projection as the runtime map.
            // Remove the legacy repeated river strips and banks, preserving bridges and their foreground rails.
            plan.Items.RemoveAll(i => i.Source == "river-bank" || i.Role == "riverbank-patch" || i.Role == "river-bend-patch");
            foreach (var chunk in river.chunks)
            {
                if (chunk.occupiedZones == null) throw new InvalidOperationException("Missing river alpha occupancy.");
                var lit = chunk.occupiedZones.Any(id => estate.OpenZones.Contains(id));
                plan.Items.Add(new EstateDrawItem { Role = lit ? chunk.lit : chunk.dark, Source = "zone-river", Grid = EstateMapModel.Unproject(new Vector2(chunk.x, chunk.y)),
                    Width = chunk.width, Colour = Color.white, Layer = EstateDrawLayer.Terrain, Key = chunk.lit });
            }
            bool PreserveAt(Vector2 at) => config.Zones.Any(z => PreserveExisting(z.Id) && at.x >= z.X && at.x < z.X + z.Width && at.y >= z.Y && at.y < z.Y + z.Height);
            var blocked = new List<Rect>();
            foreach (var item in plan.Items.Where(i => i.BuildingId >= 0 || i.SiteId >= 0 || i.Source == "placed" || i.Source == "yard" || i.Source == "yard-ground" || i.Source == "roadside" ||
                (i.Source == "empty-land" && PreserveAt(i.Grid))))
            {
                var width = item.Width;
                var aspect = 1f;
                var pivot = new Vector2(.5f, .25f);
                if (item.BuildingId >= 0)
                {
                    aspect = art.BuildingCanvas.x / art.BuildingCanvas.y;
                    pivot = art.BuildingPivot(item.BuildingArt, item.Stage);
                }
                else if (art.Has(item.Role))
                {
                    var sprite = art.Get(item.Role);
                    aspect = sprite.rect.width / sprite.rect.height;
                    pivot = art.Pivot(item.Role);
                }
                if (item.Aspect > 0) aspect = 1 / item.Aspect;
                var bounds = ProjectedBounds(item.Grid, width, aspect, pivot);
                bounds.xMin -= 8; bounds.xMax += 8; bounds.yMin -= 8; bounds.yMax += 8;
                blocked.Add(bounds);
            }
            foreach (var cell in plan.Road.Concat(EstateR8Layout.Load().riverCells.Select(c => new Vector2Int(c.x, c.y))))
            {
                var p = EstateMapModel.Project(cell + new Vector2(.5f, .5f));
                blocked.Add(new Rect(p.x - 22, p.y - 14, 44, 28));
            }
            // Replace generic empty-land filler, not yards, roads, construction or residents.
            plan.Items.RemoveAll(i => (i.Source == "empty-land" || i.Source == "wood" || i.Source == "frontier") && !PreserveAt(i.Grid));
            var footprintAsset = Resources.Load<TextAsset>("EstateZoneFootprints");
            if (footprintAsset == null) throw new InvalidOperationException("Missing approved sprite alpha footprints.");
            var footprints = JsonUtility.FromJson<Footprints>(footprintAsset.text).entries.ToDictionary(f => f.role, StringComparer.Ordinal);
            var candidateBounds = new List<Rect>();
            var lowPropCandidates = new List<Rect>();
            void Place(string variant, string state, Vector2 at, string key, string source, float scale, bool reserve, bool lowProp = false)
            {
                var role = "zone-" + variant + "-" + state;
                if (!art.Has(role)) throw new InvalidOperationException("Missing approved zone sprite: " + role);
                var sprite = art.Get(role);
                var width = art.DisplayWidth(role) * settings.spriteScale * scale;
                var canvas = ProjectedBounds(at, width, sprite.rect.width / sprite.rect.height, art.Pivot(role));
                var footprint = footprints[role];
                var bounds = new Rect(canvas.x + canvas.width * footprint.left, canvas.y + canvas.height * footprint.top,
                    canvas.width * footprint.width, canvas.height * footprint.height);
                if (!ReserveCandidate(bounds, lowProp ? lowPropCandidates : candidateBounds, blocked, reserve)) return;
                plan.Items.Add(new EstateDrawItem { Role = role, Source = source, Grid = at, Width = width,
                    Colour = Color.white, Layer = state == "bright" ? EstateDrawLayer.Standing : EstateDrawLayer.Landmark,
                    SortY = EstateMapModel.Project(at).y, Key = key });
            }
            foreach (var zone in config.Zones.OrderBy(z => z.Id, StringComparer.Ordinal))
            {
                var state = estate.OpenZones.Contains(zone.Id) ? "bright" : "dark";
                void Candidates(float spacing, float density, bool cluster)
                {
                    for (var x = zone.X + spacing * .5f; x < zone.X + zone.Width; x += spacing)
                        for (var y = zone.Y + spacing * .5f; y < zone.Y + zone.Height; y += spacing)
                        {
                            var key = settings.seed + ":" + zone.Id + ":" + (cluster ? "cluster:" : "prop:") +
                                Mathf.RoundToInt(x * 100) + ":" + Mathf.RoundToInt(y * 100);
                            if (Unit(key + ":density") > density) continue;
                            var at = new Vector2(x + (Unit(key + ":x") - .5f) * spacing * .6f,
                                y + (Unit(key + ":y") - .5f) * spacing * .6f);
                            var variant = cluster ? (PreserveExisting(zone.Id) ? "fallow" : Theme(zone.Id)) + "-0" + (Hash(key + ":variant") % 4 + 1) :
                                zone.Id.Contains("fields") ? "field-between-0" + (Hash(key + ":variant") % 6 + 1) + "-" + new[] { "S", "M", "L" }[Hash(key + ":size") % 3] :
                                "prop-0" + (Hash(key + ":variant") % 6 + 1);
                            Place(variant, state, at, key, cluster ? "zone-cluster" : "zone-prop",
                                Mathf.Lerp(settings.minimumScale, settings.maximumScale, Unit(key + ":scale")), true, !cluster && PreserveExisting(zone.Id));
                        }
                }
                // Low approved fallow clusters supplement preserved village decoration on fixed world candidates.
                Candidates(settings.clusterSpacing, settings.clusterDensity, true);
                Candidates(settings.propSpacing, settings.propDensity, false);
            }
            // Outside selectable zones stays a world-anchored forest and fog belt at both neighbourhood zooms.
            var area = plan.Area;
            var margin = Mathf.CeilToInt(settings.boundaryCells);
            for (var x = area.xMin - margin; x < area.xMax + margin; x += 4)
                for (var y = area.yMin - margin; y < area.yMax + margin; y += 4)
                {
                    if (config.Zones.Any(z => x >= z.X - 1 && x < z.X + z.Width + 1 && y >= z.Y - 1 && y < z.Y + z.Height + 1)) continue;
                    var key = settings.seed + ":exterior:" + x + ":" + y;
                    var at = new Vector2(x + Unit(key + ":x"), y + Unit(key + ":y"));
                    Place("forest-cluster-0" + (Hash(key) % 4 + 1), "dark", at, key, "zone-exterior", 1, false);
                    if (Hash(key) % 3 != 0) continue;
                    var role = "zone-dense-r8-midnight-mist-0" + (Hash(key + ":fog") % 2 + 1) + "-exterior";
                    plan.Items.Add(new EstateDrawItem { Role = role, Source = "zone-fog", Grid = at,
                        Width = art.DisplayWidth(role), Colour = new Color(1, 1, 1, settings.fogOpacity),
                        Layer = EstateDrawLayer.Landmark, SortY = EstateMapModel.Project(at).y, Key = key + ":fog" });
                }
        }
    }
}
