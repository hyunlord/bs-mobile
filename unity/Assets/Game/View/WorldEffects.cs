using System.Collections.Generic;
using SowSiege.Core;
using UnityEngine;

namespace Game.View
{
    public readonly struct WorldEffect
    {
        public readonly PresentationEvent Event;
        public readonly float Age;
        public readonly float Duration;
        public float Progress => Mathf.Clamp01(Age / Duration);
        internal WorldEffect(PresentationEvent effect, float age, float duration) { Event = effect; Age = age; Duration = duration; }
    }

    public sealed class WorldEffects
    {
        public const int Capacity = 2048;
        public const float AttackLifetimeSeconds = 0.18f;
        public const float ExperienceLifetimeSeconds = 0.55f;
        public const float BuildingLifetimeSeconds = 0.45f;
        private readonly WorldEffect[] active = new WorldEffect[Capacity];
        private long lastEventId = -1;
        private int recycleIndex;
        public int ActiveCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int UnsupportedShapeCount { get; private set; }
        public WorldEffect this[int index] => active[index];
        public int ActiveVisualProjectiles
        {
            get
            {
                var result = 0;
                for (var index = 0; index < ActiveCount; index++) if (active[index].Event.Kind == PresentationKind.Attack && (active[index].Event.Shape == "rays" || active[index].Event.Shape == "projectile")) result++;
                return result;
            }
        }

        public void Accept(IReadOnlyList<PresentationEvent> events)
        {
            for (var index = 0; index < events.Count; index++)
            {
                var effect = events[index];
                if (effect.Id <= lastEventId) continue;
                lastEventId = effect.Id;
                if (effect.Kind == PresentationKind.Attack && !Supported(effect.Shape))
                {
                    UnsupportedShapeCount++;
                    UnityEngine.Debug.LogWarning("Unsupported Core attack shape: " + effect.Shape);
                    continue;
                }
                var duration = effect.Kind == PresentationKind.Attack ? AttackLifetimeSeconds : effect.Kind == PresentationKind.BuildingCompleted ? BuildingLifetimeSeconds : ExperienceLifetimeSeconds;
                var slot = ActiveCount;
                if (slot == Capacity) { slot = recycleIndex; recycleIndex = (recycleIndex + 1) % Capacity; DroppedCount++; }
                else ActiveCount++;
                active[slot] = new WorldEffect(effect, 0, duration);
            }
        }

        public void Advance(float seconds)
        {
            seconds = Mathf.Max(0, seconds);
            var index = 0;
            while (index < ActiveCount)
            {
                var effect = active[index]; var age = effect.Age + seconds;
                if (age >= effect.Duration) { active[index] = active[--ActiveCount]; active[ActiveCount] = default; continue; }
                active[index] = new WorldEffect(effect.Event, age, effect.Duration); index++;
            }
        }

        private static bool Supported(string shape) => shape == "rays" || shape == "projectile" || shape == "sector90" || shape == "sector180" || shape == "disk" || shape == "melee" || shape == "orbit" || shape == "wave";
    }
}
