using UnityEngine;

namespace Game.View
{
    public struct HitCameraMotion
    {
        public const float MaximumPixelsAt1080 = .25f;
        public const float DurationSeconds = .18f;
        float started;
        bool active;
        public void Trigger(float time)
        {
            // A hit during the same visual impulse keeps its continuous path; flash/audio still respond to every hit.
            if (active && time < started + DurationSeconds) return;
            started = time; active = true;
        }
        public Vector2 Sample(float time, float halfHeight, int pixelHeight)
        {
            if (!active || pixelHeight <= 0) return Vector2.zero;
            var progress = (time - started) / DurationSeconds;
            if (progress <= 0 || progress >= 1) return Vector2.zero;
            var pulse = Mathf.Sin(progress * Mathf.PI);
            var pixels = MaximumPixelsAt1080 * pixelHeight / 1080f;
            return Vector2.down * (pulse * pulse * pixels * (2 * halfHeight / pixelHeight));
        }
    }
}
