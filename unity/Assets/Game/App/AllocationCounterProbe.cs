using System;

namespace Game.App
{
    public static class AllocationCounterProbe
    {
        [ThreadStatic] static bool calibrated;
        [ThreadStatic] static bool available;

        public static bool CurrentThreadAvailable()
        {
            if (calibrated) return available;
            calibrated = true;
            try
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[4096];
                probe[0] = 1;
                var after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(probe);
                available = after >= before && after - before >= probe.Length;
            }
            catch (NotSupportedException) { available = false; }
            catch (NotImplementedException) { available = false; }
            return available;
        }
    }
}
