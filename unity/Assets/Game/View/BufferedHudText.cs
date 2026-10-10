using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    // Numeric HUD text uses a reusable character/vertex buffer, not formatted strings per frame.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BufferedHudText : MaskableGraphic
    {
        const string Glyphs = "0123456789 /:-·봄여름가을겨울체력레벨남음";
        readonly char[] text = new char[96];
        readonly char[] digits = new char[20];
        readonly CharacterInfo[] glyphs = new CharacterInfo[Glyphs.Length];
        readonly UIVertex[] quad = new UIVertex[4];
        Font font;
        int fontSize, length, writing;
        FontStyle style;
        TextAnchor alignment;
        bool changed, populating;
        public override Texture mainTexture => font != null ? font.material.mainTexture : base.mainTexture;

        public static BufferedHudText Create(Text source)
        {
            var rect = UiShell.Rect("Buffered value", source.transform); UiShell.Stretch(rect);
            var value = rect.gameObject.AddComponent<BufferedHudText>();
            value.font = source.font; value.fontSize = source.fontSize; value.style = source.fontStyle;
            value.alignment = source.alignment; value.color = source.color; value.material = source.material; value.raycastTarget = false;
            value.font.RequestCharactersInTexture(Glyphs, value.fontSize, value.style);
            value.RefreshGlyphs(value.font);
            var shadow = source.GetComponent<Shadow>();
            if (shadow != null)
            {
                var copy = rect.gameObject.AddComponent<Shadow>(); copy.effectColor = shadow.effectColor;
                copy.effectDistance = shadow.effectDistance; copy.useGraphicAlpha = shadow.useGraphicAlpha;
            }
            source.enabled = false;
            return value;
        }
        protected override void OnEnable() { base.OnEnable(); Font.textureRebuilt += RefreshGlyphs; }
        protected override void OnDisable() { Font.textureRebuilt -= RefreshGlyphs; base.OnDisable(); }
        void RefreshGlyphs(Font rebuilt)
        {
            if (font == null || rebuilt != font || populating || !IsActive()) return;
            // Like uGUI Text.FontTextureChanged, rebuild immediately inside the Canvas pass:
            // registering another dirty graphic there is rejected by CanvasUpdateRegistry.
            if (CanvasUpdateRegistry.IsRebuildingGraphics() || CanvasUpdateRegistry.IsRebuildingLayout())
            {
                UpdateGeometry(); UpdateMaterial();
            }
            else { SetVerticesDirty(); SetMaterialDirty(); }
        }
        public void Begin() { writing = 0; changed = false; }
        public void Append(string value) { for (var i = 0; i < value.Length; i++) Append(value[i]); }
        public void Append(long value, int minimumDigits = 1)
        {
            if (value < 0) Append('-');
            var magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
            var count = 0;
            do { digits[count++] = (char)('0' + magnitude % 10); magnitude /= 10; } while (magnitude != 0);
            for (var i = count; i < minimumDigits; i++) Append('0');
            while (count > 0) Append(digits[--count]);
        }
        void Append(char value)
        {
            if (writing == text.Length) throw new InvalidOperationException("HUD value exceeds its preallocated character capacity.");
            if (writing >= length || text[writing] != value) changed = true;
            text[writing++] = value;
        }
        public void End()
        {
            changed |= length != writing; length = writing;
            if (changed) SetVerticesDirty();
        }
#if UNITY_EDITOR
        public string CaptureTextForTesting() => new string(text, 0, length);
#endif
        protected override void OnPopulateMesh(VertexHelper helper)
        {
            if (font == null) { helper.Clear(); return; }
            populating = true;
            try
            {
                font.RequestCharactersInTexture(Glyphs, fontSize, style);
                // A request can repopulate missing glyphs without rebuilding the whole atlas.
                // Always read the final metrics/UVs; a texture event alone is not a cache key.
                for (var i = 0; i < Glyphs.Length; i++) font.GetCharacterInfo(Glyphs[i], out glyphs[i], fontSize, style);
                // Font callbacks may rebuild another label through uGUI's shared VertexHelper.
                helper.Clear();

                var width = 0f;
                for (var i = 0; i < length; i++) { var index = Glyphs.IndexOf(text[i]); if (index >= 0) width += glyphs[index].advance; }
                var rect = rectTransform.rect;
                var horizontal = (int)alignment % 3;
                var x = horizontal == 0 ? rect.xMin : horizontal == 1 ? rect.center.x - width * .5f : rect.xMax - width;
                // Match the single-line legacy Text middle alignment using the font's ascent.
                var ascent = font.ascent * (float)fontSize / Mathf.Max(1, font.fontSize);
                var vertical = (int)alignment / 3;
                var lineTop = vertical == 0 ? rect.yMax : vertical == 1 ? rect.center.y + fontSize * .5f : rect.yMin + fontSize;
                var baseline = lineTop - ascent;
                for (var i = 0; i < length; i++)
                {
                    var index = Glyphs.IndexOf(text[i]); if (index < 0) continue;
                    var glyph = glyphs[index];
                    for (var vertex = 0; vertex < 4; vertex++) { quad[vertex] = UIVertex.simpleVert; quad[vertex].color = color; }
                    quad[0].position = new Vector3(x + glyph.minX, baseline + glyph.minY); quad[0].uv0 = glyph.uvBottomLeft;
                    quad[1].position = new Vector3(x + glyph.minX, baseline + glyph.maxY); quad[1].uv0 = glyph.uvTopLeft;
                    quad[2].position = new Vector3(x + glyph.maxX, baseline + glyph.maxY); quad[2].uv0 = glyph.uvTopRight;
                    quad[3].position = new Vector3(x + glyph.maxX, baseline + glyph.minY); quad[3].uv0 = glyph.uvBottomRight;
                    helper.AddUIVertexQuad(quad); x += glyph.advance;
                }
            }
            finally { populating = false; }
        }
    }
}
