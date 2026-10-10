using UnityEngine;

namespace Game.App
{
    public sealed class LordPresentationPredictor
    {
        Vector2 authoritative, rendered, bounds, measuredVelocity, measuredInput, locomotionAnchor;
        double locomotionX, locomotionY;
        float speed, tickSeconds, quantizationTolerance, movementQuantum;
        int tick;
        bool initialized, wasPaused, hasMeasuredVelocity, expectedLocomotion;
        public Vector2 Position => rendered;
        public float Speed => speed;
        public bool ResetThisFrame { get; private set; }

        public void Initialize(Vector2 position, Vector2 mapBounds, float baseSpeed, float secondsPerTick, int currentTick,
            bool useExpectedLocomotion = false, float positionQuantizationTolerance = 0, float locomotionQuantum = 0)
        {
            authoritative = rendered = locomotionAnchor = position; bounds = mapBounds; speed = baseSpeed;
            tickSeconds = secondsPerTick; tick = currentTick; initialized = true; wasPaused = true;
            measuredVelocity = measuredInput = Vector2.zero; hasMeasuredVelocity = false;
            expectedLocomotion = useExpectedLocomotion;
            quantizationTolerance = Mathf.Max(0, positionQuantizationTolerance);
            movementQuantum = Mathf.Max(0, locomotionQuantum);
            SetLocomotionAnchor(position);
        }

        public void Observe(Vector2 position, int currentTick, Vector2 appliedInput, float? expectedSpeed = null)
        {
            var elapsed = (currentTick - tick) * tickSeconds;
            if (expectedLocomotion)
            {
                if (!expectedSpeed.HasValue || expectedSpeed.Value < 0 || float.IsNaN(expectedSpeed.Value) || float.IsInfinity(expectedSpeed.Value))
                    throw new System.ArgumentException("Expected locomotion requires a finite nonnegative speed.", nameof(expectedSpeed));
                speed = expectedSpeed.Value;
                hasMeasuredVelocity = false;
                if (initialized && elapsed > 0)
                {
                    var velocity = ExpectedVelocity(appliedInput);
                    locomotionX = System.Math.Max(0, System.Math.Min(bounds.x, locomotionX + (double)velocity.x * elapsed));
                    locomotionY = System.Math.Max(0, System.Math.Min(bounds.y, locomotionY + (double)velocity.y * elapsed));
                    var projected = new Vector2((float)locomotionX, (float)locomotionY);
                    var displacement = position - projected;
                    if (displacement.sqrMagnitude > quantizationTolerance * quantizationTolerance)
                    {
                        rendered = Clamp(rendered + displacement);
                        projected = position;
                        SetLocomotionAnchor(position);
                    }
                    if (appliedInput.sqrMagnitude > .00000001f) locomotionAnchor = projected;
                    else SetLocomotionAnchor(position);
                }
            }
            else if(initialized && elapsed > 0 && appliedInput.sqrMagnitude > .00000001f)
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
                SetLocomotionAnchor(authoritative);
                initialized = true;
            }
            if(!paused)
            {
                var dt = Mathf.Max(0, elapsedSeconds);
                var velocity = expectedLocomotion ? ExpectedVelocity(input) : hasMeasuredVelocity && (input - measuredInput).sqrMagnitude < .00000001f
                    ? measuredVelocity : input * speed;
                var origin = expectedLocomotion ? locomotionAnchor : authoritative;
                var target = Clamp(origin + velocity * Mathf.Clamp(residualSeconds, 0, tickSeconds));
                var integrated = Clamp(rendered + velocity * dt);
                var correction = 1 - Mathf.Exp(-dt / .06f);
                rendered = Vector2.Lerp(integrated, target, correction);
                if((rendered - target).sqrMagnitude < .000000000001f) rendered = target;
                rendered = Clamp(origin + Vector2.ClampMagnitude(rendered - origin, speed * tickSeconds));
            }
            wasPaused = paused;
            return rendered;
        }

        Vector2 Clamp(Vector2 position) => new Vector2(Mathf.Clamp(position.x, 0, bounds.x), Mathf.Clamp(position.y, 0, bounds.y));

        void SetLocomotionAnchor(Vector2 position)
        {
            locomotionAnchor = position; locomotionX = position.x; locomotionY = position.y;
        }

        Vector2 ExpectedVelocity(Vector2 input)
        {
            var velocity = input * speed;
            if (movementQuantum <= 0) return velocity;
            var step = velocity * (tickSeconds / movementQuantum);
            if (Mathf.Abs(step.x - Mathf.Round(step.x)) < .00001f) step.x = Mathf.Round(step.x);
            if (Mathf.Abs(step.y - Mathf.Round(step.y)) < .00001f) step.y = Mathf.Round(step.y);
            return new Vector2((int)step.x, (int)step.y) * (movementQuantum / tickSeconds);
        }
    }
}
