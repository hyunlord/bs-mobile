using System;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;

namespace Game.View
{
    // One live hero only. Historical B remains an explicit comparison; enemies use baked SpriteBatch.
    // Every cutout is a genuine weighted Sprite mesh, deformed by Unity's SpriteSkin system.
    public sealed class HeroLiveRig : IDisposable
    {
        static readonly ProfilerMarker PoseMarker = new ProfilerMarker("SowSiege.View.HeroLiveRigPose");
        readonly HeroLiveRigData data;
        readonly GameObject root;
        readonly Transform[] bones;
        readonly int[] parents;
        readonly bool[] lowerBody;
        readonly SpriteRenderer[] renderers;
        readonly SpriteSkin[] skins;
        readonly Sprite[] sprites;
        readonly Material material;
        Material flashMaterial;
        bool flashing;
        readonly HeroLiveJoint[] pose;
        readonly HeroLiveClip swing, evolvedSwing, hitClip;
        readonly Texture2D partsTexture, swordTexture;
        readonly int farKnee, nearKnee, nearShoulder;
        public int SpriteSkinCount => renderers.Length;
        public int BoneCount => bones.Length;
        public int DeformedSkinCount
        {
            get { var count = 0; for (var i = 0; i < skins.Length; i++) if (skins[i].HasCurrentDeformedVertices()) count++; return count; }
        }
        // Explicit diagnostic readback only; never called by Present or the normal drawing path.
        public Vector3 SampleDeformedVertex(int partIndex)
        {
            foreach (var vertex in skins[partIndex].GetDeformedVertexPositionData()) return vertex;
            throw new InvalidOperationException("The hero skin has no deformed vertices yet.");
        }
        public long TextureBytes { get; }
        public double LastPoseMilliseconds { get; private set; }

        public HeroLiveRig(Transform owner, string resourceRoot = "HeroMotion")
        {
            data = HeroLiveRigData.Load(resourceRoot);
            farKnee = data.JointIndex("farKnee"); nearKnee = data.JointIndex("nearKnee");
            nearShoulder = data.JointIndex("nearShoulder");
            swing = data.Clip("swing"); evolvedSwing = data.Clip("evolved-swing"); hitClip = data.Clip("hit");
            partsTexture = Resources.Load<Texture2D>(string.IsNullOrEmpty(data.partsResource) ? resourceRoot + "/parts" : data.partsResource);
            swordTexture = Resources.Load<Texture2D>(string.IsNullOrEmpty(data.swordResource) ? resourceRoot + "/sword" : data.swordResource);
            if (partsTexture == null || swordTexture == null) throw new InvalidOperationException("Hero source cutouts are missing.");
            TextureBytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(partsTexture) + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(swordTexture);
            var template = Resources.Load<Material>("HeroMotion/live-material");
            if (template == null || template.shader == null || !template.shader.isSupported)
                throw new InvalidOperationException("Retained URP SpriteSkin material is unavailable or unsupported.");
            material = new Material(template) { name = "Hero live comparison", renderQueue = 3000 + GameVisualTokens.LordLayer, hideFlags = HideFlags.DontSave };
            root = new GameObject("Hero live comparison"); root.SetActive(false); root.transform.SetParent(owner, false);
            bones = new Transform[data.jointNames.Length]; parents = new int[bones.Length]; lowerBody = new bool[bones.Length]; pose = new HeroLiveJoint[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                parents[i] = i == 0 ? -1 : data.JointIndex(data.bones[i - 1].parent);
                var bone = new GameObject(data.jointNames[i]); bones[i] = bone.transform;
                bones[i].SetParent(parents[i] < 0 ? root.transform : bones[parents[i]], false);
                lowerBody[i] = data.jointNames[i] == "farHip" || data.jointNames[i] == "farKnee" || data.jointNames[i] == "nearHip" || data.jointNames[i] == "nearKnee";
                pose[i] = data.gaitFrames[12].joints[i];
            }
            ApplyBones();
            renderers = new SpriteRenderer[data.sprites.Length]; skins = new SpriteSkin[renderers.Length]; sprites = new Sprite[renderers.Length];
            for (var i = 0; i < sprites.Length; i++) CreatePart(i);
        }

        public void Present(Vector2 position, Vector2 signedSize, HeroGaitSample gait, bool walking,
            float attackProgress, bool evolved, float hitProgress, Color color, float deathProgress = -1, float levelProgress = -1, float flash = 0)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            using (PoseMarker.Auto())
            {
                if (!root.activeSelf) root.SetActive(true);
                walking = walking && deathProgress < 0;
                var gaitPosition = walking ? Mathf.Repeat(gait.Phase, 1) * 12 : 12;
                var first = Mathf.FloorToInt(gaitPosition);
                var second = walking ? (first + 1) % 12 : 12;
                var blend = gaitPosition - first;
                var action = hitProgress >= 0 ? hitClip : attackProgress >= 0 ? (evolved ? evolvedSwing : swing) : null;
                var actionPosition = action == null ? 0 : Mathf.Clamp01(hitProgress >= 0 ? hitProgress : attackProgress) * (action.frameCount - 1);
                var actionFirst = Mathf.FloorToInt(actionPosition);
                for (var i = 0; i < pose.Length; i++)
                {
                    if (action != null && !lowerBody[i] && i != 0)
                        pose[i] = HeroLiveRigData.Blend(data.frames[action.firstFrame + actionFirst].joints[i],
                            data.frames[action.firstFrame + Mathf.Min(actionFirst + 1, action.frameCount - 1)].joints[i], actionPosition - actionFirst);
                    else pose[i] = HeroLiveRigData.Blend(data.gaitFrames[first].joints[i], data.gaitFrames[second].joints[i], blend);
                }
                // Match the reference's continuous sole pinning, without moving simulation coordinates.
                CorrectFoot(farKnee, gait.FarCorrection, first, second, blend, false);
                CorrectFoot(nearKnee, gait.NearCorrection, first, second, blend, true);
                if (deathProgress >= 0) ApplyDeathPose(deathProgress);
                else if (levelProgress >= 0 && action == null) ApplyLevelPose(levelProgress);
                ApplyBones();
                root.transform.position = position;
                root.transform.localScale = new Vector3(signedSize.x / data.canvasSize, signedSize.y / data.canvasSize, 1);
                for (var i = 0; i < renderers.Length; i++) renderers[i].color = color;
                ApplyFlash(flash > 0);
            }
            LastPoseMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;
        }

        void ApplyDeathPose(float progress)
        {
            // Presentation only: settle around the planted sole without changing the Core actor.
            var weight = Mathf.SmoothStep(0, 1, Mathf.Clamp01(progress));
            var angle = -72 * Mathf.Deg2Rad * weight;
            var pivot = new Vector2(data.root.x, data.groundY);
            for (var i = 0; i < pose.Length; i++) RotateJoint(i, pivot, angle);
        }

        void ApplyLevelPose(float progress)
        {
            var weight = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            var pivot = new Vector2(pose[nearShoulder].x, pose[nearShoulder].y);
            var angle = -70 * Mathf.Deg2Rad * weight;
            for (var i = 0; i < pose.Length; i++)
            {
                var ancestor = i;
                while (ancestor >= 0 && ancestor != nearShoulder) ancestor = parents[ancestor];
                if (ancestor == nearShoulder) RotateJoint(i, pivot, angle);
            }
        }

        void RotateJoint(int index, Vector2 pivot, float angle)
        {
            var x = pose[index].x - pivot.x; var y = pose[index].y - pivot.y;
            var cosine = Mathf.Cos(angle); var sine = Mathf.Sin(angle);
            pose[index].x = pivot.x + x * cosine - y * sine;
            pose[index].y = pivot.y + x * sine + y * cosine;
            pose[index].angleRadians += angle;
        }

        void CorrectFoot(int index, Vector2 correction, int first, int second, float blend, bool near)
        {
            if (first >= 12) return;
            // The discrete reference sole moves between authored poses; remove that delta as live joints interpolate.
            var interpolatedSole = Vector2.Lerp(AuthoredSole(first, near), AuthoredSole(second, near), blend);
            var delta = correction + HeroGaitState.Sole(first, near) - interpolatedSole;
            pose[index].x += delta.x * data.canvasSize;
            pose[index].y -= delta.y * data.canvasSize;
        }

        Vector2 AuthoredSole(int frame, bool near)
        {
            var soles = near ? data.nearSoles : data.farSoles;
            if (soles == null || soles.Length <= frame) return HeroGaitState.Sole(frame, near);
            return new Vector2(soles[frame].x / data.canvasSize, 1 - soles[frame].y / data.canvasSize);
        }

        void ApplyBones()
        {
            for (var i = 0; i < bones.Length; i++)
            {
                var point = new Vector3(pose[i].x - data.root.x, data.groundY - pose[i].y, 0);
                var angle = -pose[i].angleRadians * Mathf.Rad2Deg;
                if (parents[i] < 0) { bones[i].localPosition = point; bones[i].localRotation = Quaternion.Euler(0, 0, angle); }
                else
                {
                    var parent = pose[parents[i]];
                    var offset = new Vector3(pose[i].x - parent.x, parent.y - pose[i].y, 0);
                    bones[i].localPosition = Quaternion.Euler(0, 0, parent.angleRadians * Mathf.Rad2Deg) * offset;
                    bones[i].localRotation = Quaternion.Euler(0, 0, angle + parent.angleRadians * Mathf.Rad2Deg);
                }
            }
        }

        void CreatePart(int index)
        {
            var part = data.sprites[index]; var texture = part.source == "cleanSword" ? swordTexture : partsTexture;
            var rect = part.rect; var boneIndex = data.JointIndex(part.bone);
            var sprite = Sprite.Create(texture, new Rect(rect[0], texture.height - rect[1] - rect[3], rect[2], rect[3]), Vector2.zero, 1, 0, SpriteMeshType.FullRect);
            sprite.name = "Hero live " + part.bone; sprites[index] = sprite;
            var vertices = new NativeArray<Vector3>(4, Allocator.Temp);
            var uv = new NativeArray<Vector2>(4, Allocator.Temp);
            var weights = new NativeArray<BoneWeight>(4, Allocator.Temp);
            var triangles = new NativeArray<ushort>(new ushort[] { 0, 1, 2, 0, 2, 3 }, Allocator.Temp);
            var bind = new NativeArray<Matrix4x4>(bones.Length, Allocator.Temp);
            try
            {
                var definitions = new SpriteBone[bones.Length];
                for (var i = 0; i < bones.Length; i++)
                {
                    bind[i] = bones[i].worldToLocalMatrix * root.transform.localToWorldMatrix;
                    definitions[i] = new SpriteBone { name = data.jointNames[i], parentId = parents[i], position = bones[i].localPosition, rotation = bones[i].localRotation, length = 1 };
                }
                var joint = pose[boneIndex]; var rotation = Quaternion.Euler(0, 0, -(joint.angleRadians * Mathf.Rad2Deg + part.angle));
                var scaleX = part.scaleX == 0 ? part.scale : part.scaleX; var scaleY = part.scaleY == 0 ? part.scale : part.scaleY;
                for (var i = 0; i < 4; i++)
                {
                    var x = rect[0] + (i == 1 || i == 2 ? rect[2] : 0);
                    var y = rect[1] + (i >= 2 ? 0 : rect[3]);
                    vertices[i] = new Vector3(joint.x - data.root.x, data.groundY - joint.y, 0) + rotation * new Vector3((x - part.anchor[0]) * scaleX, (part.anchor[1] - y) * scaleY, 0);
                    uv[i] = new Vector2(x / texture.width, 1 - y / texture.height);
                    weights[i] = new BoneWeight { boneIndex0 = boneIndex, weight0 = 1 };
                }
                sprite.SetVertexCount(4);
                sprite.SetVertexAttribute(VertexAttribute.Position, vertices);
                sprite.SetVertexAttribute(VertexAttribute.TexCoord0, uv);
                sprite.SetVertexAttribute(VertexAttribute.BlendWeight, weights);
                sprite.SetIndices(triangles); sprite.SetBindPoses(bind); sprite.SetBones(definitions);
                var partObject = new GameObject("Skin " + part.bone); partObject.transform.SetParent(root.transform, false);
                var renderer = partObject.AddComponent<SpriteRenderer>(); renderers[index] = renderer;
                renderer.sprite = sprite; renderer.sharedMaterial = material; renderer.sortingOrder = index;
                var skin = partObject.AddComponent<SpriteSkin>(); skins[index] = skin; skin.autoRebind = false; skin.alwaysUpdate = true;
                skin.SetRootBone(bones[0]); skin.SetBoneTransforms(bones);
            }
            finally
            {
                vertices.Dispose(); uv.Dispose(); weights.Dispose(); triangles.Dispose(); bind.Dispose();
            }
        }

        public bool Flashing => flashing;

        // A tint can only darken the cutouts, so the white hit flash swaps in a white-silhouette material instead.
        void ApplyFlash(bool active)
        {
            if (active == flashing) return;
            if (active && flashMaterial == null)
            {
                var shader = Resources.Load<Shader>("HeroLiveFlash");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Hero hit flash shader is unavailable or unsupported.");
                flashMaterial = new Material(shader) { name = "Hero live hit flash", renderQueue = material.renderQueue, hideFlags = HideFlags.DontSave };
            }
            flashing = active;
            for (var i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = active ? flashMaterial : material;
        }

        public void Hide() { if (root.activeSelf) root.SetActive(false); }
        public void Dispose()
        {
            UnityEngine.Object.Destroy(root);
            for (var i = 0; i < sprites.Length; i++) UnityEngine.Object.Destroy(sprites[i]);
            UnityEngine.Object.Destroy(material);
            if (flashMaterial != null) UnityEngine.Object.Destroy(flashMaterial);
        }
    }
}
