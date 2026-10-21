using UnityEngine;

namespace Game.View
{
    // Chain-lightning presentation spec ("무기 연출 레퍼런스·이론", lightning row). Frames are 60Hz display frames.
    public static class LightningSpec
    {
        public const int TelegraphFrames = 3;      // sky flicker + ground marker before the strike (2–4)
        public const int StrikeFrames = 2;         // full brightness strike and target flash
        public const int FadeFrames = 8;           // then fades out; gone 10 frames after the strike (8–10)
        public const int HitHoldFrames = 5;        // only struck enemies freeze (4–6), electric = longer than melee
        public const float HitShakeBodyWidths = .06f;
        // Whole-screen flash only for the storm (evolution element), at half the first pilot's .13; ordinary strikes flash
        // around their targets only.
        public const int ScreenBrightenFrames = 2;
        public const float StormScreenBrighten = .065f;
        public static float ScreenBrighten(bool storm) => storm ? StormScreenBrighten : 0;
        public const float ScorchSeconds = .9f;
        // Core width 4–6 px and glow 16–24 px on a 390 pt portrait screen, scaled continuously with gear strength.
        public const float CoreMinPoints = 5, CoreMaxPoints = 6, GlowMinPoints = 20, GlowMaxPoints = 24;
        public const int MinBranches = 3, MaxBranches = 6, MaxSkyBolts = 8;
        // Strength (log2 of max(count, range) ratio) at which widths, branches and storm darkness reach their caps.
        public const float FullStrengthDoublings = 2.5f, StormDarknessMax = .4f;
        public static readonly Color Core = new Color(1f, .97f, .78f);       // electric yellow-white
        public static readonly Color Glow = new Color(.62f, .38f, 1f);       // violet bloom
        public static readonly Color Scorch = new Color(.12f, .09f, .07f);
        public static readonly Color StormSky = new Color(.06f, .05f, .16f);
        public static float Seconds(int frames) => frames / 60f;
        public static float Strength(float ratio) => Mathf.Clamp01(Mathf.Log(Mathf.Max(1, ratio), 2) / FullStrengthDoublings);
        public static float CorePoints(float strength) => Mathf.Lerp(CoreMinPoints, CoreMaxPoints, strength);
        public static float GlowPoints(float strength) => Mathf.Lerp(GlowMinPoints, GlowMaxPoints, strength);
        public static int Branches(float strength) => Mathf.RoundToInt(Mathf.Lerp(MinBranches, MaxBranches, strength));
        public static float StormDarkness(float strength) => Mathf.Lerp(StormDarknessMax * .55f, StormDarknessMax, strength);

        // Opacity of a strike that became visible at time 0 (after its telegraph).
        public static float StrikeOpacity(float age)
        {
            if (age < 0) return 0;
            var strike = Seconds(StrikeFrames);
            return age < strike ? 1 : 1 - Mathf.Clamp01((age - strike) / Seconds(FadeFrames));
        }
    }

    // Deterministic zigzag polyline by midpoint displacement; the same event id always draws the same bolt.
    public static class LightningBolt
    {
        public const int Generations = 4;          // 16 segments
        public static int PointCount(int generations) => (1 << generations) + 1;

        public static uint Hash(long id, int salt)
        {
            unchecked
            {
                var h = (uint)id * 2654435761u ^ (uint)(id >> 32) * 2246822519u ^ (uint)salt * 3266489917u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return h;
            }
        }

        static float Next(ref uint state)
        {
            unchecked { state = state * 1664525u + 1013904223u; }
            return (state >> 8) / 16777216f;
        }

        // Writes PointCount(generations) points from a to b into output[start..]; jaggedness is the first offset as a
        // fraction of the bolt length, halving each generation.
        public static void Build(Vector2 a, Vector2 b, uint seed, float jaggedness, int generations, Vector2[] output, int start)
        {
            var count = PointCount(generations);
            output[start] = a; output[start + count - 1] = b;
            var span = b - a; var length = span.magnitude;
            var normal = length > .00001f ? new Vector2(-span.y, span.x) / length : Vector2.up;
            var state = seed | 1;
            var offset = jaggedness * length;
            for (var step = count - 1; step > 1; step >>= 1, offset *= .5f)
                for (var i = 0; i + step < count; i += step)
                {
                    var mid = i + step / 2;
                    output[start + mid] = (output[start + i] + output[start + i + step]) * .5f + normal * ((Next(ref state) * 2 - 1) * offset);
                }
        }

        // A branch leaves the main bolt at a point and runs forward-sideways for a fraction of its length.
        public static void Branch(Vector2[] main, int mainStart, int mainCount, uint seed, out Vector2 from, out Vector2 to)
        {
            var state = seed | 1;
            var index = mainStart + 2 + Mathf.Min(mainCount - 5, (int)(Next(ref state) * (mainCount - 4)));
            from = main[index];
            var forward = main[mainStart + mainCount - 1] - main[mainStart];
            var length = forward.magnitude * Mathf.Lerp(.18f, .34f, Next(ref state));
            var angle = Mathf.Lerp(25, 50, Next(ref state)) * (Next(ref state) < .5f ? -1 : 1);
            var direction = forward.sqrMagnitude > .000001f ? (Vector2)(Quaternion.Euler(0, 0, angle) * forward.normalized) : Vector2.right;
            to = from + direction * length;
        }
    }
}
