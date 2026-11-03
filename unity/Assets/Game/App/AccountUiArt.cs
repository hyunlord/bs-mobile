using System;
using System.Collections.Generic;
using Game.View;
using UnityEngine;
using UnityEngine.UI;

namespace Game.App
{
    public static class AccountUiArt
    {
        [Serializable] sealed class Manifest { public Entry[] assets; }
        [Serializable] sealed class Entry { public string id; public int[] rect; public float[] border; public int scale; }
        static readonly Dictionary<string, Manifest> manifests = new Dictionary<string, Manifest>();
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public static bool Apply(Image image, string atlas, string id)
        {
            if (!manifests.TryGetValue(atlas, out var manifest))
            {
                var source = Resources.Load<TextAsset>("AccountUI/" + atlas);
                if (source == null) throw new InvalidOperationException("Missing account UI manifest.");
                manifest = JsonUtility.FromJson<Manifest>(source.text); manifests.Add(atlas, manifest);
            }
            var entry = Array.Find(manifest.assets, x => x.id == id);
            if (entry == null) return false;
            var key = atlas + "/" + id;
            if (!sprites.TryGetValue(key, out var sprite))
            {
                var texture = Resources.Load<Texture2D>("AccountUI/" + atlas);
                var r = entry.rect; var b = entry.border;
                sprite = Sprite.Create(texture, new Rect(r[0], texture.height-r[1]-r[3],r[2],r[3]), new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b[0],b[1],b[2],b[3]));
                sprites.Add(key,sprite);
            }
            image.sprite=sprite;image.color=Color.white;
            image.type=sprite.border==Vector4.zero?Image.Type.Simple:Image.Type.Sliced;image.pixelsPerUnitMultiplier=entry.scale;
            return true;
        }
        public static Image Place(Transform parent,string atlas,string id,float x,float y,float width,float height)
        {
            var rect=PortraitWidgets.Place(id,parent,x,y,width,height);var image=rect.gameObject.AddComponent<Image>();
            if(!Apply(image,atlas,id))throw new InvalidOperationException("Missing account UI role: "+id);
            image.raycastTarget=false;return image;
        }
    }
}
