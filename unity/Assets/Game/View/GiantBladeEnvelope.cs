using UnityEngine;

namespace Game.View
{
    // Presentation envelope of one giant-blade strike. Core decides hits; the drawn fan only has to stay on screen and
    // enclose every on-screen kill of that strike (knockback may carry a victim outside the Core 90° fan).
    public static class GiantBladeEnvelope
    {
        public const float CoreHalfAngle = 45; // Core fan: 2·dot² ≥ |p|²·|f|²
        public const float RadiusMargin = 1.06f, AngleMargin = 4, ViewportFill = .94f, MaximumHalfAngle = 180;
        public const float SampleDegrees = 5;
        public const float SweepSeconds = .15f, HoldSeconds = .15f, FadeSeconds = .12f;
        public static float LifetimeSeconds => Mathf.Max(SweepSeconds, HoldSeconds) + FadeSeconds;

        public static float Opacity(float age)
        {
            var hold = Mathf.Max(SweepSeconds, HoldSeconds);
            return age < hold ? 1 : 1 - Mathf.Clamp01((age - hold) / FadeSeconds);
        }

        public static float SweepAngle(float aim, float halfAngle, float age)
            => aim - halfAngle + 2 * halfAngle * Mathf.Clamp01(age / SweepSeconds);

        // Seconds after the strike at which the sweeping blade reaches a victim at this bearing.
        public static float ArrivalSeconds(float aim, Vector2 origin, Vector2 point)
        {
            var offset = Mathf.DeltaAngle(aim, Angle(point - origin));
            return SweepSeconds * Mathf.Clamp01((offset + CoreHalfAngle) / (2 * CoreHalfAngle));
        }

        // Victims fall along the sweep: away from the hero and forward in the swing direction.
        public static Vector2 FallDirection(Vector2 origin, Vector2 point)
        {
            var radial = point - origin;
            if (radial.sqrMagnitude < .000001f) return Vector2.up;
            radial.Normalize();
            return (radial * .6f + new Vector2(-radial.y, radial.x) * .8f).normalized;
        }

        public static float VisibleReach(Vector2 origin, Vector2 direction, Rect bounds)
        {
            if (!bounds.Contains(origin) || direction.sqrMagnitude < .000001f) return 0;
            direction.Normalize();
            var reach = float.PositiveInfinity;
            if (direction.x > 0) reach = Mathf.Min(reach, (bounds.xMax - origin.x) / direction.x);
            else if (direction.x < 0) reach = Mathf.Min(reach, (bounds.xMin - origin.x) / direction.x);
            if (direction.y > 0) reach = Mathf.Min(reach, (bounds.yMax - origin.y) / direction.y);
            else if (direction.y < 0) reach = Mathf.Min(reach, (bounds.yMin - origin.y) / direction.y);
            return float.IsInfinity(reach) ? 0 : reach;
        }

        // Largest on-screen fan the Core reach allows, grown to the minimal envelope of the strike's on-screen kills.
        public static void Fit(Vector2 origin, float aim, float coreRadius, Rect visible, Vector2[] kills, int killCount,
            out float radius, out float halfAngle)
        {
            radius = Mathf.Min(Mathf.Max(0, coreRadius), SpanReach(origin, aim, CoreHalfAngle, visible) * ViewportFill);
            halfAngle = CoreHalfAngle;
            for (var i = 0; i < killCount; i++)
            {
                var offset = kills[i] - origin;
                radius = Mathf.Max(radius, offset.magnitude * RadiusMargin);
                halfAngle = Mathf.Max(halfAngle, Mathf.Abs(Mathf.DeltaAngle(aim, Angle(offset))) + AngleMargin);
            }
            halfAngle = Mathf.Min(MaximumHalfAngle, halfAngle);
        }

        // Shortest on-screen distance across the fan, so the base fan never leaves the screen at either edge.
        public static float SpanReach(Vector2 origin, float aim, float halfAngle, Rect visible)
        {
            var reach = float.PositiveInfinity;
            for (var offset = -halfAngle; offset <= halfAngle + .001f; offset += SampleDegrees)
                reach = Mathf.Min(reach, VisibleReach(origin, Direction(aim + offset), visible));
            return float.IsInfinity(reach) ? 0 : reach;
        }

        // True when the whole drawn fan, arc included, lies inside the visible bounds.
        public static bool FullyVisible(Vector2 origin, float aim, float radius, float halfAngle, Rect visible)
            => visible.Contains(origin) && SpanReach(origin, aim, halfAngle, visible) >= radius;

        public static bool Contains(Vector2 origin, float aim, float radius, float halfAngle, Vector2 point)
        {
            var offset = point - origin;
            return offset.magnitude <= radius && Mathf.Abs(Mathf.DeltaAngle(aim, Angle(offset))) <= halfAngle;
        }

        public static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
    }
}
