using System;

namespace SowSiege.Core
{
    public sealed record FeelExperimentOptions(bool FasterLord = false, bool ImmediateContact = false, bool ReadableHealth = false)
    {
        public static FeelExperimentOptions? Normalize(FeelExperimentOptions? value) => value is { } && (value.FasterLord || value.ImmediateContact || value.ReadableHealth) ? value : null;
    }

    internal sealed class FeelExperimentState
    {
        internal const int LordBodyWidth = 300;
        internal const int ImmunityTicks = 15;
        internal const int Health = 100;
        internal const int MinimumDamage = 5;
        internal const int MaximumDamage = 15;
        internal const int SpeedNumerator = 13;
        internal const int SpeedDenominator = 10;
        internal const int KnockbackSteps = 3;
        public FeelExperimentOptions Options;
        public long RemainderX;
        public long RemainderY;
        public int PreviousX;
        public int PreviousY;
        public int InvulnerableUntil;
        internal FeelExperimentState(FeelExperimentOptions options) { Options = options; }
        internal int MaximumHealth(int original) => Options.ReadableHealth ? Health : original;
        internal int Damage(int original) => Options.ReadableHealth ? Math.Clamp(original, MinimumDamage, MaximumDamage) : original;
        internal Position Move(Position current, PlayerInput input, int speed, MapTuning map)
        {
            var x = Axis(current.X, input.X, speed, map.Width, ref RemainderX, ref PreviousX);
            var y = Axis(current.Y, input.Y, speed, map.Height, ref RemainderY, ref PreviousY);
            return new(x, y);
        }
        private static int Axis(int position, int input, int speed, int maximum, ref long remainder, ref int previous)
        {
            if (input == 0 || Math.Sign(input) != Math.Sign(previous)) { remainder = 0; }
            previous = input;
            var numerator = (long)input * speed * SpeedNumerator + remainder;
            const int denominator = PlayerInput.Scale * SpeedDenominator;
            var delta = numerator / denominator;
            remainder = numerator % denominator;
            var target = Math.Clamp(position + delta, 0, maximum);
            if (target == 0 || target == maximum) { remainder = 0; }
            return (int)target;
        }
    }
}
