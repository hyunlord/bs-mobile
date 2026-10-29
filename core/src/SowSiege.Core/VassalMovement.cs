using System;

namespace SowSiege.Core
{
    internal static class VassalMovement
    {
        internal static Position AfterMove(Position origin, Position moved, VassalRunState state)
        {
            var multiplier = 1000L + state.Snapshot.Effects[(int)MetaAbility.Movement].Bonus;
            return new(Axis(origin.X, moved.X, multiplier), Axis(origin.Y, moved.Y, multiplier));
        }

        private static int Axis(int origin, int moved, long multiplier)
        {
            var delta = (long)moved - origin;
            var scaled = checked(Math.Abs(delta) * multiplier) / 1000;
            return checked((int)(origin + Math.Sign(delta) * scaled));
        }
    }
}
