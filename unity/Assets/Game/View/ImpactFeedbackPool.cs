using UnityEngine;

namespace Game.View
{
    public enum ImpactParticleKind { Shard, Dust, Experience }

    public struct ImpactParticle
    {
        public ImpactParticleKind Kind;
        public Vector2 Origin, Velocity;
        public float Age, Duration, Size, Rotation;
        public float Progress => Mathf.Clamp01(Age / Duration);
        public Vector2 Position => Origin + Velocity * Age + Vector2.down * (Age * Age * .6f);
    }

    public sealed class ImpactFeedbackPool
    {
        public const int Capacity = 1024;
        readonly ImpactParticle[] particles = new ImpactParticle[Capacity];
        public int Count { get; private set; }
        public int Dropped { get; private set; }
        public ImpactParticle this[int index] => particles[index];
        public void Clear() { Count = 0; Dropped = 0; }

        public void Hit(long eventId, Vector2 origin)
        {
            var state = unchecked((uint)eventId * 747796405u + 2891336453u);
            var count = 3 + state % 3;
            for (var i = 0; i < count; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                var angle = (state & 1023) * (Mathf.PI * 2 / 1024);
                var speed = .45f + ((state >> 10) & 255) / 255f * .4f;
                Add(ImpactParticleKind.Shard, origin, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
                    .16f, .045f, angle * Mathf.Rad2Deg);
            }
        }

        public void Kill(long eventId, Vector2 origin, bool grantsExperience)
        {
            Hit(eventId, origin);
            Add(ImpactParticleKind.Dust, origin, Vector2.up * .08f, .32f, .3f, 0);
            if (grantsExperience) Add(ImpactParticleKind.Experience, origin, Vector2.up * .8f, .42f, .15f, 0);
        }

        void Add(ImpactParticleKind kind, Vector2 origin, Vector2 velocity, float duration, float size, float rotation)
        {
            if (Count == Capacity) { Dropped++; return; }
            particles[Count++] = new ImpactParticle { Kind = kind, Origin = origin, Velocity = velocity,
                Duration = duration, Size = size, Rotation = rotation };
        }

        public void Advance(float seconds)
        {
            seconds = Mathf.Max(0, seconds);
            for (var i = Count - 1; i >= 0; i--)
            {
                particles[i].Age += seconds;
                if (particles[i].Age >= particles[i].Duration) particles[i] = particles[--Count];
            }
        }
    }
}
