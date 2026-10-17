using UnityEngine;

namespace Game.View
{
    // Hero hit reads as a white flash on exactly two rendered frames plus a short push away from the attacker.
    // The push only offsets the drawn hero; Core position, camera and the ground ring stay where Core put them.
    public sealed class HeroHitFeedback
    {
        public const int FlashFrames = 2;
        public const float RetriggerSeconds = .25f, PushBodyFraction = .25f, PushOutSeconds = .07f, PushSeconds = .24f;
        float started = float.NegativeInfinity;
        int flashFramesLeft;
        Vector2 direction = Vector2.right;
        public int Triggers { get; private set; }

        public void Reset() { started = float.NegativeInfinity; flashFramesLeft = 0; Triggers = 0; }

        // Continuous contact damage restarts at most every RetriggerSeconds, so it never becomes a held tint.
        public bool Trigger(float now, Vector2 awayFromAttacker)
        {
            if (now - started < RetriggerSeconds) return false;
            started = now; flashFramesLeft = FlashFrames; Triggers++;
            direction = awayFromAttacker.sqrMagnitude > .000001f ? awayFromAttacker.normalized : Vector2.right;
            return true;
        }

        // Call once per rendered frame; consumes one flash frame.
        public float NextFlash()
        {
            if (flashFramesLeft <= 0) return 0;
            flashFramesLeft--;
            return 1;
        }

        public Vector2 Offset(float now, float bodyWidth)
        {
            var age = now - started;
            if (age < 0 || age >= PushSeconds) return Vector2.zero;
            var amount = age < PushOutSeconds
                ? Mathf.SmoothStep(0, 1, age / PushOutSeconds)
                : 1 - Mathf.SmoothStep(0, 1, (age - PushOutSeconds) / (PushSeconds - PushOutSeconds));
            return direction * (Mathf.Max(0, bodyWidth) * PushBodyFraction * amount);
        }
    }
}
