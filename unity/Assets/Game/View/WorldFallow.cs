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
        Shader fallowGroundShader;
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
                fallowGroundShader = Resources.Load<Shader>("FallowGroundSprite");
                if (fallowShader == null || !fallowShader.isSupported || fallowGroundShader == null || !fallowGroundShader.isSupported)
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
            for (var workIndex = 0; workIndex < wave.Work.Count; workIndex++)
            {
                var work = wave.Work[workIndex];
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
            FallowProp("fallow.prop.watermill", .35f, .73f, 2.4f, 2.1f, 5);
            FallowProp("fallow.prop.stone", .30f, .59f, .8f, 1.3f, 6);
            for (var i = 0; i < 10; i++)
            {
                FallowProp("fallow.prop.stream", .19f + i * .075f, .68f + Mathf.Sin(i * .5f) * .013f, width * .2f, .85f, 2);
                FallowProp("fallow.prop.path", .64f + Mathf.Sin(i * .4f) * .05f, .18f + i * .057f, 1.5f, height * .14f, 3, -12);
            }
            FallowProp("fallow.prop.bridge", .70f, .70f, 1.85f, 1.35f, 4);
            FallowProp("fallow.prop.furrow", .33f, .36f, 1.5f, 1.0f, 3);
            FallowProp("fallow.prop.furrow", .39f, .29f, 1.5f, 1.0f, 3);
            for (var i = 0; i < 4; i++)
            {
                FallowProp("fallow.prop.dead-tree", .20f + .018f * (i % 2), .23f + i * .15f, 1.0f, 1.5f, 6);
                FallowProp("fallow.prop.dead-tree", .78f - .035f * (i % 2), .28f + i * .14f, .95f, 1.4f, 6);
            }
            FallowProp("fallow.prop.dead-tree", .46f, .85f, .72f, 1.15f, 6, opacity: .72f);
            FallowProp("fallow.prop.dead-tree", .61f, .79f, .66f, 1.08f, 6, opacity: .72f);
            FallowProp("fallow.prop.dead-tree", .70f, .88f, .84f, 1.32f, 6, opacity: .68f);
            FallowProp("fallow.prop.stone", .56f, .85f, .45f, .73f, 6, opacity: .68f);
            FallowProp("fallow.prop.stone", .81f, .75f, .42f, .65f, 6, opacity: .68f);
        }

        void FallowProp(string name, float x, float y, float width, float height, int layer, float degrees = 0, float opacity = 1)
        {
            if (!TryFallowArt(name, out var visual)) return;
            var position = new Vector2(x * mapWidth / settings.WorldUnitsPerUnityUnit, y * mapHeight / settings.WorldUnitsPerUnityUnit);
            var uv = visual.UvRects[0];
            var pixels = new Vector2(Mathf.Abs(uv.width) * visual.Texture.width, Mathf.Abs(uv.height) * visual.Texture.height);
            var fit = Mathf.Min(width / pixels.x, height / pixels.y);
            Draw(visual, layer, position, 0, pixels * fit, degrees, opacity: opacity * (name == "fallow.prop.path" ? .6f : 1));
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
