using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    // A texture may recur after another texture in painter order: those runs must never be coalesced.
    public sealed class EnemyDrawQueue : IDisposable
    {
        struct Entry
        {
            public Texture2D Texture;
            public Vector2 Position, Size, Pivot;
            public Rect Uv;
            public Color Color;
            public float Y, Degrees, Flash, Edge, UpperOpacity;
            public int Sequence;
        }
        sealed class PainterOrder : IComparer<Entry>
        {
            public static readonly PainterOrder Instance = new PainterOrder();
            public int Compare(Entry a, Entry b)
            {
                var y = b.Y.CompareTo(a.Y);
                return y != 0 ? y : a.Sequence.CompareTo(b.Sequence);
            }
        }
        readonly List<Entry> entries = new List<Entry>(4096);
        readonly Dictionary<Texture2D, SpriteBatch> batches = new Dictionary<Texture2D, SpriteBatch>();
        public int DrawCalls { get; private set; }
        public int SubmittedInstances { get; private set; }

        public void BeginFrame()
        {
            entries.Clear(); DrawCalls = 0; SubmittedInstances = 0;
            foreach (var batch in batches.Values) batch.BeginFrame();
        }

        public void Add(Texture2D texture, Vector2 position, Vector2 size, Vector2 pivot, Rect uv,
            float degrees, Color color, float flash, float edge, float upperOpacity, float sortY)
        {
            entries.Add(new Entry { Texture = texture, Position = position, Size = size, Pivot = pivot, Uv = uv,
                Degrees = degrees, Color = color, Flash = flash, Edge = edge, UpperOpacity = upperOpacity,
                Y = sortY, Sequence = entries.Count });
        }

        public void Flush(Shader shader, Camera camera)
        {
            entries.Sort(PainterOrder.Instance);
            for (var start = 0; start < entries.Count;)
            {
                var texture = entries[start].Texture;
                var end = start + 1;
                while (end < entries.Count && end - start < SpriteBatch.MaximumInstances && entries[end].Texture == texture) end++;
                if (!batches.TryGetValue(texture, out var batch))
                {
                    batch = new SpriteBatch(texture, shader, camera, GameVisualTokens.EnemyLayer);
                    batches.Add(texture, batch);
                }
                // Default orthographic transparency sorting uses bounds depth within renderQueue 3050.
                // Globally unique run depths preserve A/B/A and 511-instance splits without moving any sprite.
                batch.SortingDepth = 1f - 2f * start / Mathf.Max(1, entries.Count);
                for (var i = start; i < end; i++)
                {
                    var e = entries[i];
                    batch.Add(e.Position, e.Size, e.Pivot, e.Uv, e.Degrees, e.Color, e.Flash, e.Edge, e.UpperOpacity);
                }
                batch.Flush(); DrawCalls++; SubmittedInstances += end - start; start = end;
            }
        }

        public void Dispose()
        {
            foreach (var batch in batches.Values) batch.Dispose();
            batches.Clear(); entries.Clear();
        }
    }
}
