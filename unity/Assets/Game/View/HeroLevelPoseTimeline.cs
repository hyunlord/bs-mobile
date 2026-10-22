using UnityEngine;

namespace Game.View
{
    public sealed class HeroLevelPoseTimeline
    {
        const float DurationSeconds = .6f;
        float elapsed;
        public bool Pending { get; private set; }
        public void Queue() { Pending = true; elapsed = 0; }
        public void Reset() { Pending = false; elapsed = 0; }
        public float Sample(float seconds, bool paused, bool actionBlocked)
        {
            if (!Pending || paused || actionBlocked) return -1;
            var progress = elapsed / DurationSeconds;
            elapsed += Mathf.Max(0, seconds);
            if (elapsed >= DurationSeconds) Pending = false;
            return progress;
        }
    }
}
