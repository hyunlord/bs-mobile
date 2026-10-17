using System;
using UnityEngine;

namespace Game.View
{
    // Shared authoring export: baked pixels and live joints come from the same solved poses.
    [Serializable] public sealed class HeroLiveRigData
    {
        public int schemaVersion, canvasSize;
        public string partsResource, swordResource;
        public float groundY;
        public HeroLiveRoot root;
        public string[] jointNames;
        public HeroLiveBone[] bones;
        public HeroLivePart[] sprites;
        public HeroLiveClip[] clips;
        public HeroLiveFrame[] frames, gaitFrames;
        public HeroLiveRoot[] farSoles, nearSoles;

        public static HeroLiveRigData Load(string resourceRoot = "HeroMotion")
        {
            var resource = Resources.Load<TextAsset>(resourceRoot + "/motion-runtime");
            if (resource == null) throw new InvalidOperationException("Hero motion authoring export is missing.");
            var data = JsonUtility.FromJson<HeroLiveRigData>(resource.text);
            if (data == null || data.schemaVersion != 1 || data.canvasSize != 512 || data.gaitFrames.Length != 13)
                throw new InvalidOperationException("Unsupported hero motion export.");
            return data;
        }

        public int JointIndex(string name)
        {
            for (var i = 0; i < jointNames.Length; i++) if (jointNames[i] == name) return i;
            throw new InvalidOperationException("Missing authored joint: " + name);
        }
        public HeroLiveClip Clip(string id)
        {
            for (var i = 0; i < clips.Length; i++) if (clips[i].id == id) return clips[i];
            throw new InvalidOperationException("Missing authored clip: " + id);
        }
        public static HeroLiveJoint Blend(HeroLiveJoint a, HeroLiveJoint b, float t)
            => new HeroLiveJoint { x = Mathf.Lerp(a.x, b.x, t), y = Mathf.Lerp(a.y, b.y, t),
                angleRadians = (a.angleRadians * Mathf.Rad2Deg + Mathf.DeltaAngle(a.angleRadians * Mathf.Rad2Deg, b.angleRadians * Mathf.Rad2Deg) * t) * Mathf.Deg2Rad };
    }
    [Serializable] public struct HeroLiveRoot { public float x, y; }
    [Serializable] public sealed class HeroLiveBone { public string id, parent; public float[] offset; public float angle; }
    [Serializable] public sealed class HeroLivePart
    {
        public string bone, source;
        public float[] rect, anchor;
        public float scale, scaleX, scaleY, angle;
    }
    [Serializable] public sealed class HeroLiveClip { public string id; public float durationSeconds; public int firstFrame, frameCount; }
    [Serializable] public sealed class HeroLiveFrame { public string id; public HeroLiveJoint[] joints; }
    [Serializable] public struct HeroLiveJoint { public float x, y, angleRadians; }
}
