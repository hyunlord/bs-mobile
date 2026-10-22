using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public enum EnemyDeathTier { None, Full, Light }

    public struct DyingEnemyPose
    {
        public EnemyImpactPose Actor;
        public int Layer;
        public Color Tint;
        public float UpperOpacity;
        public string DefinitionId;
        public WaveEnemyKind Kind;
    }

    public struct EnemyDeathSample
    {
        public Vector2 Position;
        public float Flash, ActorOpacity, FallOpacity, FallDegrees, Squash, Pop;
        public bool Killed;
    }

    // Core removes a killed actor on its kill tick; this keeps only its last rendered pose alive until the
    // kill reaches the screen, then plays a short burst: flash, then swell and fade (HitReaction). Nothing here feeds back into simulation.
    public sealed class EnemyDeathPresentationPool
    {
        public const int FullCapacity = 64, LightCapacity = 512;
        // The flash frames less half a frame, as headroom for accumulated clock rounding (two frames: ages 0 and 1/60).
        public const float FlashSeconds = HitReaction.FlashSeconds - HitReaction.Frame / 2;
        public const float PendingSeconds = .25f;
        public static float FullSeconds => HitReaction.DeathSeconds;
        struct Entry
        {
            public int Id; public long EventId; public bool Killed, Burst, Experience; public float Held, Start; public Vector2 Direction;
            public DyingEnemyPose Pose;
        }
        readonly Entry[] full = new Entry[FullCapacity];
        readonly Entry[] light = new Entry[LightCapacity];
        readonly HashSet<int> ids = new HashSet<int>(FullCapacity + LightCapacity);
        int fullCount, lightCount;
        public int FullCount => fullCount;
        public int LightCount => lightCount;
        public int Count => fullCount + lightCount;
        public int Overflowed { get; private set; }
        public int Dropped { get; private set; }
        public int PeakFull { get; private set; }
        public bool Contains(int id) => ids.Contains(id);

        public void Clear()
        {
            fullCount = lightCount = 0; ids.Clear(); Overflowed = Dropped = PeakFull = 0;
        }

        public void Hold(int id, in DyingEnemyPose pose, float now)
        {
            if (ids.Contains(id)) return;
            Add(new Entry { Id = id, Held = now, Pose = pose }, false);
        }

        public EnemyDeathTier Kill(int id, long eventId, in DyingEnemyPose pose, bool hasPose, Vector2 direction, float now, float delay, bool experience = true)
        {
            direction = direction.sqrMagnitude > .000001f ? direction.normalized : Vector2.up;
            var start = now + Mathf.Max(0, delay);
            if (ids.Contains(id))
            {
                for (var i = 0; i < fullCount; i++) if (full[i].Id == id) { Arm(ref full[i], eventId, direction, start, experience); return EnemyDeathTier.Full; }
                for (var i = 0; i < lightCount; i++) if (light[i].Id == id) { Arm(ref light[i], eventId, direction, start, experience); return EnemyDeathTier.Light; }
            }
            if (!hasPose) return EnemyDeathTier.None;
            var entry = new Entry { Id = id, Held = now, Pose = pose };
            Arm(ref entry, eventId, direction, start, experience);
            return Add(entry, true);
        }

        static void Arm(ref Entry entry, long eventId, Vector2 direction, float start, bool experience)
        {
            if (entry.Killed) return;
            entry.Killed = true; entry.EventId = eventId; entry.Direction = direction; entry.Start = start; entry.Experience = experience;
        }

        EnemyDeathTier Add(Entry entry, bool killed)
        {
            if (fullCount < FullCapacity)
            {
                full[fullCount++] = entry; ids.Add(entry.Id); PeakFull = Mathf.Max(PeakFull, fullCount); return EnemyDeathTier.Full;
            }
            if (killed) Overflowed++;
            if (lightCount < LightCapacity) { light[lightCount++] = entry; ids.Add(entry.Id); return EnemyDeathTier.Light; }
            Dropped++;
            return EnemyDeathTier.None;
        }

        public void Release(int id)
        {
            if (!ids.Contains(id)) return;
            for (var i = 0; i < fullCount; i++) if (full[i].Id == id && !full[i].Killed) { full[i] = full[--fullCount]; ids.Remove(id); return; }
            for (var i = 0; i < lightCount; i++) if (light[i].Id == id && !light[i].Killed) { light[i] = light[--lightCount]; ids.Remove(id); return; }
        }

        public void Advance(float now)
        {
            for (var i = fullCount - 1; i >= 0; i--) if (Finished(full[i], now, true)) { ids.Remove(full[i].Id); full[i] = full[--fullCount]; }
            for (var i = lightCount - 1; i >= 0; i--) if (Finished(light[i], now, false)) { ids.Remove(light[i].Id); light[i] = light[--lightCount]; }
        }

        static bool Finished(in Entry entry, float now, bool full) => entry.Killed
            ? now - entry.Start >= (full ? FullSeconds : FlashSeconds)
            : now - entry.Held >= PendingSeconds;

        public ref readonly DyingEnemyPose Pose(int index) => ref (index < fullCount ? ref full[index].Pose : ref light[index - fullCount].Pose);
        public bool IsFull(int index) => index < fullCount;

        // True once per kill when its sequence begins, so the death burst follows the blade instead of preceding it.
        public bool TakeBurst(int index, float now, out long eventId, out Vector2 origin, out bool experience)
        {
            ref var entry = ref (index < fullCount ? ref full[index] : ref light[index - fullCount]);
            eventId = entry.EventId; origin = entry.Pose.Actor.Position; experience = entry.Experience;
            if (!entry.Killed || entry.Burst || now < entry.Start) return false;
            entry.Burst = true;
            return true;
        }

        // knock: the push along the kill direction in world units (HitReaction.KnockbackPixels on screen).
        public EnemyDeathSample Sample(int index, float now, float knock = 0)
        {
            var isFull = index < fullCount;
            ref readonly var entry = ref (isFull ? ref full[index] : ref light[index - fullCount]);
            var sample = new EnemyDeathSample { Position = entry.Pose.Actor.Position, ActorOpacity = 1, Killed = entry.Killed };
            if (!entry.Killed) return sample;
            var age = now - entry.Start;
            if (age < 0) return sample;
            sample.Flash = age < FlashSeconds ? 1 : 0;
            if (!isFull) { sample.ActorOpacity = age < FlashSeconds ? 1 : 0; return sample; }
            sample.Position += entry.Direction * (Mathf.Max(0, knock) * Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / HitReaction.DeathSeconds)));
            // After the flash frames the body swells and fades; it is gone at DeathSeconds (no fallen body is left behind).
            var burst = Mathf.Clamp01((age - HitReaction.FlashSeconds) / (HitReaction.DeathSeconds - HitReaction.FlashSeconds));
            sample.Pop = HitReaction.DeathPopScale * Mathf.Sin(burst * Mathf.PI * .5f);
            sample.ActorOpacity = age >= HitReaction.DeathSeconds ? 0 : 1 - burst * burst;
            return sample;
        }
    }
}
