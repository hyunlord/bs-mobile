using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    // Review-only alpha landmarks: neither the transparent quad nor cape/sword defines body height.
    public sealed class HeroHeadFootMeasure
    {
        readonly Part[] parts;
        readonly struct Part
        {
            public readonly int Joint;
            public readonly Vector2[] Points;
            public Part(int joint, Vector2[] points) { Joint = joint; Points = points; }
        }
        public HeroHeadFootMeasure(HeroLiveRigData data, Texture2D texture)
        {
            var pixels = ReadPixels(texture);
            var selected = new List<Part>(3);
            foreach (var part in data.sprites)
            {
                if (part.bone != "head" && part.bone != "farKnee" && part.bone != "nearKnee") continue;
                var points = new List<Vector2>();
                var rect = part.rect;
                var angle = part.angle * Mathf.Deg2Rad;
                var cosine = Mathf.Cos(angle); var sine = Mathf.Sin(angle);
                var sx = part.scaleX == 0 ? part.scale : part.scaleX;
                var sy = part.scaleY == 0 ? part.scale : part.scaleY;
                for (var y = (int)rect[1]; y < rect[1] + rect[3]; y++)
                {
                    var left = int.MaxValue; var right = -1;
                    for (var x = (int)rect[0]; x < rect[0] + rect[2]; x++)
                        if (pixels[(texture.height - 1 - y) * texture.width + x].a >= 128)
                        { left = Math.Min(left, x); right = Math.Max(right, x); }
                    if (right < 0) continue;
                    // Row end squares suffice for extrema under any rigid bone rotation.
                    AddPoint(points, left, y, part, sx, sy, cosine, sine);
                    AddPoint(points, right + 1, y, part, sx, sy, cosine, sine);
                    AddPoint(points, left, y + 1, part, sx, sy, cosine, sine);
                    AddPoint(points, right + 1, y + 1, part, sx, sy, cosine, sine);
                }
                if (points.Count == 0) throw new InvalidOperationException("No opaque head/foot landmark: " + part.bone);
                selected.Add(new Part(data.JointIndex(part.bone), points.ToArray()));
            }
            if (selected.Count != 3) throw new InvalidOperationException("Cutout height needs one head and both boots.");
            parts = selected.ToArray();
        }
        static void AddPoint(List<Vector2> points, float x, float y, HeroLivePart part, float sx, float sy, float cosine, float sine)
        {
            var dx = (x - part.anchor[0]) * sx; var dy = (y - part.anchor[1]) * sy;
            points.Add(new Vector2(dx * cosine - dy * sine, dx * sine + dy * cosine));
        }
        public Vector2 Measure(HeroLiveJoint[] pose)
        {
            var top = float.PositiveInfinity; var bottom = float.NegativeInfinity;
            foreach (var part in parts)
            {
                var joint = pose[part.Joint];
                var sine = Mathf.Sin(joint.angleRadians); var cosine = Mathf.Cos(joint.angleRadians);
                foreach (var point in part.Points)
                {
                    var y = joint.y + point.x * sine + point.y * cosine;
                    top = Mathf.Min(top, y); bottom = Mathf.Max(bottom, y);
                }
            }
            return new Vector2(top, bottom);
        }
        static Color32[] ReadPixels(Texture2D texture)
        {
            if (texture.isReadable) return texture.GetPixels32();
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(texture, target); RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
