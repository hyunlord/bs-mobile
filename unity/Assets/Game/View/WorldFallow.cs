using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        readonly Dictionary<string, ArtVisual> fallowArt = new Dictionary<string, ArtVisual>(StringComparer.Ordinal);
        readonly List<(Vector2 position, float started)> fallowHarvests = new List<(Vector2, float)>();
        Texture2D fallowMask;
        Color32[] fallowPixels;
        Shader fallowShader;
        float fallowMaskAt = -1;
        bool fallowChapter;
        bool FallowActive => fallowChapter && wave != null && ArtCatalog.ProfileName == "wave-1a";

        public void SetFallowChapter(bool enabled)
        {
            if (batches.Count != 0) throw new InvalidOperationException("Set the chapter appearance before its first presented frame.");
            fallowChapter = enabled;
        }

        bool TryFallowArt(string role, out ArtVisual visual)
        {
            if (fallowArt.TryGetValue(role, out visual)) return visual != null;
            try { visual = art.ResolveRole(role); }
            catch (InvalidOperationException) { visual = null; }
            fallowArt.Add(role, visual);
            return visual != null;
        }

        void ResetFallow()
        {
            fallowHarvests.Clear();
            fallowMaskAt = -1;
        }

        void AcceptFallowEvent(WaveEvent value)
        {
            if (value.Kind == "harvest-complete") fallowHarvests.Add((Point(value.Position), visualTime));
        }

        void UpdateFallowMask()
        {
            if (!FallowActive) return;
            if (fallowMask == null)
            {
                fallowShader = Resources.Load<Shader>("FallowWorldSprite");
                if (fallowShader == null || !fallowShader.isSupported)
                    throw new InvalidOperationException("Fallow restoration shader is unavailable.");
                var resolution = GameVisualTokens.FallowMaskSize;
                fallowMask = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
                {
                    name = "Cared-for land presentation mask", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                fallowPixels = new Color32[resolution * resolution];
            }
            if (visualTime < fallowMaskAt) return;
            fallowMaskAt = visualTime + GameVisualTokens.FallowMaskInterval;
            Array.Clear(fallowPixels, 0, fallowPixels.Length);
            foreach (var work in wave.Work)
            {
                if ((work.Kind == "grain" || work.Kind == "building") && work.Health <= 0) continue;
                var fraction = work.Complete ? 1 : Mathf.Clamp01((float)work.Progress / Mathf.Max(1, work.Required));
                if (work.Kind == "grain")
                {
                    var radius = Mathf.Lerp(GameVisualTokens.FallowSeedRadius, GameVisualTokens.FallowRipeRadius, fraction);
                    var strength = work.Dormant || work.Dry ? .28f : Mathf.Lerp(.48f, 1, fraction);
                    PaintFallow(Point(work.Position), radius, work.Complete ? 1 : 0, strength);
                }
                else if (work.Kind == "building")
                    PaintFallow(Point(work.Position), GameVisualTokens.FallowBuildingRadius * Mathf.Lerp(.45f, 1, fraction), 2, work.Complete ? 1 : .35f + fraction * .4f);
                else if (work.Kind == "water" && work.Wet)
                    PaintFallow(Point(work.Position), GameVisualTokens.FallowSeedRadius, 0, .55f);
            }
            for (var i = fallowHarvests.Count - 1; i >= 0; i--)
            {
                var bloom = fallowHarvests[i];
                var progress = (visualTime - bloom.started) / GameVisualTokens.FallowHarvestSeconds;
                if (progress >= 1) { fallowHarvests.RemoveAt(i); continue; }
                PaintFallow(bloom.position, GameVisualTokens.FallowRipeRadius * (1 + progress * .9f), 1, 1 - progress);
            }
            fallowMask.SetPixels32(fallowPixels);
            fallowMask.Apply(false, false);
            Shader.SetGlobalTexture("_FallowMask", fallowMask);
            Shader.SetGlobalVector("_FallowMap", new Vector4((float)settings.WorldUnitsPerUnityUnit / mapWidth, (float)settings.WorldUnitsPerUnityUnit / mapHeight, 0, 0));
        }

        void PaintFallow(Vector2 center, float radius, int channel, float strength)
        {
            var size = GameVisualTokens.FallowMaskSize;
            var width = (float)mapWidth / settings.WorldUnitsPerUnityUnit;
            var height = (float)mapHeight / settings.WorldUnitsPerUnityUnit;
            var left = Mathf.Clamp(Mathf.FloorToInt((center.x - radius) / width * size), 0, size - 1);
            var right = Mathf.Clamp(Mathf.CeilToInt((center.x + radius) / width * size), 0, size - 1);
            var bottom = Mathf.Clamp(Mathf.FloorToInt((center.y - radius) / height * size), 0, size - 1);
            var top = Mathf.Clamp(Mathf.CeilToInt((center.y + radius) / height * size), 0, size - 1);
            for (var y = bottom; y <= top; y++) for (var x = left; x <= right; x++)
            {
                var dx = ((x + .5f) / size * width - center.x) / radius;
                var dy = ((y + .5f) / size * height - center.y) / radius;
                var distance = Mathf.Sqrt(dx * dx + dy * dy);
                var coverage = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - distance) * 2.5f)) * strength;
                var amount = (byte)Mathf.RoundToInt(coverage * 255);
                var index = y * size + x;
                var pixel = fallowPixels[index];
                if (channel == 0) pixel.r = Math.Max(pixel.r, amount);
                else if (channel == 1) pixel.g = Math.Max(pixel.g, amount);
                else pixel.b = Math.Max(pixel.b, amount);
                pixel.a = Math.Max(pixel.a, amount);
                fallowPixels[index] = pixel;
            }
        }

        void DrawFallowLandmarks()
        {
            if (!FallowActive) return;
            var width = (float)mapWidth / settings.WorldUnitsPerUnityUnit;
            var height = (float)mapHeight / settings.WorldUnitsPerUnityUnit;
            FallowProp("watermill", .18f, .83f, 2.3f, 1.9f, 5);
            FallowProp("stone", .12f, .60f, .85f, 1.3f, 6);
            for (var i = 0; i < 6; i++)
            {
                FallowProp("stream", .1f + i * .16f, .72f + Mathf.Sin(i * .9f) * .025f, width * .19f, .7f, 2);
                FallowProp("path", .64f + Mathf.Sin(i * .8f) * .09f, .16f + i * .115f, .95f, height * .15f, 3);
            }
            FallowProp("bridge", .73f, .73f, 1.65f, 1.15f, 4);
            FallowProp("furrow", .26f, .36f, 1.7f, 1.15f, 3);
            FallowProp("furrow", .37f, .25f, 1.7f, 1.15f, 3);
            for (var i = 0; i < 5; i++)
            {
                FallowProp("dead-tree", .045f + .018f * (i % 2), .12f + i * .17f, .9f, 1.2f, 6);
                FallowProp("dead-tree", .92f - .035f * (i % 2), .2f + i * .17f, .85f, 1.1f, 6);
            }
        }

        void FallowProp(string name, float x, float y, float width, float height, int layer)
        {
            if (!TryFallowArt("fallow.prop." + name, out var visual)) return;
            var position = new Vector2(x * mapWidth / settings.WorldUnitsPerUnityUnit, y * mapHeight / settings.WorldUnitsPerUnityUnit);
            Draw(visual, layer, position, 0, new Vector2(width, height), opacity: .82f);
        }

        void DisposeFallow()
        {
            if (fallowMask != null)
            {
                Shader.SetGlobalTexture("_FallowMask", Texture2D.blackTexture);
                if (Application.isPlaying) Destroy(fallowMask); else DestroyImmediate(fallowMask);
            }
            fallowMask = null; fallowPixels = null; fallowArt.Clear(); fallowHarvests.Clear();
        }
    }
}
