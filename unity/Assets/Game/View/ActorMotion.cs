using UnityEngine;

namespace Game.View
{
    public readonly struct ActorPose
    {
        public readonly Vector2 Offset, Scale;
        public readonly float Degrees;
        public ActorPose(Vector2 offset, Vector2 scale, float degrees) { Offset = offset; Scale = scale; Degrees = degrees; }
    }

    public static class ActorMotion
    {
        public static ActorPose Sample(float time, bool walking, float attackProgress, float hitProgress)
        {
            var phase = time * Mathf.PI * 2 / GameVisualTokens.WalkSeconds;
            var stride = walking ? Mathf.Sin(phase) : 0;
            var lift = walking ? (1 - Mathf.Cos(phase * 2)) * GameVisualTokens.WalkBob * .5f : Mathf.Sin(time * 2) * .004f;
            var attack = attackProgress >= 0 && attackProgress <= 1 ? Mathf.Sin(attackProgress * Mathf.PI) : 0;
            var hit = hitProgress >= 0 && hitProgress <= 1 ? Mathf.Sin(hitProgress * Mathf.PI) : 0;
            var squash = Mathf.Cos(phase * 2) * (walking ? .025f : .004f);
            return new ActorPose(new Vector2(-attack * .025f, lift), new Vector2(1 + squash + hit * .04f, 1 - squash - hit * .04f), stride * GameVisualTokens.WalkLeanDegrees - attack * 7 + hit * 4);
        }
    }
}
