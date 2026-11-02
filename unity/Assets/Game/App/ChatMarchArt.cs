using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static class ChatMarchArt
    {
        [Serializable] sealed class Manifest { public Entry[] assets; }
        [Serializable] sealed class Entry { public string id, atlas; public int[] rect, border; public int scale; }
        static Manifest manifest;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        static Entry Find(string id)
        {
            if (manifest == null)
            {
                var text = Resources.Load<TextAsset>("ChatMarchUI/chat-march");
                if (text == null) throw new InvalidOperationException("Missing approved chat/march manifest.");
                manifest = JsonUtility.FromJson<Manifest>(text.text);
            }
            var entry = Array.Find(manifest.assets, candidate => candidate.id == id);
            if (entry == null) throw new ArgumentException("Unregistered chat/march art role: " + id, nameof(id));
            return entry;
        }

        public static Sprite Sprite(string id)
        {
            if (sprites.TryGetValue(id, out var sprite)) return sprite;
            var entry = Find(id);
            if (!textures.TryGetValue(entry.atlas, out var texture))
            {
                texture = Resources.Load<Texture2D>("ChatMarchUI/" + entry.atlas);
                if (texture == null) throw new InvalidOperationException("Missing approved chat/march atlas.");
                textures.Add(entry.atlas, texture);
            }
            var r = entry.rect; var b = entry.border;
            sprite = UnityEngine.Sprite.Create(texture, new Rect(r[0], texture.height - r[1] - r[3], r[2], r[3]),
                new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b[0], b[1], b[2], b[3]));
            sprite.name = id;
            sprites.Add(id, sprite);
            return sprite;
        }

        public static void Surface(Image image, string id)
        {
            var entry = Find(id);
            image.sprite = Sprite(id); image.color = Color.white;
            image.type = entry.border[0] + entry.border[1] + entry.border[2] + entry.border[3] > 0 ? Image.Type.Sliced : Image.Type.Simple;
            image.pixelsPerUnitMultiplier = entry.scale;
            image.raycastTarget = false;
        }
    }
}
