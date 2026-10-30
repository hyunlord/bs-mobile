using System;
using System.Collections.Generic;
using System.Linq;
using SowSiege.Core;
using UnityEngine;

namespace Game.App
{
    // Approved #574 data. Art is bundled during authoring; no network or placement generation at runtime.
    public static class EstateAuthoredLayout
    {
        [Serializable] public sealed class Document
        {
            public string id, kind, sourceSha256;
            public int version;
            public Asset[] assets;
            public Instance[] instances;
            public Zone[] zones;
            public Background background;
            public Projection projection;
        }
        [Serializable] public sealed class Projection { public float tileWidth, tileHeight; }
        [Serializable] public sealed class Background { public string assetId; public int tilePixels; public float[] canvasOffset; }
        [Serializable] public sealed class Zone { public string id, groundAssetId; public float[] rect; }
        [Serializable] public sealed class Asset { public string id; public float[] size, anchor; public int[] rect; }
        [Serializable] public sealed class Instance
        {
            public string id, assetId, zoneId, category, sizeVariant;
            public float[] grid, scale;
            public bool flipX;
            public float rotationRadians, opacity;
            public int drawOrder;
        }
        static Document loaded;
        static bool Pair(float[] a) => a != null && a.Length == 2 && a.All(Finite);
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static void Require(bool valid, string field) { if (!valid) throw new InvalidOperationException("Invalid approved estate layout: " + field); }
        public static Document Parse(string json, AccountEstateConfig config)
        {
            var doc = JsonUtility.FromJson<Document>(json);
            Require(doc != null && doc.id == "presentation:terrain_layout_r8" && doc.kind == "estate-terrain-layout" && doc.version == 1 && config.Version == EstateR8Layout.Version, "version");
            Require(doc.projection != null && doc.projection.tileWidth == 64 && doc.projection.tileHeight == 32, "projection");
            Require(doc.assets != null && doc.assets.Length > 0 && doc.instances != null && doc.instances.Length > 0 && doc.zones != null, "collections");
            var assets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in doc.assets)
                Require(a != null && !string.IsNullOrWhiteSpace(a.id) && assets.Add(a.id) && Pair(a.size) && a.size.All(v => v > 0) && Pair(a.anchor) && a.rect != null && a.rect.Length == 4 && a.rect[2] == a.size[0] && a.rect[3] == a.size[1], "asset");
            var zones = new HashSet<string>(StringComparer.Ordinal);
            foreach (var z in doc.zones)
            {
                Require(z != null && zones.Add(z.id) && assets.Contains(z.groundAssetId) && z.rect != null && z.rect.Length == 4, "zone");
                var expected = config.Zones.SingleOrDefault(v => v.Id == z.id);
                Require(expected != null && z.rect.SequenceEqual(new float[] { expected.X, expected.Y, expected.Width, expected.Height }), "zone geometry");
            }
            Require(zones.Count == config.Zones.Length && doc.background != null && assets.Contains(doc.background.assetId) && Pair(doc.background.canvasOffset) && doc.background.tilePixels == 512, "background");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var orders = new HashSet<int>();
            foreach (var i in doc.instances)
            {
                Require(i != null && !string.IsNullOrWhiteSpace(i.id) && ids.Add(i.id) && assets.Contains(i.assetId) && (i.zoneId == "exterior" || zones.Contains(i.zoneId)) && Pair(i.grid) && Pair(i.scale) && i.scale.All(v => v > 0) && Finite(i.rotationRadians) && Finite(i.opacity) && i.opacity >= 0 && i.opacity <= 1 && i.category != "building" && orders.Add(i.drawOrder), "instance");
                var z = doc.zones.SingleOrDefault(v => v.id == i.zoneId);
                Require(z == null || (i.grid[0] >= z.rect[0] && i.grid[0] < z.rect[0]+z.rect[2] && i.grid[1] >= z.rect[1] && i.grid[1] < z.rect[1]+z.rect[3]), "anchor zone");
            }
            return doc;
        }
        public static Instance[] Ordered(Document doc) => doc.instances.OrderBy(i => i.drawOrder).ToArray();
        public static Document Load(AccountEstateConfig config)
        {
            if (config.Version != EstateR8Layout.Version) return null;
            if (loaded != null) return loaded;
            var text = Resources.Load<TextAsset>("EstateAuthoredLayout");
            if (text == null) throw new InvalidOperationException("Missing approved estate layout resource.");
            return loaded = Parse(text.text, config);
        }
        // Coordinates use y-down native projection. Rotation is around the scaled canvas centre, not the foot anchor.
        public static Vector2[] Corners(Asset a, Instance i)
        {
            var size = new Vector2(a.size[0] * i.scale[0], a.size[1] * i.scale[1]);
            var origin = EstateMapModel.Project(new Vector2(i.grid[0], i.grid[1])) - new Vector2(a.anchor[0] * i.scale[0], a.anchor[1] * i.scale[1]);
            var centre = origin + size * .5f;
            var c = Mathf.Cos(i.rotationRadians); var s = Mathf.Sin(i.rotationRadians);
            return new[] { Vector2.zero, new Vector2(size.x, 0), size, new Vector2(0, size.y) }.Select(p =>
            { var d = p - size * .5f; return centre + new Vector2(c * d.x - s * d.y, s * d.x + c * d.y); }).ToArray();
        }
    }
}
