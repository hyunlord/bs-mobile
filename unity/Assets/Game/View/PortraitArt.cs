using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    // Source crops and borders are the user-supplied portrait-v0 design contract.
    public static class PortraitArt
    {
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        public static Sprite Get(string asset)
        {
            if (sprites.TryGetValue(asset, out var sprite)) return sprite;
            var texture = Resources.Load<Texture2D>("PortraitV0/" + asset);
            if (texture == null) throw new InvalidOperationException("Missing portrait artwork: " + asset);
            Border(asset, out var border, out _);
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            sprite.name = "PortraitV0/" + asset;
            sprites.Add(asset, sprite);
            return sprite;
        }
        public static bool Apply(Image image, string role)
        {
            string asset;
            switch (role)
            {
                case "ui.panel": asset = "ui-panel"; break;
                case "ui.button.primary": asset = "ui-btn-blue"; break;
                case "ui.button.destructive": asset = "ui-btn-red"; break;
                case "ui.button.secondary": asset = "ui-btn-cream"; break;
                case "ui.button.round.blue": asset = "ui-btn-round-blue"; break;
                case "ui.medallion": asset = "ui-medallion"; break;
                case "ui.bar.wood": asset = "ui-bar-wood"; break;
                case "ui.slot": asset = "ui-slot"; break;
                case "ui.card.common": asset = "ui-card-common"; break;
                case "ui.card.rare": asset = "ui-card-rare"; break;
                case "ui.card.epic": asset = "ui-card-epic"; break;
                case "ui.hp.rail": case "ui.xp.rail": asset = "ui-xp-rail"; break;
                case "ui.hp.fill": asset = "ui-hp-fill"; break;
                case "ui.xp.fill": asset = "ui-xp-fill"; break;
                case "ui.hint": asset = "ui-hint"; break;
                case "ui.confirm": asset = "ui-confirm"; break;
                default: return false;
            }
            image.sprite = Get(asset);
            Border(asset, out var border, out var multiplier);
            image.color = Color.white;
            image.type = border == Vector4.zero ? Image.Type.Simple : Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = multiplier;
            return true;
        }
        static void Border(string asset, out Vector4 border, out float multiplier)
        {
            float pixels = 0, points = 1;
            var all = false;
            switch (asset)
            {
                case "ui-panel": pixels = 44; points = 16; all = true; break;
                case "ui-btn-blue": pixels = 52; points = 26; break;
                case "ui-btn-red": pixels = 52; points = 29; break;
                case "ui-btn-cream": pixels = 46; points = 17; break;
                case "ui-slot": pixels = 48; points = 8; all = true; break;
                case "ui-card-common": case "ui-card-rare": case "ui-card-epic": pixels = 40; points = 12; all = true; break;
                case "ui-xp-rail": case "ui-xp-fill": case "ui-hp-fill": pixels = 36; points = 8; break;
                case "ui-bar-wood": pixels = 44; points = 18; break;
                case "ui-hint": pixels = 60; points = 30; break;
            }
            border = new Vector4(pixels, all ? pixels : 0, pixels, all ? pixels : 0);
            multiplier = pixels == 0 ? 1 : pixels / points;
        }
    }
}
