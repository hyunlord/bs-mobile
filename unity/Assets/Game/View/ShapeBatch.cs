using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public sealed class ShapeBatch : IDisposable
    {
        public const int MaximumInstances = 511;
        private static readonly int ColorId = Shader.PropertyToID("_InstanceColor");
        private static readonly int StyleId = Shader.PropertyToID("_InstanceStyle");
        private readonly Matrix4x4[] matrices = new Matrix4x4[MaximumInstances];
        private readonly Vector4[] colors = new Vector4[MaximumInstances];
        private readonly Vector4[] styles = new Vector4[MaximumInstances];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly Material material;
        private readonly Mesh mesh;
        private int count;
        public int SubmittedInstances { get; private set; }
        public int DrawCalls { get; private set; }

        public ShapeBatch(Mesh mesh, Shader shader, Camera camera, int layer)
        {
            if (mesh == null || shader == null || camera == null) throw new ArgumentNullException(nameof(mesh));
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("The world renderer requires GPU instancing.");
            this.mesh = mesh;
            material = new Material(shader) { name = "World instances " + layer, enableInstancing = true, renderQueue = 3000 + layer, hideFlags = HideFlags.HideAndDontSave };
        }

        public void BeginFrame() { count = 0; SubmittedInstances = 0; DrawCalls = 0; }

        public void Add(Vector2 position, Vector2 scale, float degrees, Color color, float innerRadius = -1)
        {
            if (count == MaximumInstances) Flush();
            matrices[count] = Matrix4x4.TRS(new Vector3(position.x, position.y, 0), Quaternion.Euler(0, 0, degrees), new Vector3(scale.x, scale.y, 1));
            colors[count] = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color; styles[count] = new Vector4(innerRadius, 0, 0, 0); count++;
        }

        public void Flush()
        {
            if (count == 0) return;
            properties.SetVectorArray(ColorId, colors); properties.SetVectorArray(StyleId, styles);
            var parameters = new RenderParams(material)
            {
                // Explicit camera filtering drops instanced draws in the pinned URP2D runtime.
                // The run has one world camera; layer zero participates in its culling mask.
                camera = null, layer = 0, matProps = properties, shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off
            };
            Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, count);
            SubmittedInstances += count; DrawCalls++; count = 0;
        }

        public void Dispose()
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
