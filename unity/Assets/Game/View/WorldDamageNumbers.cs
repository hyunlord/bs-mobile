using System;
using System.Globalization;
using UnityEngine;

namespace Game.View
{
    public sealed class WorldDamageNumbers : IDisposable
    {
        public const int Capacity = 64;
        const float PlacementStep = 0.18f;
        const float LabelGap = 0.025f;
        static readonly int[] PlacementOffsets = { 0, 1, -1, 2, -2, 3, -3 };
        readonly MeshRenderer[] renderers = new MeshRenderer[Capacity];
        readonly TextMesh[] labels = new TextMesh[Capacity];
        readonly Vector2[] origins = new Vector2[Capacity];
        readonly float[] ages = new float[Capacity];
        readonly Transform parent;
        Font font;
        int next;
        readonly string[] amountText;
        static readonly string[] AmountText = CreateAmountText();
        static string[] CreateAmountText()
        {
            var values = new string[16384];
            for (var i = 0; i < values.Length; i++) values[i] = i.ToString(CultureInfo.InvariantCulture);
            return values;
        }
        public WorldDamageNumbers(Transform parent)
        {
            this.parent = parent; amountText = AmountText;
            font = FontProvider.Create(new[] { "0123456789" });
            font.material.renderQueue = 3100;
            font.RequestCharactersInTexture("0123456789",48);
            for (var index = 0; index < Capacity; index++)
            {
                var owner = new GameObject("Pooled damage number"); owner.transform.SetParent(parent, false);
                var label = owner.AddComponent<TextMesh>(); label.font = font; label.fontSize = 48; label.characterSize = 0.045f;
                label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
                var renderer = owner.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material; renderer.sortingOrder = GameVisualTokens.ThreatLayer + 1;
                labels[index] = label; renderers[index] = renderer; owner.SetActive(false);
            }
        }

        public void Add(Vector2 origin, long amount)
        {
            if (amount <= 0) return;
            var index = next; next = (next + 1) % Capacity;
            // Unusually large values remain exact; their event-only formatting is visible in acceptance allocation telemetry.
            labels[index].text = amount < amountText.Length ? amountText[(int)amount] : amount.ToString(CultureInfo.InvariantCulture);
            origins[index] = origin; ages[index] = 0; labels[index].gameObject.SetActive(true);
            labels[index].transform.position = new Vector3(origin.x, origin.y + 0.14f, -0.1f);
            labels[index].color = GameVisualTokens.Attack;
            if (!Place(index, origin)) labels[index].gameObject.SetActive(false);
        }
        bool Place(int index, Vector2 origin)
        {
            foreach (var y in PlacementOffsets)
                foreach (var x in PlacementOffsets)
                {
                    var candidate = origin + new Vector2(x * PlacementStep, y * PlacementStep);
                    labels[index].transform.position = new Vector3(candidate.x, candidate.y + 0.14f, -0.1f);
                    var bounds = renderers[index].bounds;
                    var available = true;
                    for (var other = 0; other < Capacity; other++)
                    {
                        if (other == index || labels[other] == null || !labels[other].gameObject.activeSelf) continue;
                        var occupied = renderers[other].bounds;
                        if (bounds.min.x < occupied.max.x + LabelGap && bounds.max.x > occupied.min.x - LabelGap &&
                            bounds.min.y < occupied.max.y + LabelGap && bounds.max.y > occupied.min.y - LabelGap)
                        { available = false; break; }
                    }
                    if (!available) continue;
                    origins[index] = candidate;
                    return true;
                }
            return false;
        }
        public void Advance(float seconds, bool visible)
        {
            for (var i = 0; i < Capacity; i++)
            {
                var label = labels[i]; if (label == null || !label.gameObject.activeSelf) continue;
                ages[i] += Mathf.Max(0, seconds);
                if (!visible || ages[i] >= GameVisualTokens.DamageNumberSeconds) { label.gameObject.SetActive(false); continue; }
                var progress = ages[i] / GameVisualTokens.DamageNumberSeconds;
                label.transform.position = new Vector3(origins[i].x, origins[i].y + 0.14f + progress * 0.25f, -0.1f);
                var color = GameVisualTokens.Attack; color.a = 1 - progress * progress; label.color = color;
            }
        }
        public void Dispose()
        {
            foreach (var label in labels) if (label != null) Destroy(label.gameObject);
            if (font != null) Destroy(font);
        }
        static void Destroy(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
