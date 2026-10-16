using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    // Review-only P5 budget measurement: the share of enemy sprite pixels covered by own weapon effects in a frame. While a
    // probe is active, every batch flushed for the frame is also drawn into a small mask target (enemy alpha in red, effect
    // alpha × opacity in green, max blend) and read back asynchronously. Off in play; costs nothing when inactive.
    public sealed class OcclusionProbe : IDisposable
    {
        public static OcclusionProbe Active;
        public Func<int> FrameSource;
        // Full-screen tint alpha of the frame being recorded (kept out of the coverage, checked on its own).
        public float Tint;
        public const int Downscale = 2;
        public static bool IsEnemyLayer(int layer) => EffectAlphaCap.IsEnemyLayer(layer);
        public static bool IsEffectLayer(int layer) => EffectAlphaCap.IsEffectLayer(layer);

        public readonly struct Sample
        {
            public readonly int Frame, EnemyPixels;
            public readonly float CoveredPixels, UncappedPixels, Tint;
            public Sample(int frame, int enemy, float covered, float uncapped, float tint) { Frame = frame; EnemyPixels = enemy; CoveredPixels = covered; UncappedPixels = uncapped; Tint = tint; }
            // A uniform normal-blend tint of alpha a scales every luminance difference, enemy against ground included, by 1 − a.
            public float TintContrast => 1 - Tint;
            public float Ratio => EnemyPixels > 0 ? CoveredPixels / EnemyPixels : 0;
            public float UncappedRatio => EnemyPixels > 0 ? UncappedPixels / EnemyPixels : 0;
        }

        static readonly int ChannelId = Shader.PropertyToID("_MaskChannel");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_InstanceColor");
        static readonly int UvId = Shader.PropertyToID("_InstanceUv");
        static readonly int BoostId = Shader.PropertyToID("_AlphaBoost");
        static readonly Vector4 EnemyChannel = new Vector4(1, 0, 0, 1), EffectChannel = new Vector4(0, 1, 0, 1), ThinChannel = new Vector4(0, 0, 1, 1);
        readonly CommandBuffer commands = new CommandBuffer { name = "P5 occlusion mask" };
        readonly Material material;
        readonly List<Sample> samples = new List<Sample>(4096);
        RenderTexture target;
        bool recording;
        int frame;
        public IReadOnlyList<Sample> Samples => samples;
        public int Pending { get; private set; }

        public OcclusionProbe()
        {
            material = Resources.Load<Material>("OcclusionMaskMaterial");
            if (material == null || !material.shader.isSupported) throw new InvalidOperationException("Occlusion mask material is unavailable.");
        }

        public void Begin(Camera camera, int frameIndex)
        {
            var width = Mathf.Max(1, camera.pixelWidth / Downscale); var height = Mathf.Max(1, camera.pixelHeight / Downscale);
            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) target.Release();
                target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32) { name = "P5 occlusion mask", hideFlags = HideFlags.HideAndDontSave };
                target.Create();
            }
            commands.Clear();
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            frame = frameIndex; recording = true;
        }

        public bool Wants(int layer) => recording && (IsEnemyLayer(layer) || IsEffectLayer(layer));

        // Called by a batch at flush with the instances it is about to draw.
        // Wide effects go to green (capped when counted), thin ones to blue (never capped).
        // alphaBoost mirrors a material that multiplies the picture's alpha before clamping (the slash's opaque core).
        public void Record(int layer, Mesh mesh, Matrix4x4[] matrices, int count, Texture texture, Vector4[] colors, Vector4[] uvs, bool wide = true, float alphaBoost = 1)
        {
            if (!Wants(layer) || count <= 0) return;
            var block = new MaterialPropertyBlock();
            block.SetFloat(BoostId, alphaBoost);
            block.SetVector(ChannelId, IsEnemyLayer(layer) ? EnemyChannel : wide ? EffectChannel : ThinChannel);
            block.SetTexture(MainTexId, texture != null ? texture : Texture2D.whiteTexture);
            block.SetVectorArray(ColorId, colors);
            if (uvs != null) block.SetVectorArray(UvId, uvs);   // shapes have no atlas rectangle: full coverage of their mesh
            commands.DrawMeshInstanced(mesh, 0, material, 0, matrices, count, block);
        }

        public void End()
        {
            if (!recording) return;
            recording = false;
            Graphics.ExecuteCommandBuffer(commands);
            var index = frame; var tint = Tint; Pending++;
            AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
            {
                Pending--;
                if (request.hasError) return;
                Count(index, tint, request.GetData<byte>());
            });
        }

        void Count(int index, float tint, NativeArray<byte> data)
        {
            int enemy = 0; float covered = 0, raw = 0;
            for (var i = 0; i + 3 < data.Length; i += 4)
            {
                if (data[i] < 128) continue;
                // The mask holds the strongest wide (green) and thin (blue) effect alpha; wide ones show capped over enemies.
                var wide = data[i + 1] / 255f; var thin = data[i + 2] / 255f;
                enemy++; covered += Mathf.Max(Mathf.Min(wide, EffectAlphaCap.OverEnemies), thin); raw += Mathf.Max(wide, thin);
            }
            samples.Add(new Sample(index, enemy, covered, raw, tint));
        }

        public void Dispose()
        {
            commands.Release();
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (Active == this) Active = null;
        }
    }

    // P5 alpha cap. Enemy sprites mark the stencil buffer where they are drawn. Wide own-effect elements (fills, glows,
    // smoke, bands, shapes) drawn above them are split into two passes: full alpha where no enemy is, at most OverEnemies
    // over an enemy pixel. Thin elements (bolt cores, lines, outlines: batches made with wide = false) are exempt and
    // draw once at full strength. Full-screen tints (storm sky 73, storm screen flash 77) are checked separately; threat
    // markers (72) are not own attacks.
    public static class EffectAlphaCap
    {
        public const float OverEnemies = .5f;
        public const int EnemyMark = 1;
        // Enemies are depth-sorted on EnemyLayer..+19. Own weapon effects that can cover them sit above: attack edges (70),
        // impact particles (71), projectiles and shield discs (74), additive glow (75), shield emblems (76).
        public static bool IsEnemyLayer(int layer) => layer >= GameVisualTokens.EnemyLayer && layer < GameVisualTokens.EnemyLayer + 20;
        public static bool IsEffectLayer(int layer) => layer >= 70 && layer <= 79 && layer != GameVisualTokens.WaveDangerLayer
            && layer != WorldRenderer.StormSkyLayer && layer != WorldRenderer.ScreenFlashLayer || layer == WorldRenderer.OverHeroThinLayer;
        static readonly int RefId = Shader.PropertyToID("_StencilRef"), CompId = Shader.PropertyToID("_StencilComp"), PassId = Shader.PropertyToID("_StencilPass");
        static readonly int CapId = Shader.PropertyToID("_AlphaCap"), ClipId = Shader.PropertyToID("_AlphaClip");

        // Configures a batch material for its layer; returns the capped twin a wide effect batch draws over enemies, else null.
        public static Material Configure(Material material, int layer, bool wide = true)
        {
            if (IsEnemyLayer(layer))
            {
                // Mark only where the enemy is actually painted, not its transparent quad corners.
                Set(material, EnemyMark, CompareFunction.Always, StencilOp.Replace, 1, .05f);
                return null;
            }
            if (!IsEffectLayer(layer) || !wide) return null;
            var capped = new Material(material) { name = material.name + " over enemies", hideFlags = HideFlags.HideAndDontSave };
            Set(material, 0, CompareFunction.Equal, StencilOp.Keep, 1, 0);
            Set(capped, EnemyMark, CompareFunction.Equal, StencilOp.Keep, OverEnemies, 0);
            return capped;
        }

        static void Set(Material material, int reference, CompareFunction compare, StencilOp pass, float cap, float clip)
        {
            material.SetFloat(RefId, reference); material.SetFloat(CompId, (float)compare); material.SetFloat(PassId, (float)pass);
            material.SetFloat(CapId, cap); material.SetFloat(ClipId, clip);
        }
    }
}
