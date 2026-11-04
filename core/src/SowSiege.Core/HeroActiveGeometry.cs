using System;

namespace SowSiege.Core
{
    internal readonly struct HeroActiveHit : IComparable<HeroActiveHit>
    {
        internal long Numerator { get; }
        internal long Denominator { get; }

        internal HeroActiveHit(long numerator, long denominator)
        {
            if (denominator <= 0 || numerator < 0 || numerator > denominator) { throw new ArgumentOutOfRangeException(nameof(numerator)); }
            Numerator = numerator; Denominator = denominator;
        }

        public int CompareTo(HeroActiveHit other) => HeroActiveGeometry.CompareFractions(Numerator, Denominator, other.Numerator, other.Denominator);
    }

    internal static class HeroActiveGeometry
    {
        // Protocol arithmetic bound, including expanded walls and proposed movement endpoints.
        internal const int CoordinateLimit = 4_000_000;
        // Mathematical two: binary subdivision and the quadratic cross-term coefficient.
        internal const int BinaryPartitionDivisor = 2;

        internal static bool TryHitBox(Position from, Position to, int minX, int minY, int maxX, int maxY, out HeroActiveHit hit)
        {
            ValidateBox(from, to, minX, minY, maxX, maxY);
            // A segment departing along/outside a touched face cannot enter the open
            // box. Keep boundary contact blocking only when movement enters it.
            if (from.X >= minX && from.X <= maxX && from.Y >= minY && from.Y <= maxY
                && (from.X == minX && to.X <= from.X || from.X == maxX && to.X >= from.X
                    || from.Y == minY && to.Y <= from.Y || from.Y == maxY && to.Y >= from.Y))
            { hit = default; return false; }
            var entry = new HeroActiveHit(0, 1); var exit = new HeroActiveHit(1, 1);
            if (!Clip(from.X, to.X, minX, maxX, ref entry, ref exit) || !Clip(from.Y, to.Y, minY, maxY, ref entry, ref exit))
            { hit = default; return false; }
            hit = entry; return true;
        }

        internal static bool TryExitBox(Position from, Position to, int minX, int minY, int maxX, int maxY, out HeroActiveHit hit)
        {
            ValidateBox(from, to, minX, minY, maxX, maxY);
            if (from.X < minX || from.X > maxX || from.Y < minY || from.Y > maxY) { throw new ArgumentException("Segment must start inside the allowed box.", nameof(from)); }
            var first = new HeroActiveHit(1, 1); var found = false;
            ExitAxis(from.X, to.X, minX, maxX, ref first, ref found);
            ExitAxis(from.Y, to.Y, minY, maxY, ref first, ref found);
            hit = found ? first : default; return found;
        }

        internal static Position BeforeHit(Position from, Position to, HeroActiveHit hit)
        {
            Validate(from); Validate(to);
            if (hit.Denominator <= 0) { throw new ArgumentException("A valid hit is required.", nameof(hit)); }
            // Truncate displacement toward its origin; never round beyond the contact plane.
            return new(checked(from.X + (int)(checked(((long)to.X - from.X) * hit.Numerator) / hit.Denominator)),
                checked(from.Y + (int)(checked(((long)to.Y - from.Y) * hit.Numerator) / hit.Denominator)));
        }

        internal static bool IntersectsCircle(Position from, Position to, Position center, int radius)
        {
            Validate(from); Validate(to); Validate(center);
            if (radius < 0 || radius > CoordinateLimit) { throw new ArgumentOutOfRangeException(nameof(radius)); }
            var dx = (long)to.X - from.X; var dy = (long)to.Y - from.Y;
            var px = (long)center.X - from.X; var py = (long)center.Y - from.Y;
            var length = checked(dx * dx + dy * dy); var dot = checked(px * dx + py * dy);
            var radiusSquared = checked((long)radius * radius);
            if (length == 0 || dot <= 0) { return from.DistanceSquared(center) <= radiusSquared; }
            if (dot >= length) { return to.DistanceSquared(center) <= radiusSquared; }
            var cross = Math.Abs(checked(px * dy - py * dx));
            if (cross == 0) { return true; }
            if (radius == 0) { return false; }
            // cross² <= radius² * length, without either potentially overflowing product.
            return CompareFractions(cross, radiusSquared, length, cross) <= 0;
        }

        internal static bool TryHitCircle(Position from, Position to, Position center, int radius, out HeroActiveHit hit)
        {
            if (!IntersectsCircle(from, to, center, radius)) { hit = default; return false; }
            var dx = (long)to.X - from.X; var dy = (long)to.Y - from.Y;
            var px = (long)from.X - center.X; var py = (long)from.Y - center.Y;
            var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var a = checked(dx * dx + dy * dy); var b = checked(px * dx + py * dy);
            var c = checked(px * px + py * py - (long)radius * radius);
            if (c == 0 && b >= 0) { hit = default; return false; }
            if (c <= 0) { hit = new HeroActiveHit(0, 1); return true; }
            long safe = 0, touching = steps;
            while (touching - safe > 1)
            {
                var middle = safe + (touching - safe) / BinaryPartitionDivisor;
                if (PrefixTouches(a, b, c, middle, steps)) { touching = middle; } else { safe = middle; }
            }
            hit = new HeroActiveHit(safe, steps);
            // Axis truncation can bend a segment toward a tangent circle. In that
            // case use the safe start instead of claiming a one-quantum error bound.
            if (!IntersectsCircle(from, BeforeHit(from, to, hit), center, radius)) { return true; }
            hit = new HeroActiveHit(0, 1); return true;
        }

        private static bool PrefixTouches(long a, long b, long c, long step, long steps)
        {
            // Decimal is used only as a 96-bit integer: all operands and results are
            // integral, with no division or roots. Under CoordinateLimit, the sum of
            // absolute polynomial terms is <= 3.2768e28 (decimal max is > 7.9e28).
            var endpoint = checked((decimal)a * step * step + (decimal)BinaryPartitionDivisor * b * step * steps + (decimal)c * steps * steps);
            if (endpoint <= 0) { return true; }
            return b < 0 && checked(-(decimal)b * steps) <= checked((decimal)a * step)
                && checked((decimal)b * b) >= checked((decimal)a * c);
        }

        internal static int CompareFractions(long leftNumerator, long leftDenominator, long rightNumerator, long rightDenominator)
        {
            if (leftNumerator < 0 || rightNumerator < 0 || leftDenominator <= 0 || rightDenominator <= 0) { throw new ArgumentOutOfRangeException(nameof(leftDenominator)); }
            var direction = 1;
            while (true)
            {
                var leftWhole = leftNumerator / leftDenominator; var rightWhole = rightNumerator / rightDenominator;
                if (leftWhole != rightWhole) { return direction * leftWhole.CompareTo(rightWhole); }
                var leftRemainder = leftNumerator % leftDenominator; var rightRemainder = rightNumerator % rightDenominator;
                if (leftRemainder == 0 || rightRemainder == 0) { return direction * leftRemainder.CompareTo(rightRemainder); }
                leftNumerator = leftDenominator; leftDenominator = leftRemainder;
                rightNumerator = rightDenominator; rightDenominator = rightRemainder; direction = -direction;
            }
        }

        private static bool Clip(int from, int to, int min, int max, ref HeroActiveHit entry, ref HeroActiveHit exit)
        {
            var delta = (long)to - from;
            if (delta == 0) { return from >= min && from <= max; }
            var denominator = Math.Abs(delta);
            var near = delta > 0 ? (long)min - from : (long)from - max;
            var far = delta > 0 ? (long)max - from : (long)from - min;
            if (far < 0 || near > denominator) { return false; }
            var axisEntry = new HeroActiveHit(Math.Max(0, near), denominator);
            var axisExit = new HeroActiveHit(Math.Min(denominator, far), denominator);
            if (axisEntry.CompareTo(entry) > 0) { entry = axisEntry; }
            if (axisExit.CompareTo(exit) < 0) { exit = axisExit; }
            return entry.CompareTo(exit) <= 0;
        }

        private static void ExitAxis(int from, int to, int min, int max, ref HeroActiveHit first, ref bool found)
        {
            var delta = (long)to - from;
            if (delta == 0 || delta > 0 && to < max || delta < 0 && to > min) { return; }
            var hit = delta > 0 ? new HeroActiveHit((long)max - from, delta) : new HeroActiveHit((long)from - min, -delta);
            if (!found || hit.CompareTo(first) < 0) { first = hit; }
            found = true;
        }

        private static void ValidateBox(Position from, Position to, int minX, int minY, int maxX, int maxY)
        {
            Validate(from); Validate(to); Validate(new(minX, minY)); Validate(new(maxX, maxY));
            if (minX > maxX || minY > maxY) { throw new ArgumentException("Box bounds are reversed."); }
        }

        private static void Validate(Position position)
        {
            if (position.X < -CoordinateLimit || position.X > CoordinateLimit || position.Y < -CoordinateLimit || position.Y > CoordinateLimit)
            { throw new ArgumentOutOfRangeException(nameof(position), "Coordinate exceeds the active geometry arithmetic bound."); }
        }
    }
}
