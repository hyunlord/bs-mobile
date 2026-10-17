using System;
using UnityEngine;

namespace Game.View
{
    public sealed partial class WorldRenderer
    {
        string m1Shape, m1Cue;
        string[] m1AllShapes, m1AllCues;
        readonly int[] m1AllCycles = new int[8];
        int m1Stage, m1LastCycle = -1;
        bool m1HasBounds;
        Rect m1Bounds;
        public float M1ShowcaseTime { get; set; }
        public Action<string> M1ShowcaseCue { get; set; }
        public Rect M1ShowcaseBounds => m1Bounds;
        public bool M1ShowcaseVisible => m1HasBounds;
        public string M1ShowcasePhase { get; private set; } = "idle";
        public void ConfigureM1WeaponShowcase(string shape, int stage, string cue)
        {
            if (stage < 0 || stage > 2) throw new ArgumentOutOfRangeException(nameof(stage));
            switch (shape)
            {
                case "giantblade": case "shield": case "lightning": case "meteor":
                case "tornado": case "spears": case "arrowrain": case "summon": break;
                default: throw new ArgumentException("Unknown visual showcase shape.", nameof(shape));
            }
            m1AllShapes = null; m1AllCues = null;
            m1Shape = shape; m1Stage = stage; m1Cue = cue; m1LastCycle = -1;
        }
        public void ConfigureAllM1WeaponShowcase(string[] shapes, string[] cues)
        {
            if (shapes == null || cues == null || shapes.Length != 8 || cues.Length != 8) throw new ArgumentException("All-shape review requires eight shapes and cues.");
            for (var i = 0; i < 8; i++) { ConfigureM1WeaponShowcase(shapes[i], 2, cues[i]); m1AllCycles[i] = -1; }
            m1AllShapes = (string[])shapes.Clone(); m1AllCues = (string[])cues.Clone();
        }
        void DrawM1WeaponShowcase(Vector2 lordPoint)
        {
            m1HasBounds = false; m1Bounds = default;
            if (m1AllShapes != null)
            {
                if (M1ShowcaseTime < 0) return;
                for (var i = 0; i < 8; i++)
                {
                    m1Shape = m1AllShapes[i]; m1Cue = m1AllCues[i]; m1LastCycle = m1AllCycles[i];
                    DrawM1WeaponShape(lordPoint); m1AllCycles[i] = m1LastCycle;
                }
                return;
            }
            if (m1Shape == null || M1ShowcaseTime < 0 || M1ShowcaseTime >= 10) return;
            DrawM1WeaponShape(lordPoint);
        }
        void DrawM1WeaponShape(Vector2 lordPoint)
        {
            var viewport = CameraWorldViewport;
            if (viewport.width <= 0 || viewport.height <= 0) return;
            var cycle = Mathf.FloorToInt(M1ShowcaseTime / 2);
            var phase = M1ShowcaseTime / 2 - cycle;
            M1ShowcasePhase = phase < .15f ? "anticipation" : phase < .7f ? "release" : "recovery";
            if (cycle != m1LastCycle && phase >= .15f) { m1LastCycle = cycle; M1ShowcaseCue?.Invoke(m1Cue); }
            var strength = m1Stage == 0 ? .22f : m1Stage == 1 ? .42f : .92f;
            var radius = Mathf.Min(viewport.width, viewport.height) * strength;
            var center = new Vector2(viewport.center.x, Mathf.Lerp(lordPoint.y, viewport.center.y, .35f));
            var release = Mathf.Clamp01((phase - .15f) / .55f);
            var opacity = phase < .15f ? .22f : Mathf.Clamp01((1 - phase) * 2.8f);
            var count = m1Stage == 0 ? 3 : m1Stage == 1 ? 6 : 12;
            switch (m1Shape)
            {
                case "giantblade":
                    var angle = Mathf.Lerp(-100, 100, release);
                    M1Draw("attack", "core:iron_blade", "sector90", center, Vector2.one * radius * 2, angle, opacity, new Color(.75f,.9f,1));
                    M1Draw("icon", "core:iron_blade", "default", center + M1Direction(angle) * radius * .48f, new Vector2(radius * .3f,radius * 1.3f), angle - 90, opacity, Color.white);
                    break;
                case "shield":
                    for (var i = 0; i < count; i++)
                    {
                        var direction = M1Direction(i * 360f / count + M1ShowcaseTime * 135);
                        M1Draw("wave", "shield-fragment", "default", center + direction * radius * .68f, Vector2.one * radius * .43f, -i * 360f / count, opacity, new Color(.8f,.92f,1));
                    }
                    break;
                case "lightning":
                    for (var branch = 0; branch < count; branch++)
                    {
                        var end = center + M1Direction(branch * 360f / count + 15) * radius;
                        var prior = center;
                        for (var segment = 1; segment <= 4; segment++)
                        {
                            var next = Vector2.Lerp(center,end,segment / 4f) + new Vector2(segment % 2 == 0 ? -.05f : .05f,.025f) * radius;
                            var delta = next - prior;
                            M1Draw("attack", "core:storm_fork", "chain", (prior + next) * .5f, new Vector2(delta.magnitude,radius * .09f), Angle(delta), opacity, new Color(.78f,.96f,1));
                            prior = next;
                        }
                    }
                    break;
                case "meteor":
                    for (var i = 0; i < count / 2; i++)
                    {
                        var target = center + M1Direction(i * 137.5f) * radius * (i == 0 ? 0 : .65f);
                        var descent = Mathf.Clamp01(release * 1.5f - i * .06f);
                        var position = target + new Vector2(.3f,1) * radius * (1 - descent);
                        M1Draw("attack", "core:ember_wand", "projectile", position, Vector2.one * radius * .47f, -100, opacity, new Color(1,.72f,.28f));
                        if (descent > .9f) M1Draw("attack", "core:muster_horn", "wave", target, Vector2.one * radius * .92f, 0, opacity, new Color(1,.83f,.4f));
                    }
                    break;
                case "tornado":
                    for (var i = 0; i < 9; i++)
                    {
                        var size = radius * (.32f + i * .13f);
                        var position = center + new Vector2(Mathf.Sin(M1ShowcaseTime * 10 + i * .7f) * radius * .13f, (i / 8f - .5f) * radius * 1.8f);
                        M1Draw("attack", "core:rain_ladle", "wave", position, new Vector2(size,size * .28f), Mathf.Sin(i + M1ShowcaseTime * 4) * 10, opacity * .8f, new Color(.65f,.9f,.65f));
                    }
                    break;
                case "spears":
                    for (var i = 0; i < count + 1; i++)
                    {
                        var x = (i / (float)count - .5f) * radius * 1.8f;
                        var rise = Mathf.Clamp01(release * 3 - i * .06f);
                        M1Draw("attack", "core:canal_bow", "piercing", center + new Vector2(x, radius * .12f * Mathf.Sin(i)), new Vector2(radius * rise * 1.3f,radius * .14f), 78, opacity, new Color(.82f,.9f,.68f));
                    }
                    break;
                case "arrowrain":
                    for (var i = 0; i < count * 3; i++)
                    {
                        var fall = Mathf.Repeat(release * 2 + i * .137f,1);
                        var x = (Mathf.Repeat(i * .618f,1) - .5f) * radius * 1.8f;
                        M1Draw("attack", "core:canal_bow", "piercing", center + new Vector2(x + (1 - fall) * radius * .3f, (.5f - fall) * radius * 1.8f), new Vector2(radius * .35f,radius * .045f), -110, opacity, new Color(.9f,.95f,.7f));
                    }
                    break;
                case "summon":
                    M1Draw("attack", "core:muster_horn", "wave", center, Vector2.one * radius * 2, 0, opacity * .45f, new Color(.7f,.85f,1));
                    for (var i = 0; i < count; i++)
                    {
                        var position = center + M1Direction(i * 360f / count + 20) * radius * (.5f + release * .3f);
                        M1Draw("person", "guard", "muster", position, Vector2.one * radius * .48f, 0, opacity, new Color(.86f,.9f,1));
                    }
                    break;
            }
        }
        static Vector2 M1Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        void M1Draw(string category, string id, string state, Vector2 point, Vector2 size, float degrees, float opacity, Color tint)
        {
            if (opacity <= 0 || size.x <= 0 || size.y <= 0) return;
            var visual = Resolve(category,id,state);
            Draw(visual,GameVisualTokens.AttackLayer,point,M1ShowcaseTime,size,degrees,opacity,tint,applyTween:false);
            var radians = degrees * Mathf.Deg2Rad;
            var rotated = new Vector2(Mathf.Abs(Mathf.Cos(radians)) * size.x + Mathf.Abs(Mathf.Sin(radians)) * size.y,
                Mathf.Abs(Mathf.Sin(radians)) * size.x + Mathf.Abs(Mathf.Cos(radians)) * size.y);
            var bounds = new Rect(point - rotated * .5f,rotated);
            m1Bounds = m1HasBounds ? Rect.MinMaxRect(Mathf.Min(m1Bounds.xMin,bounds.xMin),Mathf.Min(m1Bounds.yMin,bounds.yMin),Mathf.Max(m1Bounds.xMax,bounds.xMax),Mathf.Max(m1Bounds.yMax,bounds.yMax)) : bounds;
            m1HasBounds = true;
        }
    }
}
