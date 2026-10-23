using System;
using UnityEngine;

namespace Game.View
{
    public sealed class WorldGrowthLabels : IDisposable
    {
        const int Capacity = 3;
        readonly Transform parent;
        readonly TextMesh[] labels = new TextMesh[Capacity];
        readonly TextMesh[] shadows = new TextMesh[Capacity];
        readonly Renderer[] labelRenderers = new Renderer[Capacity];
        Font font;
        Material material;
        Material shadowMaterial;
        bool disposed;
        readonly Color ink;

        public WorldGrowthLabels(Transform parent, string additionalGlyphs = "", Color? color = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            this.parent = parent;
            ink = color ?? GameVisualTokens.Ready;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if (font == null)
                {
                    var glyphs = "익음 수확 완료 경험치 수리 중 0123456789+ XP" + additionalGlyphs;
                    font = FontProvider.Create(new[] { glyphs });
                    font.RequestCharactersInTexture(glyphs,48);
                    material = new Material(font.material)
                    {
                        name = "Growth label ink",
                        renderQueue = 3000 + GameVisualTokens.WaveTransientCueLayer,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    shadowMaterial = new Material(font.material)
                    {
                        name = "Growth label shadow ink",
                        renderQueue = 3000 + GameVisualTokens.WaveTransientCueLayer - 1,
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    Font.textureRebuilt += RefreshFontTexture;
                }
                shadows[slot] = CreateLabel("Growth label shadow " + slot, shadowMaterial);
                labels[slot] = CreateLabel("Growth label " + slot, material);
                labelRenderers[slot] = labels[slot].GetComponent<Renderer>();
            }
            Clear();
        }

        public void BeginFrame() => Clear();

        public void ShowClamped(int slot, string message, Vector2 position, Rect worldBounds, float opacity = 1)
        {
            if (worldBounds.width <= 0 || worldBounds.height <= 0) { Hide(slot); return; }
            Show(slot, message, position, opacity);
            var bounds = labelRenderers[slot].bounds;
            var center = (Vector2)bounds.center;
            var extent = (Vector2)bounds.extents + Vector2.one * .03f;
            var target = new Vector2(
                Mathf.Clamp(center.x, worldBounds.xMin + Mathf.Min(extent.x, worldBounds.width * .5f), worldBounds.xMax - Mathf.Min(extent.x, worldBounds.width * .5f)),
                Mathf.Clamp(center.y, worldBounds.yMin + Mathf.Min(extent.y, worldBounds.height * .5f), worldBounds.yMax - Mathf.Min(extent.y, worldBounds.height * .5f)));
            Show(slot, message, position + target - center, opacity);
        }

        public Bounds SlotBounds(int slot) => labelRenderers[slot].bounds;
        public bool Shown(int slot) => labels[slot] != null && labels[slot].gameObject.activeSelf;
        public float Opacity(int slot) => labels[slot] != null ? labels[slot].color.a : 0;

        // Hero keep-out (#345, the damage numbers' rule): a label box touching the circle round the drawn hero's centre.
        public static bool TouchesCircle(Bounds bounds, Vector2 centre, float radius)
        {
            if (radius <= 0) return false;
            var closest = new Vector2(Mathf.Clamp(centre.x, bounds.min.x, bounds.max.x), Mathf.Clamp(centre.y, bounds.min.y, bounds.max.y));
            return (closest - centre).sqrMagnitude < radius * radius;
        }

        public void Show(int slot, string message, Vector2 position, float opacity = 1)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WorldGrowthLabels));
            if (slot < 0 || slot >= Capacity) throw new ArgumentOutOfRangeException(nameof(slot));
            if (string.IsNullOrEmpty(message) || opacity <= 0)
            {
                Hide(slot);
                return;
            }
            var color = ink;
            color.a = Mathf.Clamp01(opacity);
            var shadowColor = GameVisualTokens.Ink;
            shadowColor.a = color.a;
            Present(shadows[slot], message, position + new Vector2(.012f, -.012f), shadowColor);
            Present(labels[slot], message, position, color);
        }

        TextMesh CreateLabel(string name, Material sharedMaterial)
        {
            var owner = new GameObject(name);
            owner.transform.SetParent(parent, false);
            var label = owner.AddComponent<TextMesh>();
            label.font = font;
            label.fontSize = 48;
            label.characterSize = .035f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            var renderer = owner.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.sortingOrder = 0;
            return label;
        }

        void RefreshFontTexture(Font rebuilt)
        {
            if (rebuilt != font) return;
            if (material != null) material.mainTexture = font.material.mainTexture;
            if (shadowMaterial != null) shadowMaterial.mainTexture = font.material.mainTexture;
        }

        static void Present(TextMesh label, string message, Vector2 position, Color color)
        {
            if (label.text != message) label.text = message;
            label.transform.position = new Vector3(position.x, position.y, -.1f);
            label.color = color;
            label.gameObject.SetActive(true);
        }

        void Hide(int slot)
        {
            if (labels[slot] != null) labels[slot].gameObject.SetActive(false);
            if (shadows[slot] != null) shadows[slot].gameObject.SetActive(false);
        }

        public void Clear()
        {
            for (var slot = 0; slot < Capacity; slot++) Hide(slot);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
            Font.textureRebuilt -= RefreshFontTexture;
            for (var slot = 0; slot < Capacity; slot++)
            {
                if (labels[slot] != null) Destroy(labels[slot].gameObject);
                if (shadows[slot] != null) Destroy(shadows[slot].gameObject);
                labels[slot] = null;
                shadows[slot] = null;
            }
            if (material != null) Destroy(material);
            material = null;
            if (shadowMaterial != null) Destroy(shadowMaterial);
            shadowMaterial = null;
            if (font != null) Destroy(font);
            font = null;
        }

        static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
