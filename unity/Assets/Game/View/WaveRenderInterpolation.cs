using System;
using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public struct OrbitRenderMotion
    {
        float angle, target, angularVelocity, radius, radiusTarget, radialVelocity, sampledAt, radiusSpeed;
        public float AngularVelocity => angularVelocity;
        public float MaximumAngularLag { get; private set; }
        public OrbitRenderMotion(float angle, float time, float radius = 0)
        {
            this.angle = target = angle; this.radius = radiusTarget = radius;
            sampledAt = time; angularVelocity = radialVelocity = 0;
            radiusSpeed = Mathf.Max(1, radius) * 4; MaximumAngularLag = 0;
        }
        void Advance(float time)
        {
            var delta = time - sampledAt;
            if (delta <= 0) return;
            angle = Mathf.SmoothDampAngle(angle, target, ref angularVelocity, GameVisualTokens.OrbitSmoothSeconds, GameVisualTokens.OrbitMaxDegreesPerSecond, delta);
            radius = Mathf.SmoothDamp(radius, radiusTarget, ref radialVelocity, GameVisualTokens.OrbitSmoothSeconds, radiusSpeed, delta);
            sampledAt = time;
            MaximumAngularLag = Mathf.Max(MaximumAngularLag, Mathf.Abs(Mathf.DeltaAngle(angle, target)));
        }
        public float AngleAt(float time) { Advance(time); return angle; }
        public float RadiusAt(float time) { Advance(time); return radius; }
        public void SetTarget(float value, float time)
        {
            Advance(time); target = value;
            MaximumAngularLag = Mathf.Max(MaximumAngularLag, Mathf.Abs(Mathf.DeltaAngle(angle, target)));
        }
        public void SetRadius(float value, float time) { Advance(time); radiusTarget = value; }
    }

    public sealed class WaveRenderInterpolation
    {
        readonly Dictionary<int, WaveGroupView> previousGroups = new Dictionary<int, WaveGroupView>(32);
        readonly Dictionary<int, WaveProjectileView> previousProjectiles = new Dictionary<int, WaveProjectileView>(256);
        readonly Dictionary<(string source, int activation, int expiry, int ordinal), OrbitState> orbits = new Dictionary<(string, int, int, int), OrbitState>(128);
        readonly Dictionary<(string source, int activation, int expiry), int> ordinals = new Dictionary<(string, int, int), int>(32);
        readonly List<(string source, int activation, int expiry, int ordinal)> keys = new List<(string, int, int, int)>(128);
        readonly List<(string source, int activation, int expiry, int ordinal)> expired = new List<(string, int, int, int)>(128);
        WaveRuntimeFrame accepted;
        int generation;
        int nextSampleId;
        struct OrbitState { public WaveAttackView before, current; public OrbitRenderMotion motion; public int generation, sampleId; }
        public int OrbitSampleId(int index) => orbits[keys[index]].sampleId;
        public static float BlendAngle(float previous, float current, float alpha) => Mathf.LerpAngle(previous, current, Mathf.Clamp01(alpha));
        public static Vector2 RadialPoint(Vector2 origin, float radius, float degrees) => origin + new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * radius;

        public void Reset()
        {
            previousGroups.Clear(); previousProjectiles.Clear(); orbits.Clear(); ordinals.Clear(); keys.Clear(); expired.Clear(); accepted = null; generation = nextSampleId = 0;
        }

        public void Accept(WaveRuntimeFrame frame, float time)
        {
            if (ReferenceEquals(frame, accepted)) return;
            previousGroups.Clear(); previousProjectiles.Clear();
            if (accepted != null)
            {
                for (var i = 0; i < accepted.Groups.Count; i++) previousGroups[accepted.Groups[i].Id] = accepted.Groups[i];
                for (var i = 0; i < accepted.Projectiles.Count; i++) previousProjectiles[accepted.Projectiles[i].Id] = accepted.Projectiles[i];
            }
            generation++; keys.Clear(); ordinals.Clear();
            for (var i = 0; i < frame.Attacks.Count; i++)
            {
                var attack = frame.Attacks[i];
                var activation = (attack.Source, attack.ActivationId, attack.ExpireTick);
                ordinals.TryGetValue(activation, out var ordinal); ordinals[activation] = ordinal + 1;
                var key = (attack.Source, attack.ActivationId, attack.ExpireTick, ordinal); keys.Add(key);
                var delta = new Vector2(attack.Position.X - attack.Origin.X, attack.Position.Y - attack.Origin.Y);
                var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                if (!orbits.TryGetValue(key, out var state))
                    state = new OrbitState { before = attack, current = attack, motion = new OrbitRenderMotion(angle, time, delta.magnitude), sampleId = nextSampleId++ };
                state.before = state.current; state.current = attack; state.generation = generation;
                state.motion.SetTarget(angle, time); state.motion.SetRadius(delta.magnitude, time); orbits[key] = state;
            }
            expired.Clear();
            foreach (var pair in orbits) if (pair.Value.generation != generation) expired.Add(pair.Key);
            for (var i = 0; i < expired.Count; i++) orbits.Remove(expired[i]);
            accepted = frame;
        }

        public Vector2 GroupPosition(WaveGroupView group, float alpha, float units)
        {
            var current = new Vector2(group.Position.X, group.Position.Y) / units;
            return previousGroups.TryGetValue(group.Id, out var previous) && previous.Source == group.Source
                ? Vector2.Lerp(new Vector2(previous.Position.X, previous.Position.Y) / units, current, alpha) : current;
        }
        public Vector2 GroupDirection(WaveGroupView group)
            =>previousGroups.TryGetValue(group.Id,out var previous)&&previous.Source==group.Source
                ?new Vector2(group.Position.X-previous.Position.X,group.Position.Y-previous.Position.Y):Vector2.zero;

        public float ProjectileAngle(WaveProjectileView projectile, float alpha)
        {
            var delta = new Vector2(projectile.Position.X - projectile.Previous.X, projectile.Position.Y - projectile.Previous.Y);
            var current = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            if (!previousProjectiles.TryGetValue(projectile.Id, out var previous) || previous.Source != projectile.Source) return current;
            var before = new Vector2(previous.Position.X - previous.Previous.X, previous.Position.Y - previous.Previous.Y);
            var angle = Mathf.Atan2(before.y, before.x) * Mathf.Rad2Deg;
            return delta.sqrMagnitude == 0 ? angle : BlendAngle(angle, current, alpha);
        }

        public Vector2 OrbitPosition(int index, float alpha, float time, float units, WorldPoint lord, Vector2 renderedLord)
        {
            var state = orbits[keys[index]];
            var current = state.current; var previous = state.before;
            var center = current.Origin.Equals(lord) ? renderedLord : Vector2.Lerp(new Vector2(previous.Origin.X, previous.Origin.Y), new Vector2(current.Origin.X, current.Origin.Y), alpha) / units;
            var position = RadialPoint(center, state.motion.RadiusAt(time) / units, state.motion.AngleAt(time));
            orbits[keys[index]] = state;
            return position;
        }
    }
}
