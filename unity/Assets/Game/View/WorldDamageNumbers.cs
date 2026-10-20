using System;
using System.Globalization;
using UnityEngine;

namespace Game.View
{
    public sealed class WorldDamageNumbers : IDisposable
    {
        public const int Capacity = 64;
        public const int EnemyVisibleLimit = 6;
        const int ContinuationChunks = 4;
        const float PlacementStep = 0.18f;
        const float LabelGap = 0.025f;
        static readonly int[] PlacementOffsets = { 0, 1, -1, 2, -2, 3, -3 };
        // Hero keep-out (user decision 2026-10-20): a number that would appear within HeroKeepOutOfFigureHeight x the drawn
        // knight's height of its centre moves above the head through the usual free-slot search, never overlapping that circle;
        // with no free slot there it stays where it was, drawn at HeroFadedAlpha.
        public const float HeroKeepOutOfFigureHeight = .6f, HeroFadedAlpha = .35f;
        Vector2 heroCentre; float heroRadius;
        readonly bool[] clearOfHero = new bool[Capacity];
        public int MovedAboveHero { get; private set; }
        public int FadedOverHero { get; private set; }
        public void SetHero(Vector2 centre, float radius) { heroCentre = centre; heroRadius = Mathf.Max(0, radius); }
        public bool NearHero(Vector2 labelCentre) => heroRadius > 0 && (labelCentre - heroCentre).sqrMagnitude < heroRadius * heroRadius;
        bool OverlapsHero(Bounds bounds)
        {
            if (heroRadius <= 0) return false;
            var closest = new Vector2(Mathf.Clamp(heroCentre.x, bounds.min.x, bounds.max.x), Mathf.Clamp(heroCentre.y, bounds.min.y, bounds.max.y));
            return (closest - heroCentre).sqrMagnitude < heroRadius * heroRadius;
        }
        readonly MeshRenderer[] renderers = new MeshRenderer[Capacity];
        readonly TextMesh[] labels = new TextMesh[Capacity];
        readonly TextMesh[,] continuations = new TextMesh[Capacity, ContinuationChunks];
        readonly MeshRenderer[,] continuationRenderers = new MeshRenderer[Capacity, ContinuationChunks];
        readonly int[] chunkCounts = new int[Capacity];
        readonly int[] digitCounts = new int[Capacity];
        readonly float[] centerShifts = new float[Capacity];
        readonly int[] chunks = new int[ContinuationChunks + 1];
        readonly Vector2[] origins = new Vector2[Capacity];
        readonly float[] ages = new float[Capacity];
        readonly Color[] colors = new Color[Capacity];
        readonly int[] subjects = new int[Capacity];
        readonly long[] amounts = new long[Capacity];
        readonly bool[] aggregate = new bool[Capacity];
        readonly Transform parent;
        readonly int visibleLimit;
        public int SuppressedVisualLabels { get; private set; }
        Font font;
        int next;
        readonly string[] amountText;
        static readonly string[] AmountText = CreateAmountText();
        static readonly string[] PaddedChunkText = CreatePaddedChunkText();
        static string[] CreateAmountText()
        {
            var values = new string[16384];
            for (var i = 0; i < values.Length; i++) values[i] = i.ToString(CultureInfo.InvariantCulture);
            return values;
        }
        static string[] CreatePaddedChunkText()
        {
            var values = new string[10000];
            for (var i = 0; i < values.Length; i++) values[i] = i.ToString("D4", CultureInfo.InvariantCulture);
            return values;
        }
        public WorldDamageNumbers(Transform parent, int visibleLimit = EnemyVisibleLimit, float characterSize = .032f, int sortingOffset = 1)
        {
            if (visibleLimit < 1 || visibleLimit > Capacity) throw new ArgumentOutOfRangeException(nameof(visibleLimit));
            this.visibleLimit = visibleLimit;
            this.parent = parent; amountText = AmountText;
            font = FontProvider.Create(new[] { "0123456789" });
            font.material.renderQueue = 3100;
            font.RequestCharactersInTexture("0123456789",48);
            for (var index = 0; index < Capacity; index++)
            {
                var owner = new GameObject("Pooled damage number"); owner.transform.SetParent(parent, false);
                var label = owner.AddComponent<TextMesh>(); label.font = font; label.fontSize = 48; label.characterSize = characterSize;
                label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center;
                var renderer = owner.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material; renderer.sortingOrder = GameVisualTokens.ThreatLayer + sortingOffset;
                labels[index] = label; renderers[index] = renderer; owner.SetActive(false);
                for (var chunk = 0; chunk < ContinuationChunks; chunk++)
                {
                    var child = new GameObject("Pooled damage digits"); child.transform.SetParent(owner.transform, false);
                    var digits = child.AddComponent<TextMesh>(); digits.font = font; digits.fontSize = label.fontSize;
                    digits.characterSize = label.characterSize; digits.anchor = label.anchor; digits.alignment = label.alignment;
                    var childRenderer = child.GetComponent<MeshRenderer>(); childRenderer.sharedMaterial = font.material;
                    childRenderer.sortingOrder = renderer.sortingOrder;
                    continuations[index, chunk] = digits; continuationRenderers[index, chunk] = childRenderer;
                    child.SetActive(false);
                }
            }
        }

        public void Add(Vector2 origin, long amount) => Add(origin, amount, GameVisualTokens.Attack);
        public void AddAggregated(int subjectId, Vector2 origin, long amount)
        {
            if (amount <= 0) return;
            for (var i = 0; i < Capacity; i++)
            {
                if (!aggregate[i] || subjects[i] != subjectId || !labels[i].gameObject.activeSelf || ages[i] > GameVisualTokens.DamageAggregateSeconds) continue;
                if (amount > long.MaxValue - amounts[i]) continue;
                amounts[i] += amount;
                var oldDigits = digitCounts[i];
                SetAmount(i, amounts[i]);
                if (oldDigits != digitCounts[i] && !Place(i, origins[i]))
                {
                    labels[i].gameObject.SetActive(false);
                    SuppressedVisualLabels++;
                }
                return;
            }
            var slot = TryAdd(origin, amount, GameVisualTokens.Attack);
            if (slot < 0) return;
            subjects[slot] = subjectId; amounts[slot] = amount; aggregate[slot] = true;
        }
        void SetAmount(int index, long amount)
        {
            amounts[index] = amount;
            var previousChunks = chunkCounts[index];
            var count = 0;
            var rest = amount;
            if (amount < amountText.Length)
            {
                labels[index].text = amountText[(int)amount];
                digitCounts[index] = amountText[(int)amount].Length;
            }
            else
            {
                while (rest >= 10000) { chunks[count++] = (int)(rest % 10000); rest /= 10000; }
                labels[index].text = amountText[(int)rest];
                digitCounts[index] = amountText[(int)rest].Length + count * 4;
                for (var chunk = 0; chunk < count; chunk++) continuations[index, chunk].text = PaddedChunkText[chunks[count - chunk - 1]];
            }
            chunkCounts[index] = count;
            for (var chunk = count; chunk < previousChunks; chunk++) continuations[index, chunk].gameObject.SetActive(false);
            for (var chunk = previousChunks; chunk < count; chunk++) continuations[index, chunk].gameObject.SetActive(true);
            if (count == 0) { centerShifts[index] = 0; return; }
            labels[index].gameObject.SetActive(true);
            var prefixWidth = renderers[index].bounds.size.x;
            var width = prefixWidth;
            for (var chunk = 0; chunk < count; chunk++)
            {
                var childWidth = continuationRenderers[index, chunk].bounds.size.x;
                continuations[index, chunk].transform.localPosition = new Vector3(width - prefixWidth * .5f + childWidth * .5f, 0, 0);
                width += childWidth;
            }
            centerShifts[index] = (width - prefixWidth) * .5f;
            SetColor(index, colors[index]);
        }
        void SetColor(int index, Color color)
        {
            labels[index].color = color;
            for (var chunk = 0; chunk < chunkCounts[index]; chunk++) continuations[index, chunk].color = color;
        }
        void SetPosition(int index, Vector2 center)
            => labels[index].transform.position = new Vector3(center.x - centerShifts[index], center.y, -.1f);
        Bounds LabelBounds(int index)
        {
            var result = renderers[index].bounds;
            for (var chunk = 0; chunk < chunkCounts[index]; chunk++) result.Encapsulate(continuationRenderers[index, chunk].bounds);
            return result;
        }
        public void Add(Vector2 origin, long amount, Color color)
            => TryAdd(origin, amount, color);
        int TryAdd(Vector2 origin, long amount, Color color)
        {
            if (amount <= 0) return -1;
            var visible = 0;
            for (var i = 0; i < Capacity; i++) if (labels[i].gameObject.activeSelf) visible++;
            if (visible >= visibleLimit) { SuppressedVisualLabels++; return -1; }
            var index = next;
            while (labels[index].gameObject.activeSelf) index = (index + 1) % Capacity;
            next = (index + 1) % Capacity;
            SetAmount(index, amount); aggregate[index] = false;
            origins[index] = origin; ages[index] = 0; labels[index].gameObject.SetActive(true);
            SetPosition(index, origin + Vector2.up * .14f);
            colors[index] = color;
            SetColor(index, color);
            clearOfHero[index] = false;
            if (NearHero(origin + Vector2.up * .14f))
            {
                clearOfHero[index] = true;
                if (Place(index, new Vector2(origin.x, heroCentre.y + heroRadius))) { MovedAboveHero++; return index; }
                clearOfHero[index] = false;
                color.a *= HeroFadedAlpha; colors[index] = color; SetColor(index, color);
                FadedOverHero++;
            }
            if (!Place(index, origin))
            {
                labels[index].gameObject.SetActive(false);
                SuppressedVisualLabels++;
                return -1;
            }
            return index;
        }
        bool Place(int index, Vector2 origin)
        {
            foreach (var y in PlacementOffsets)
                foreach (var x in PlacementOffsets)
                {
                    var candidate = origin + new Vector2(x * PlacementStep, y * PlacementStep);
                    SetPosition(index, candidate + Vector2.up * .14f);
                    var bounds = LabelBounds(index);
                    if (clearOfHero[index] && OverlapsHero(bounds)) continue;
                    var available = true;
                    for (var other = 0; other < Capacity; other++)
                    {
                        if (other == index || labels[other] == null || !labels[other].gameObject.activeSelf) continue;
                        var occupied = LabelBounds(other);
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
                SetPosition(i, origins[i] + Vector2.up * (.14f + progress * .25f));
                var color = colors[i]; color.a *= 1 - progress * progress; SetColor(i, color);
            }
        }
#if UNITY_EDITOR
        public string[] CaptureVisibleAmountsForTesting()
        {
            var result = new System.Collections.Generic.List<string>();
            for (var i = 0; i < Capacity; i++)
            {
                if (!labels[i].gameObject.activeSelf) continue;
                var text = labels[i].text;
                for (var chunk = 0; chunk < chunkCounts[i]; chunk++) text += continuations[i, chunk].text;
                result.Add(text);
            }
            return result.ToArray();
        }
#endif
        public void Dispose()
        {
            foreach (var label in labels) if (label != null) Destroy(label.gameObject);
            if (font != null) Destroy(font);
        }
        static void Destroy(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
