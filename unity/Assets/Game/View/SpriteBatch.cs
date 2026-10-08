using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.View
{
    public sealed class SpriteBatch : IDisposable
    {
        public const int MaximumInstances = 511;
        static readonly int ColorId = Shader.PropertyToID("_InstanceColor");
        static readonly int UvId = Shader.PropertyToID("_InstanceUv");
        static readonly int FlashId = Shader.PropertyToID("_InstanceFlash");
        readonly Matrix4x4[] matrices = new Matrix4x4[MaximumInstances];
        readonly Vector4[] colors = new Vector4[MaximumInstances];
        readonly Vector4[] uvs = new Vector4[MaximumInstances];
        readonly Vector4[] flashes = new Vector4[MaximumInstances];
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly Material material;
        readonly Mesh mesh;
        int count;
        public int SubmittedInstances { get; private set; }
        public int DrawCalls { get; private set; }

        public SpriteBatch(Texture2D texture, Shader shader, Camera camera, int layer)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("The world renderer requires GPU instancing.");
            material = new Material(shader) { name = "World sprites " + layer, mainTexture = texture, enableInstancing = true, renderQueue = 3000 + layer, hideFlags = HideFlags.HideAndDontSave };
            mesh = new Mesh { name = "Instanced sprite quad", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds();
        }
        public void BeginFrame() { count = 0; SubmittedInstances = 0; DrawCalls = 0; }
        public void Add(Vector2 position, Vector2 size, Vector2 pivot, Rect uv, float degrees, Color color, float flash = 0)
        {
            if (count == MaximumInstances) Flush();
            var rotation = Quaternion.Euler(0, 0, degrees);
            var offset = rotation * new Vector3(-pivot.x * size.x, -pivot.y * size.y, 0);
            matrices[count] = Matrix4x4.TRS(new Vector3(position.x, position.y, 0) + offset, rotation, new Vector3(size.x, size.y, 1));
            colors[count] = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            uvs[count] = new Vector4(uv.x, uv.y, uv.width, uv.height);
            flashes[count] = new Vector4(Mathf.Clamp01(flash), 0, 0, 0); count++;
        }
        public void Flush()
        {
            if (count == 0) return;
            properties.SetVectorArray(ColorId, colors); properties.SetVectorArray(UvId, uvs); properties.SetVectorArray(FlashId, flashes);
            var parameters = new RenderParams(material)
            {
                // Explicit camera filtering drops instanced draws in the pinned URP2D runtime.
                camera = null, layer = 0, matProps = properties, shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off
            };
            Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, count);
            SubmittedInstances += count; DrawCalls++; count = 0;
        }
        public void Dispose()
        {
            if (Application.isPlaying) { UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(mesh); }
            else { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
