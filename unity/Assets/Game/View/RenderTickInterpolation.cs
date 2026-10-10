using UnityEngine;

namespace Game.View
{
    public sealed class RenderTickInterpolation
    {
        float renderedTick;
        bool initialized;
        public void Reset() => initialized = false;
        public float Present(int tick, float alpha, bool paused, float deltaSeconds, int tickRate)
        {
            var desired = tick - 1 + Mathf.Clamp01(alpha);
            if (!initialized) { renderedTick = desired; initialized = true; }
            if (paused)
                renderedTick = Mathf.MoveTowards(renderedTick, tick, Mathf.Max(0, deltaSeconds) * tickRate);
            else
                renderedTick = Mathf.Max(renderedTick, desired);
            return Mathf.Clamp01(renderedTick - (tick - 1));
        }
    }
}
