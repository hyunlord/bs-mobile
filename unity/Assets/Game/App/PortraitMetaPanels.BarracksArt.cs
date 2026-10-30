using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Game.View;

namespace Game.App
{
    public static partial class PortraitMetaPanels
    {
        [Serializable] sealed class TrainingArtManifest { public TrainingArtEntry[] assets; }
        [Serializable] sealed class TrainingArtEntry { public string id; public int[] rect; public float[] border; public int scale; }
        static readonly Dictionary<string, Sprite> trainingSprites = new Dictionary<string, Sprite>();
        static TrainingArtManifest trainingManifest;
        static void TrainingSurface(Image image, string id)
        {
            if (trainingManifest == null) trainingManifest = JsonUtility.FromJson<TrainingArtManifest>(Resources.Load<TextAsset>("BarracksUI/training").text);
            var entry = Array.Find(trainingManifest.assets, a => a.id == id);
            if (entry == null) throw new InvalidOperationException("Missing training art: " + id);
            if (!trainingSprites.TryGetValue(id, out var sprite))
            {
                var texture = Resources.Load<Texture2D>("BarracksUI/training");
                var r = entry.rect;
                sprite = Sprite.Create(texture, new Rect(r[0], texture.height - r[1] - r[3], r[2], r[3]), new Vector2(.5f,.5f), 100, 0, SpriteMeshType.FullRect,
                    new Vector4(entry.border[0],entry.border[1],entry.border[2],entry.border[3]));
                trainingSprites.Add(id,sprite);
            }
            image.sprite=sprite; image.color=Color.white; image.type=entry.border[0]+entry.border[1]+entry.border[2]+entry.border[3]==0?Image.Type.Simple:Image.Type.Sliced; image.pixelsPerUnitMultiplier=entry.scale;
        }
        static Image TrainingImage(Transform parent,string id,float x,float y,float width,float height)
        {
            var rect=PortraitWidgets.Place(id,parent,x,y,width,height);var image=rect.gameObject.AddComponent<Image>();
            TrainingSurface(image,id);image.raycastTarget=false;return image;
        }
        static Button TrainingQuantity(UiShell ui,Transform parent,string name,string glyph,Action action,float x,float y,bool enabled)
        {
            var button=ActionButton(ui,parent,name,"",action,x,y,48,enabled);
            var state=enabled?"active":"disabled"; TrainingSurface(button.image,"training-quantity-button-"+state);
            TrainingImage(button.transform,"training-quantity-"+glyph+"-"+state,12,12,24,24);return button;
        }
    }
}
