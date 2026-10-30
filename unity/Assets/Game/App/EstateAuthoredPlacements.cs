using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // Draft interchange contract, not an Astra export. No production placement file is supplied yet.
    public static class EstateAuthoredPlacements
    {
        [Serializable] public sealed class Document
        {
            public string version, config;
            public Entry[] entries;
        }
        [Serializable] public sealed class Entry
        {
            public string id, zoneId, propId, darkPropId, sizeVariant;
            public float[] grid;
            public float width;
        }

        public static Document Parse(string json, AccountEstateConfig config, Func<string, bool> hasSprite)
        {
            var doc = JsonUtility.FromJson<Document>(json);
            if (doc == null || doc.version != "estate-authored-placements-draft-v1" || doc.config != config.Version || doc.entries == null)
                throw new InvalidOperationException("Invalid authored estate placement contract.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in doc.entries)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id) || !ids.Add(item.id) ||
                    string.IsNullOrWhiteSpace(item.zoneId) || string.IsNullOrWhiteSpace(item.propId) || string.IsNullOrWhiteSpace(item.darkPropId) ||
                    !hasSprite(item.propId) || !hasSprite(item.darkPropId) ||
                    !new[] { "native", "S", "M", "L" }.Contains(item.sizeVariant) ||
                    item.grid == null || item.grid.Length != 2 || item.grid.Any(v => float.IsNaN(v) || float.IsInfinity(v)) ||
                    float.IsNaN(item.width) || float.IsInfinity(item.width) || item.width <= 0)
                    throw new InvalidOperationException("Invalid authored estate placement: " + item?.id);
                var zone = config.Zones.FirstOrDefault(z => z.Id == item.zoneId);
                if (zone == null || item.grid[0] < zone.X || item.grid[0] >= zone.X + zone.Width ||
                    item.grid[1] < zone.Y || item.grid[1] >= zone.Y + zone.Height)
                    throw new InvalidOperationException("Authored placement anchor is outside its declared zone: " + item.id);
            }
            return doc;
        }

        public static Document Load(AccountEstateConfig config, EstateArt art)
        {
            var text = Resources.Load<TextAsset>("EstateAuthoredPlacements");
            return text == null ? null : Parse(text.text, config, art.Has);
        }

        // No RNG, snapping, jitter, resizing, culling or collision-driven replacement. Art pivots stay in EstateArt.
        public static void Apply(EstatePlan plan, Document doc, IEnumerable<string> openZones)
        {
            foreach (var item in doc.entries)
            {
                var grid = new Vector2(item.grid[0], item.grid[1]);
                var open = openZones.Contains(item.zoneId);
                plan.Items.Add(new EstateDrawItem { Role = open ? item.propId : item.darkPropId,
                    Grid = grid, Width = item.width, Colour = Color.white, Source = "zone-authored",
                    Layer = open ? EstateDrawLayer.Standing : EstateDrawLayer.Landmark,
                    SortY = EstateMapModel.Project(grid).y, Key = item.id });
            }
        }
    }
}
