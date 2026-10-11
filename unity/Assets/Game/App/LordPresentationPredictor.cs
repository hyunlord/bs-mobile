using UnityEngine;

namespace Game.App
{
    public sealed class LordPresentationPredictor
    {
        Vector2 authoritative, rendered, bounds, measuredVelocity, measuredInput;
        float speed, tickSeconds;
        int tick;
        bool initialized, wasPaused, hasMeasuredVelocity;
        public Vector2 Position => rendered;
        public float Speed => speed;
        public bool ResetThisFrame { get; private set; }

        public void Initialize(Vector2 position, Vector2 mapBounds, float baseSpeed, float secondsPerTick, int currentTick)
        {
            authoritative = rendered = position; bounds = mapBounds; speed = baseSpeed;
            tickSeconds = secondsPerTick; tick = currentTick; initialized = true; wasPaused = true;
            measuredVelocity = measuredInput = Vector2.zero; hasMeasuredVelocity = false;
        }

        public void Observe(Vector2 position, int currentTick, Vector2 appliedInput)
        {
            var elapsed = (currentTick - tick) * tickSeconds;
            if(initialized && elapsed > 0 && appliedInput.sqrMagnitude > .00000001f)
            {
                var velocity = (position - authoritative) / elapsed;
                var onEdge = position.x <= 0 || position.y <= 0 || position.x >= bounds.x || position.y >= bounds.y;
                if(!onEdge)
                {
                    measuredVelocity = velocity; measuredInput = appliedInput; hasMeasuredVelocity = true;
                    if(velocity.sqrMagnitude > 0) speed = velocity.magnitude / appliedInput.magnitude;
                }
                else hasMeasuredVelocity = false;
            }
            authoritative = position; tick = currentTick;
        }

        public Vector2 Present(Vector2 position, Vector2 input, float elapsedSeconds, float residualSeconds, bool paused)
        {
            authoritative = position;
            ResetThisFrame = !initialized || paused != wasPaused;
            if(!initialized || paused || wasPaused)
            {
                rendered = authoritative;
                initialized = true;
            }
            if(!paused)
            {
                var dt = Mathf.Max(0, elapsedSeconds);
                var velocity = hasMeasuredVelocity && (input - measuredInput).sqrMagnitude < .00000001f
                    ? measuredVelocity : input * speed;
                var target = Clamp(authoritative + velocity * Mathf.Clamp(residualSeconds, 0, tickSeconds));
                var integrated = Clamp(rendered + velocity * dt);
                var correction = 1 - Mathf.Exp(-dt / .06f);
                rendered = Vector2.Lerp(integrated, target, correction);
                if((rendered - target).sqrMagnitude < .000000000001f) rendered = target;
                rendered = Clamp(authoritative + Vector2.ClampMagnitude(rendered - authoritative, speed * tickSeconds));
            }
            wasPaused = paused;
            return rendered;
        }

        Vector2 Clamp(Vector2 position) => new Vector2(Mathf.Clamp(position.x, 0, bounds.x), Mathf.Clamp(position.y, 0, bounds.y));
    }
}
