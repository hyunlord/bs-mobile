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
        public const int Downscale = 2;
        // Enemies are depth-sorted on EnemyLayer..+19. Own weapon effects that can cover them sit above: attack edges (70),
        // impact particles (71), storm sky (73), projectiles and shield discs (74), additive glow (75), shield emblems (76).
        // Threat markers (72), the hero (80) and UI warnings (90) are not own attacks.
        public static bool IsEnemyLayer(int layer) => layer >= GameVisualTokens.EnemyLayer && layer < GameVisualTokens.EnemyLayer + 20;
        public static bool IsEffectLayer(int layer) => layer >= 70 && layer <= 79 && layer != 72;

        public readonly struct Sample
        {
            public readonly int Frame, EnemyPixels;
            public readonly float CoveredPixels;
            public Sample(int frame, int enemy, float covered) { Frame = frame; EnemyPixels = enemy; CoveredPixels = covered; }
            public float Ratio => EnemyPixels > 0 ? CoveredPixels / EnemyPixels : 0;
        }

        static readonly int ChannelId = Shader.PropertyToID("_MaskChannel");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int ColorId = Shader.PropertyToID("_InstanceColor");
        static readonly int UvId = Shader.PropertyToID("_InstanceUv");
        static readonly Vector4 EnemyChannel = new Vector4(1, 0, 0, 1), EffectChannel = new Vector4(0, 1, 0, 1);
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
        public void Record(int layer, Mesh mesh, Matrix4x4[] matrices, int count, Texture texture, Vector4[] colors, Vector4[] uvs)
        {
            if (!Wants(layer) || count <= 0) return;
            var block = new MaterialPropertyBlock();
            block.SetVector(ChannelId, IsEnemyLayer(layer) ? EnemyChannel : EffectChannel);
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
            var index = frame; Pending++;
            AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
            {
                Pending--;
                if (request.hasError) return;
                Count(index, request.GetData<byte>());
            });
        }

        void Count(int index, NativeArray<byte> data)
        {
            int enemy = 0; float covered = 0;
            for (var i = 0; i + 3 < data.Length; i += 4)
            {
                if (data[i] < 128) continue;
                enemy++; covered += data[i + 1] / 255f;
            }
            samples.Add(new Sample(index, enemy, covered));
        }

        public void Dispose()
        {
            commands.Release();
            if (target != null) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (Active == this) Active = null;
        }
    }
}
