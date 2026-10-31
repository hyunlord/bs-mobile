namespace SowSiege.Server.Application;

public sealed record ClockState(long HighWatermarkMilliseconds, long LogicalSeconds, int RemainderMilliseconds);
public sealed record ClockObservation(ClockState State, long CreditedSeconds, string Reason);

public static class EstateClock
{
    public static ClockObservation Observe(ClockState previous, long nowMilliseconds, int capSeconds)
    {
        if (nowMilliseconds < previous.HighWatermarkMilliseconds)
        {
            return new(previous, 0, "clock-backward");
        }
        long elapsed = checked(nowMilliseconds - previous.HighWatermarkMilliseconds + previous.RemainderMilliseconds);
        long whole = elapsed / 1000;
        bool capped = elapsed > (long)capSeconds * 1000;
        long credited = Math.Min(whole, capSeconds);
        int remainder = capped ? 0 : (int)(elapsed % 1000);
        return new(new(nowMilliseconds, checked(previous.LogicalSeconds + credited), remainder), credited,
            capped ? "offline-capped" : "accepted");
    }
}
