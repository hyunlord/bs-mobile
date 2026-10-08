using System;
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
        public const float ExperienceLifetimeSeconds = GameVisualTokens.ExperienceSeconds;
        public const float BuildingLifetimeSeconds = GameVisualTokens.EmphasisSeconds;
        readonly WorldEffect[] active = new WorldEffect[Capacity];
        long lastEventId = -1;
        int recycleIndex;
        public int ActiveCount { get; private set; }
        public int DroppedCount { get; private set; }
        public int UnsupportedShapeCount { get; private set; }
        public WorldEffect this[int index] => active[index];
        public int ActiveVisualProjectiles => CountVisualProjectiles();
        public int CountVisualProjectiles(Func<PresentationEvent, bool> included = null)
        {
            var result = 0;
            for (var i = 0; i < ActiveCount; i++)
            {
                var effect = active[i].Event;
                if (effect.Kind == PresentationKind.Attack && (effect.Shape == "rays" || IsTravelling(effect.Shape)) && (included == null || included(effect))) result++;
            }
            return result;
        }
        public void Accept(IReadOnlyList<PresentationEvent> events, Action<PresentationEvent> accepted = null)
        {
            for (var index = 0; index < events.Count; index++)
            {
                var effect = events[index];
                if (effect.Id <= lastEventId) continue;
                lastEventId = effect.Id;
                if (effect.Kind == PresentationKind.Attack && !Supported(effect.Shape))
                {
                    UnsupportedShapeCount++;
                    Debug.LogWarning("Unsupported Core attack shape: " + effect.Shape); continue;
                }
                accepted?.Invoke(effect);
                var slot = ActiveCount;
                if (slot == Capacity) { slot = recycleIndex; recycleIndex = (recycleIndex + 1) % Capacity; DroppedCount++; }
                else ActiveCount++;
                active[slot] = new WorldEffect(effect, 0, Duration(effect.Kind));
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
        public static bool IsTravelling(string shape) => shape == "projectile" || shape == "piercing" || shape == "volley" || shape == "boomerang" || shape == "orbit";
        static float Duration(PresentationKind kind)
        {
            switch (kind)
            {
                case PresentationKind.Attack: return AttackLifetimeSeconds;
                case PresentationKind.Damage: case PresentationKind.LordHit: return GameVisualTokens.HitFlashSeconds;
                case PresentationKind.EnemyKilled: case PresentationKind.CartBroken: return GameVisualTokens.KillSeconds;
                case PresentationKind.HarvestExperience: return GameVisualTokens.HarvestSeconds;
                case PresentationKind.KillExperience: case PresentationKind.TaxExperience: return GameVisualTokens.ExperienceSeconds;
                case PresentationKind.BossWarning: return 1.2f;
                default: return BuildingLifetimeSeconds;
            }
        }
        static bool Supported(string shape) => shape == "rays" || IsTravelling(shape) || shape == "sector90" || shape == "sector180" || shape == "disk" || shape == "melee" || shape == "wave" || shape == "chain" || shape == "field" || shape == "nova";
    }
}
