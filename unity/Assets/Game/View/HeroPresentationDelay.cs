using UnityEngine;

namespace Game.View
{
    public sealed class HeroPresentationDelay
    {
        public const float RecoverySeconds = .08f;
        Vector2 lastRendered, delayOffset, recoveryOffset;
        bool hasRendered, wasDelayed, recovering;
        float recoveryAge;

        public void Reset()
        {
            hasRendered = wasDelayed = recovering = false;
            recoveryAge = 0;
            lastRendered = delayOffset = recoveryOffset = Vector2.zero;
        }

        public void CommitRendered(Vector2 position)
        {
            lastRendered = position;
            hasRendered = true;
        }

        public Vector2? Resolve(Vector2 interpolated, Vector2? predicted, bool delayed, float seconds)
        {
            var live = predicted ?? interpolated;
            if (delayed)
            {
                if (!wasDelayed) delayOffset = (hasRendered ? lastRendered : live) - interpolated;
                wasDelayed = true;
                recovering = false;
                return interpolated + delayOffset;
            }
            if (wasDelayed)
            {
                wasDelayed = false;
                recovering = hasRendered;
                recoveryAge = 0;
                recoveryOffset = lastRendered - live;
                return hasRendered ? lastRendered : live;
            }
            if (!recovering) return predicted;
            recoveryAge = Mathf.Min(RecoverySeconds, recoveryAge + Mathf.Max(0, seconds));
            var blend = Mathf.SmoothStep(0, 1, recoveryAge / RecoverySeconds);
            if (recoveryAge >= RecoverySeconds) recovering = false;
            return live + recoveryOffset * (1 - blend);
        }
    }
}
