using System;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif

namespace Game.View
{
    public sealed class SpriteBatch : IDisposable
    {
        public const int MaximumInstances = 511;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly ProfilerMarker SubmitMarker = new ProfilerMarker("SowSiege.View.SpriteBatchSubmit");
        static readonly ProfilerMarker FirstUseMarker = new ProfilerMarker("SowSiege.View.SpriteBatchFirstUse");
        static readonly ProfilerMarker MeshUploadMarker = new ProfilerMarker("SowSiege.View.SpriteBatchMeshUpload");
        static readonly ProfilerMarker PropertyUploadMarker = new ProfilerMarker("SowSiege.View.SpriteBatchPropertyUpload");
        static readonly ProfilerMarker CapacityFlushMarker = new ProfilerMarker("SowSiege.View.SpriteBatchCapacityFlush");
        static readonly ProfilerMarker DrawMarker = new ProfilerMarker("SowSiege.View.SpriteBatchDraw");
#endif
        static readonly int ColorId = Shader.PropertyToID("_InstanceColor");
        static readonly int UvId = Shader.PropertyToID("_InstanceUv");
        static readonly int FlashId = Shader.PropertyToID("_InstanceFlash");
        static readonly int RigUpperTextureId = Shader.PropertyToID("_RigUpperTex");
        static readonly int RigUseUpperTextureId = Shader.PropertyToID("_RigUseUpperTex");
        static readonly int RigFrameId = Shader.PropertyToID("_RigFrame");
        static readonly int RigUpperFrameId = Shader.PropertyToID("_RigUpperFrame");
        static readonly int RigFarId = Shader.PropertyToID("_RigFarDelta");
        static readonly int RigNearId = Shader.PropertyToID("_RigNearDelta");
        static readonly int RigWarpId = Shader.PropertyToID("_RigWarp");
        static readonly int RigCanvasId = Shader.PropertyToID("_RigCanvas");
        readonly Matrix4x4[] matrices = new Matrix4x4[MaximumInstances];
        readonly Vector4[] colors = new Vector4[MaximumInstances];
        readonly Vector4[] uvs = new Vector4[MaximumInstances];
        readonly Vector4[] flashes = new Vector4[MaximumInstances];
        readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        readonly Material material, overEnemies;
        readonly Mesh mesh;
        readonly Texture2D texture;
        readonly int layer;
        readonly bool wide;
        int count;
        bool linearColorSpace;
        bool heroRigBoundsConfigured;
        // Enemy runs share one render queue. Only their aggregate sorting bounds move in depth; sprite geometry stays at z=0.
        public float? SortingDepth { get; set; }
        public int SubmittedInstances { get; private set; }
        public int DrawCalls { get; private set; }

        // wide = false tags thin elements (bolt cores, lines, outlines): exempt from the P5 alpha cap over enemies.
        public SpriteBatch(Texture2D texture, Shader shader, Camera camera, int layer, bool wide = true)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var firstUseSample = FirstUseMarker.Auto();
#endif
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (!SystemInfo.supportsInstancing) throw new InvalidOperationException("The world renderer requires GPU instancing.");
            this.texture = texture; this.layer = layer; this.wide = wide;
            material = new Material(shader) { name = "World sprites " + layer, mainTexture = texture, enableInstancing = true, renderQueue = 3000 + layer, hideFlags = HideFlags.HideAndDontSave };
            overEnemies = EffectAlphaCap.Configure(material, layer, wide);
            mesh = new Mesh { name = "Instanced sprite quad", hideFlags = HideFlags.HideAndDontSave };
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (MeshUploadMarker.Auto())
#endif
            {
                mesh.vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds();
            }
            linearColorSpace=QualitySettings.activeColorSpace==ColorSpace.Linear;
        }
        public void BeginFrame() { count = 0; SubmittedInstances = 0; DrawCalls = 0; linearColorSpace=QualitySettings.activeColorSpace==ColorSpace.Linear; }
        public void SetHeroRigPose(Vector4 frame, Vector4 upperFrame, Vector2 farCorrection, Vector2 nearCorrection, Vector4 warp, Texture2D upperTexture = null)
        {
            if(count!=0)throw new InvalidOperationException("The hero rig owns a single-instance batch.");
            properties.SetFloat(RigUseUpperTextureId, upperTexture == null ? 0 : 1);
            if (upperTexture != null) properties.SetTexture(RigUpperTextureId, upperTexture);
            properties.SetVector(RigFrameId,frame);
            properties.SetVector(RigUpperFrameId,upperFrame);
            properties.SetVector(RigFarId,new Vector4(farCorrection.x,farCorrection.y,0,0));
            properties.SetVector(RigNearId,new Vector4(nearCorrection.x,nearCorrection.y,0,0));
            properties.SetVector(RigWarpId,warp);
            properties.SetVector(RigCanvasId,new Vector4(HeroGaitState.CanvasMinimumY,1-HeroGaitState.CanvasMinimumY,0,0));
            if(!heroRigBoundsConfigured)
            {
                mesh.bounds=new Bounds(new Vector3(.5f,(1+HeroGaitState.CanvasMinimumY)*.5f,0),new Vector3(1,1-HeroGaitState.CanvasMinimumY,0));
                heroRigBoundsConfigured=true;
            }
        }
        public void Add(Vector2 position, Vector2 size, Vector2 pivot, Rect uv, float degrees, Color color, float flash = 0, float edgeTexels = 0, float upperOpacity = 1)
        {
            if (count == MaximumInstances)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                using (CapacityFlushMarker.Auto()) Flush();
#else
                Flush();
#endif
            }
            var rotation = Quaternion.Euler(0, 0, degrees);
            var offset = rotation * new Vector3(-pivot.x * size.x, -pivot.y * size.y, 0);
            matrices[count] = Matrix4x4.TRS(new Vector3(position.x, position.y, 0) + offset, rotation, new Vector3(size.x, size.y, 1));
            colors[count] = linearColorSpace ? color.linear : color;
            uvs[count] = new Vector4(uv.x, uv.y, uv.width, uv.height);
            flashes[count] = new Vector4(Mathf.Clamp01(flash), Mathf.Max(0, edgeTexels), Mathf.Clamp01(upperOpacity), edgeTexels>0?GameVisualTokens.WaveEdgePixels:0); count++;
        }
        public void Flush()
        {
            if (count == 0) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var submitSample = SubmitMarker.Auto();
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (PropertyUploadMarker.Auto())
#endif
            {
                properties.SetVectorArray(ColorId, colors); properties.SetVectorArray(UvId, uvs); properties.SetVectorArray(FlashId, flashes);
            }
            var parameters = new RenderParams(material)
            {
                // Explicit camera filtering drops instanced draws in the pinned URP2D runtime.
                camera = null, layer = 0, matProps = properties, shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false, lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off
            };
            if (SortingDepth.HasValue)
            {
                var bounds = new Bounds(matrices[0].MultiplyPoint3x4(Vector3.zero), Vector3.zero);
                for (var i = 0; i < count; i++)
                {
                    bounds.Encapsulate(matrices[i].MultiplyPoint3x4(Vector3.zero));
                    bounds.Encapsulate(matrices[i].MultiplyPoint3x4(Vector3.right));
                    bounds.Encapsulate(matrices[i].MultiplyPoint3x4(Vector3.up));
                    bounds.Encapsulate(matrices[i].MultiplyPoint3x4(new Vector3(1, 1, 0)));
                }
                var center = bounds.center; center.z = SortingDepth.Value; bounds.center = center;
                var extents = bounds.extents; extents.z = Mathf.Abs(center.z) + .01f; bounds.extents = extents;
                parameters.worldBounds = bounds;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using (DrawMarker.Auto())
#endif
            {
                OcclusionProbe.Active?.Record(layer, mesh, matrices, count, texture, colors, uvs, wide);
                Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, count);
                if (overEnemies != null)
                {
                    parameters.material = overEnemies;
                    Graphics.RenderMeshInstanced(parameters, mesh, 0, matrices, count); DrawCalls++;
                }
            }
            SubmittedInstances += count; DrawCalls++; count = 0;
        }
#if UNITY_EDITOR
        public void MoveHeroRigUniformsToMaterialForTesting()
        {
            if (count != 0 || DrawCalls != 0) throw new InvalidOperationException("Uniform inspection requires an unsubmitted batch.");
            foreach (var id in new[] { RigFrameId, RigUpperFrameId, RigFarId, RigNearId, RigWarpId, RigCanvasId })
                if (properties.HasVector(id)) material.SetVector(id, properties.GetVector(id));
            properties.Clear();
        }

        public void DrawSingleInstanceForTesting(CommandBuffer commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (count != 0 || DrawCalls != 1 || SubmittedInstances != 1)
                throw new InvalidOperationException("GPU inspection requires one completed single-instance draw.");
            commands.DrawMeshInstanced(mesh, 0, material, 0, matrices, 1, properties);
        }

        internal Vector4[] CaptureSingleDrawUvsForTesting()
        {
            if(DrawCalls>1||count!=0)throw new InvalidOperationException("UV inspection requires a completed single draw.");
            if(SubmittedInstances==0)return Array.Empty<Vector4>();
            var result=new Vector4[SubmittedInstances];Array.Copy(uvs,result,result.Length);return result;
        }
#endif
        public void Dispose()
        {
            if (Application.isPlaying) { UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(mesh); if (overEnemies != null) UnityEngine.Object.Destroy(overEnemies); }
            else { UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(mesh); if (overEnemies != null) UnityEngine.Object.DestroyImmediate(overEnemies); }
        }
    }
}
