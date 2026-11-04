using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.App
{
    // Approved original PNGs packed without resizing by tools/art-authoring/world-map/pack.mjs.
    public sealed class WorldMapArt
    {
        [Serializable] sealed class Manifest { public string version; public Entry[] assets; }
        [Serializable] sealed class Entry { public string id, atlas; public int[] rect; public float[] pivot; }
        static WorldMapArt loaded;
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        WorldMapArt(Manifest manifest)
        {
            foreach (var entry in manifest.assets) entries.Add(entry.id, entry);
        }

        public static WorldMapArt Load()
        {
            if (loaded != null) return loaded;
            var text = Resources.Load<TextAsset>("WorldMapArt/world-map");
            if (text == null) throw new InvalidOperationException("Missing world map art manifest.");
            var manifest = JsonUtility.FromJson<Manifest>(text.text);
            if (manifest?.version != "world-map-art-v1" || manifest.assets == null)
                throw new InvalidOperationException("Unsupported world map art manifest.");
            return loaded = new WorldMapArt(manifest);
        }

        public bool Has(string id) => entries.ContainsKey(id);
        Entry Find(string id) => entries.TryGetValue(id, out var entry) ? entry : throw new ArgumentException("Unregistered world map art: " + id, nameof(id));
        public Vector2 Pivot(string id) { var entry = Find(id); return new Vector2(entry.pivot[0], entry.pivot[1]); }
        public Vector2 Size(string id) { var entry = Find(id); return new Vector2(entry.rect[2], entry.rect[3]); }
        public Sprite Get(string id)
        {
            if (sprites.TryGetValue(id, out var sprite)) return sprite;
            var entry = Find(id);
            if (!textures.TryGetValue(entry.atlas, out var texture))
            {
                texture = Resources.Load<Texture2D>("WorldMapArt/" + entry.atlas);
                if (texture == null) throw new InvalidOperationException("Missing world map atlas: " + entry.atlas);
                textures.Add(entry.atlas, texture);
            }
            var rect = entry.rect;
            sprite = Sprite.Create(texture, new Rect(rect[0], texture.height - rect[1] - rect[3], rect[2], rect[3]),
                Pivot(id), 100, 0, SpriteMeshType.FullRect);
            sprite.name = id;
            sprites.Add(id, sprite);
            return sprite;
        }
    }
}
