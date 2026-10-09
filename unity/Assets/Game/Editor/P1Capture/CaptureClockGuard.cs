using System;

namespace Game.P1Capture
{
    public sealed class CaptureClockGuard
    {
        bool armed;
        double mediaStart, unscaledStart;

        public void Arm(double mediaSeconds, double unscaledSeconds)
        {
            if (armed) throw new InvalidOperationException("Capture clock baseline is already armed.");
            mediaStart = mediaSeconds;
            unscaledStart = unscaledSeconds;
            armed = true;
        }

        public void Validate(double mediaSeconds, double unscaledSeconds)
        {
            if (!armed) return;
            var drift = Math.Abs((mediaSeconds - mediaStart) - (unscaledSeconds - unscaledStart));
            if (double.IsNaN(drift) || double.IsInfinity(drift) || drift > .15)
                throw new InvalidOperationException($"Media/game clock drift {drift:F4}s exceeds 0.15s; normal playback is not proven.");
        }
    }
}
