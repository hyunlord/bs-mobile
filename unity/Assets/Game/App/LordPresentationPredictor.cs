using UnityEngine;

namespace Game.App
{
    public sealed class LordPresentationPredictor
    {
        Vector2 authoritative, rendered, bounds;
        float speed, tickSeconds;
        int tick;
        bool initialized, wasPaused;
        public Vector2 Position => rendered;
        public float Speed => speed;
        public bool ResetThisFrame { get; private set; }

        public void Initialize(Vector2 position, Vector2 mapBounds, float baseSpeed, float secondsPerTick, int currentTick)
        {
            authoritative = rendered = position; bounds = mapBounds; speed = baseSpeed;
            tickSeconds = secondsPerTick; tick = currentTick; initialized = true; wasPaused = true;
        }

        public void Observe(Vector2 position, int currentTick, Vector2 appliedInput)
        {
            var elapsed = (currentTick - tick) * tickSeconds;
            if(initialized && elapsed > 0 && appliedInput.sqrMagnitude > .001f)
            {
                var delta = position - authoritative;
                var along = Vector2.Dot(delta, appliedInput.normalized);
                var observed = along / (elapsed * appliedInput.magnitude);
                var onEdge = position.x <= 0 || position.y <= 0 || position.x >= bounds.x || position.y >= bounds.y;
                if(observed > 0 && !onEdge) speed = observed;
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
                var target = Clamp(authoritative + input * speed * Mathf.Clamp(residualSeconds, 0, tickSeconds));
                var integrated = Clamp(rendered + input * speed * dt);
                rendered = Vector2.MoveTowards(integrated, target, speed * dt * .35f);
                rendered = Clamp(authoritative + Vector2.ClampMagnitude(rendered - authoritative, speed * tickSeconds));
            }
            wasPaused = paused;
            return rendered;
        }

        Vector2 Clamp(Vector2 position) => new Vector2(Mathf.Clamp(position.x, 0, bounds.x), Mathf.Clamp(position.y, 0, bounds.y));
    }
}
