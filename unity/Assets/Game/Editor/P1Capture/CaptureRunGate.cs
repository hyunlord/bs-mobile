using System;

namespace Game.P1Capture
{
    public static class CaptureRunGate
    {
        public static void RequirePausedAtZero(int tick, bool menuOpen)
        {
            if (tick != 0 || !menuOpen)
                throw new InvalidOperationException($"Recorder preparation requires the ordinary settings pause at tick zero; tick={tick}, menuOpen={menuOpen}.");
        }
    }
}
