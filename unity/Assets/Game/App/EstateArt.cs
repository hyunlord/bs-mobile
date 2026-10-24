using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Game.App
{
    // Astra's estate art (#375) as packed by tools/estate-art.mjs: Resources/EstateArt/estate-art.json plus its atlases.
    // Sprites are cut from the atlases on first use and cached; every estate sprite draws with one shared EstateTint material,
    // so sprites from the same atlas still batch.
    public sealed class EstateArt
    {
        [Serializable] sealed class Manifest { public string version; public BuildingSet buildings; public Entry[] sprites; }
        [Serializable] sealed class BuildingSet { public float shadowScale; public int[] canvas; public BuildingRecord[] records; }
        [Serializable] sealed class BuildingRecord { public string id, kind; public int stage; public int[] rect; public float[] pivot; public float[] drawRect; public float bodyWidth; }
        [Serializable] sealed class Entry { public string id, atlas, layer; public int[] rect; public float[] pivot; public float displayWidth; }

        public const string Directory = "EstateArt";
        static EstateArt loaded;
        readonly Manifest manifest;
        readonly Dictionary<string, Texture2D> atlases = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        Material tint;

        EstateArt(Manifest manifest) { this.manifest = manifest; }

        public static EstateArt Load()
        {
            if (loaded != null) return loaded;
            var asset = Resources.Load<TextAsset>(Directory + "/estate-art");
            if (asset == null) throw new InvalidOperationException("Estate art manifest is missing.");
            var manifest = JsonUtility.FromJson<Manifest>(asset.text);
            if (manifest?.version != "estate-art-r1") throw new InvalidOperationException("Unsupported estate art manifest.");
            return loaded = new EstateArt(manifest);
        }

        public string Version => manifest.version;
        public IEnumerable<string> SpriteIds => manifest.sprites.Select(s => s.id);
        public bool Has(string id) => manifest.sprites.Any(s => s.id == id);
        public float DisplayWidth(string id) => Find(id).displayWidth;
        public string Layer(string id) => Find(id).layer;
        public Vector2 Pivot(string id) { var e = Find(id); return new Vector2(e.pivot[0], e.pivot[1]); }

        // The shared material (null where the shader is unavailable; images then draw untinted with the default UI material).
        public Material Tint
        {
            get
            {
                if (tint != null) return tint;
                var shader = Resources.Load<Shader>("EstateTint");
                if (shader != null && shader.isSupported) tint = new Material(shader) { name = "Estate tint", hideFlags = HideFlags.HideAndDontSave };
                return tint;
            }
        }

        Entry Find(string id) => manifest.sprites.FirstOrDefault(s => s.id == id) ?? throw new InvalidOperationException("Missing estate art: " + id);

        Texture2D Atlas(string file)
        {
            if (atlases.TryGetValue(file, out var texture)) return texture;
            texture = Resources.Load<Texture2D>(Directory + "/" + file);
            if (texture == null) throw new InvalidOperationException("Missing estate atlas: " + file);
            atlases.Add(file, texture); return texture;
        }

        // Manifest rects use a top-left origin; Unity sprite rects a bottom-left one.
        Sprite Cut(string key, string file, int[] rect, Vector2 pivot)
        {
            if (sprites.TryGetValue(key, out var sprite)) return sprite;
            var texture = Atlas(file);
            sprite = Sprite.Create(texture, new Rect(rect[0], texture.height - rect[1] - rect[3], rect[2], rect[3]), pivot, 100, 0, SpriteMeshType.FullRect);
            sprite.name = key; sprites.Add(key, sprite); return sprite;
        }

        public Sprite Get(string id) { var e = Find(id); return Cut(id, "estate-" + e.atlas, e.rect, Pivot(id)); }

        BuildingRecord Building(string kind, int stage) =>
            manifest.buildings.records.FirstOrDefault(r => r.kind == kind && r.stage == stage) ?? throw new InvalidOperationException($"Missing building art {kind} stage {stage}");

        public bool HasBuilding(string kind, int stage) => manifest.buildings.records.Any(r => r.kind == kind && r.stage == stage);
        public Vector2 BuildingCanvas => new Vector2(manifest.buildings.canvas[0], manifest.buildings.canvas[1]);
        public Vector2 BuildingPivot(string kind, int stage) { var r = Building(kind, stage); return new Vector2(r.pivot[0], r.pivot[1]); }
        // Opaque width of the painted body inside its 256-wide canvas (alpha > 8, measured by tools/estate-art.mjs): Astra draws a
        // building of screen width w at scale w / bodyWidth (assembly.js bodyWidths, trim()).
        public float BodyWidth(string kind, int stage) => Building(kind, stage).bodyWidth;

        public Sprite Body(string kind, int stage)
        {
            var r = Building(kind, stage); return Cut(r.id + "-body", "estate-buildings", r.rect, BuildingPivot(kind, stage));
        }

        public Sprite Shadow(string kind, int stage)
        {
            var r = Building(kind, stage); var s = manifest.buildings.shadowScale;
            var rect = r.rect.Select(v => Mathf.RoundToInt(v * s)).ToArray();
            return Cut(r.id + "-shadow", "estate-building-shadows", rect, BuildingPivot(kind, stage));
        }
    }
}
