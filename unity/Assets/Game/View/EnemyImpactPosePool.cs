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
        struct Reaction { public float Started, FlashUntil, Hold, Shake, Recoil; public Vector2 Direction; public bool Captured, Flashes; public EnemyImpactPose Pose; }
        readonly Dictionary<int, Reaction> reactions = new Dictionary<int, Reaction>(Capacity);
        readonly List<int> expired = new List<int>(Capacity);
        public int Count => reactions.Count;
        public int Dropped { get; private set; }
        public void Clear() { reactions.Clear(); expired.Clear(); Dropped = 0; }
        public void Hit(int id, float now, Vector2 direction) => Hit(id, now, direction, GameVisualTokens.EnemyPoseHoldSeconds, 0, true);

        // holdSeconds: how long only this enemy's pose is frozen; shake: jitter amplitude in body widths while frozen
        // (electric hits); flash=false gives a shove without the white "damaged" signal.
        public void Hit(int id, float now, Vector2 direction, float holdSeconds, float shake, bool flash)
        {
            if (reactions.TryGetValue(id, out var previous) && now - previous.Started < previous.Recoil)
            {
                if (flash) { previous.FlashUntil = now + GameVisualTokens.EnemyHitFlashSeconds; previous.Flashes = true; }
                previous.Hold = Mathf.Max(previous.Hold, holdSeconds); previous.Shake = Mathf.Max(previous.Shake, shake);
                previous.Recoil = Mathf.Max(previous.Recoil, holdSeconds + GameVisualTokens.EnemyRecoilSeconds - GameVisualTokens.EnemyPoseHoldSeconds);
                reactions[id] = previous;
                return;
            }
            if (reactions.Count == Capacity && !reactions.ContainsKey(id)) { Dropped++; return; }
            reactions[id] = new Reaction { Started = now, FlashUntil = flash ? now + GameVisualTokens.EnemyHitFlashSeconds : now, Flashes = flash,
                Hold = holdSeconds, Shake = shake, Recoil = holdSeconds + GameVisualTokens.EnemyRecoilSeconds - GameVisualTokens.EnemyPoseHoldSeconds,
                Direction = direction.sqrMagnitude > .000001f ? direction.normalized : Vector2.up };
        }
        public void Advance(float now)
        {
            expired.Clear();
            foreach (var pair in reactions)
                if (now - pair.Value.Started >= pair.Value.Recoil && now >= pair.Value.FlashUntil) expired.Add(pair.Key);
            for (var i = 0; i < expired.Count; i++) reactions.Remove(expired[i]);
        }
        public EnemyImpactPose Sample(int id, float now, float bodyWidth, EnemyImpactPose current)
        {
            if (!reactions.TryGetValue(id, out var reaction)) return current;
            var age = Mathf.Max(0, now - reaction.Started);
            current.Flash = now < reaction.FlashUntil;
            if (age >= reaction.Recoil) return current;
            if (!reaction.Captured)
            {
                reaction.Started = now; age = 0;
                reaction.FlashUntil = reaction.Flashes ? now + GameVisualTokens.EnemyHitFlashSeconds : now;
                reaction.Pose = current;
                reaction.Pose.Position += reaction.Direction * (Mathf.Max(0, bodyWidth) * GameVisualTokens.EnemyRecoilBodyFraction);
                reaction.Captured = true; reactions[id] = reaction;
            }
            current.Flash = now < reaction.FlashUntil;
            if (age < reaction.Hold)
            {
                var held = reaction.Pose; held.Flash = current.Flash;
                // Alternating sideways jitter, one step per 60Hz frame, only for the struck enemy.
                if (reaction.Shake > 0) held.Position += new Vector2(-reaction.Direction.y, reaction.Direction.x) *
                    (Mathf.Max(0, bodyWidth) * reaction.Shake * ((Mathf.FloorToInt(age * 60) & 1) == 0 ? 1 : -1));
                return held;
            }
            var recovery = Mathf.SmoothStep(0, 1, (age - reaction.Hold) / Mathf.Max(.0001f, reaction.Recoil - reaction.Hold));
            current.Position = Vector2.Lerp(reaction.Pose.Position, current.Position, recovery);
            return current;
        }
    }
}
