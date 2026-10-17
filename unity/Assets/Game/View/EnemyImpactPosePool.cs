using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    public struct EnemyImpactPose
    {
        public Vector2 Position;
        public float AnimationTime;
        public Rect Uv;
        public ActorFacingPose Facing;
        public ActorPose Body;
        public ArtVisual Visual;
        public Vector2 Dimensions;
        public bool Rigged, Flash;
    }

    // Only presentation poses are held; snapshots, other actors and the camera keep advancing.
    public sealed class EnemyImpactPosePool
    {
        public const int Capacity = 1024;
        struct Reaction { public float Started, FlashUntil; public Vector2 Direction; public bool Captured; public EnemyImpactPose Pose; }
        readonly Dictionary<int, Reaction> reactions = new Dictionary<int, Reaction>(Capacity);
        readonly List<int> expired = new List<int>(Capacity);
        public int Count => reactions.Count;
        public int Dropped { get; private set; }
        public void Clear() { reactions.Clear(); expired.Clear(); Dropped = 0; }
        public void Hit(int id, float now, Vector2 direction)
        {
            if (reactions.TryGetValue(id, out var previous) && now - previous.Started < GameVisualTokens.EnemyRecoilSeconds)
            {
                previous.FlashUntil = now + GameVisualTokens.EnemyHitFlashSeconds;
                reactions[id] = previous;
                return;
            }
            if (reactions.Count == Capacity && !reactions.ContainsKey(id)) { Dropped++; return; }
            reactions[id] = new Reaction { Started = now, FlashUntil = now + GameVisualTokens.EnemyHitFlashSeconds,
                Direction = direction.sqrMagnitude > .000001f ? direction.normalized : Vector2.up };
        }
        public void Advance(float now)
        {
            expired.Clear();
            foreach (var pair in reactions)
                if (now - pair.Value.Started >= GameVisualTokens.EnemyRecoilSeconds && now >= pair.Value.FlashUntil) expired.Add(pair.Key);
            for (var i = 0; i < expired.Count; i++) reactions.Remove(expired[i]);
        }
        public EnemyImpactPose Sample(int id, float now, float bodyWidth, EnemyImpactPose current)
        {
            if (!reactions.TryGetValue(id, out var reaction)) return current;
            var age = Mathf.Max(0, now - reaction.Started);
            current.Flash = now < reaction.FlashUntil;
            if (age >= GameVisualTokens.EnemyRecoilSeconds) return current;
            if (!reaction.Captured)
            {
                reaction.Started = now; age = 0;
                reaction.FlashUntil = now + GameVisualTokens.EnemyHitFlashSeconds;
                reaction.Pose = current;
                reaction.Pose.Position += reaction.Direction * (Mathf.Max(0, bodyWidth) * GameVisualTokens.EnemyRecoilBodyFraction);
                reaction.Captured = true; reactions[id] = reaction;
            }
            current.Flash = now < reaction.FlashUntil;
            if (age < GameVisualTokens.EnemyPoseHoldSeconds)
            {
                var held = reaction.Pose; held.Flash = current.Flash; return held;
            }
            var recovery = Mathf.SmoothStep(0, 1, (age - GameVisualTokens.EnemyPoseHoldSeconds) /
                (GameVisualTokens.EnemyRecoilSeconds - GameVisualTokens.EnemyPoseHoldSeconds));
            current.Position = Vector2.Lerp(reaction.Pose.Position, current.Position, recovery);
            return current;
        }
    }
}
