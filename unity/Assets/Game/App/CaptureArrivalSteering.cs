using UnityEngine;

namespace Game.App
{
    public static class CaptureArrivalSteering
    {
        // The scripted pointer eases into a waypoint instead of crossing it at full speed every tick.
        public static Vector2 Direction(Vector2 remaining, float baseUnitsPerTick)
            => Vector2.ClampMagnitude(remaining / Mathf.Max(1, baseUnitsPerTick * 4), 1);
    }
}
