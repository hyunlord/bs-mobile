using SowSiege.Core;

namespace SowSiege.Server.Application;

public sealed record WorldInputFrontier(long UtcHighWatermark, long LastReceivedAtMs, int ConfirmedTick, long NextSequence);
public sealed record WorldTroopSettlement(string Id, int N, int K, int Survivors, int Admitted, int Lost);

/// <summary>UTC/grace and return-settlement policy; neither changes replay simulation.</summary>
public static class WorldCombatInput
{
    public static WorldInputFrontier Observe(WorldInputFrontier frontier, long now)
    {
        if (now < 0 || frontier.UtcHighWatermark < frontier.LastReceivedAtMs || frontier.LastReceivedAtMs < 0 ||
            frontier.ConfirmedTick < 0 || frontier.NextSequence < 0)
            { throw new ArgumentOutOfRangeException(nameof(frontier)); }
        return frontier with { UtcHighWatermark = Math.Max(frontier.UtcHighWatermark, now) };
    }

    public static long GraceDeadline(WorldInputFrontier frontier, int grace)
    {
        if (grace <= 0) { throw new ArgumentOutOfRangeException(nameof(grace)); }
        return checked(frontier.LastReceivedAtMs + grace);
    }

    public static long AbsoluteDeadline(long issuedAt, int tickRate, int limit, int grace)
    {
        if (issuedAt < 0 || tickRate <= 0 || limit < 0 || grace <= 0)
        { throw new ArgumentOutOfRangeException(nameof(issuedAt)); }
        // Ceil preserves the last fractional tick, while the data grace is added only once.
        return checked(issuedAt + (long)decimal.Ceiling((decimal)limit * 1000 / tickRate) + grace);
    }

    public static long VerificationDeadline(long now, long maximumWait, long absoluteDeadline)
    {
        if (now < 0 || maximumWait <= 0 || now > absoluteDeadline)
        { throw new ArgumentOutOfRangeException(nameof(now)); }
        return checked((long)Math.Min((decimal)now + maximumWait + 1, (decimal)absoluteDeadline + 1));
    }

    public static int DueTick(WorldInputFrontier frontier, long issuedAt, long now, int tickRate, int limit, int grace)
    {
        frontier = Observe(frontier, now);
        if (issuedAt < 0 || issuedAt > frontier.LastReceivedAtMs || tickRate <= 0 || limit < frontier.ConfirmedTick)
            { throw new ArgumentOutOfRangeException(nameof(issuedAt)); }
        if (frontier.UtcHighWatermark > AbsoluteDeadline(issuedAt, tickRate, limit, grace)) { return limit; }
        if (frontier.UtcHighWatermark <= GraceDeadline(frontier, grace)) { return frontier.ConfirmedTick; }
        var elapsed = frontier.UtcHighWatermark - issuedAt;
        // Decimal avoids overflow for a valid UTC long multiplied by the tick rate.
        var tick = (int)Math.Min(limit, decimal.Floor((decimal)elapsed * tickRate / 1000));
        return Math.Max(frontier.ConfirmedTick, tick);
    }

    public static WorldInputFrontier Accepted(WorldInputFrontier frontier, long now, int tick, long nextSequence)
    {
        frontier = Observe(frontier, now);
        if (tick < frontier.ConfirmedTick || nextSequence < frontier.NextSequence)
            { throw new ArgumentOutOfRangeException(nameof(nextSequence)); }
        if (nextSequence == frontier.NextSequence)
        {
            if (tick != frontier.ConfirmedTick) { throw new ArgumentException("Duplicate prefix cannot change its tick."); }
            return frontier;
        }
        return frontier with
        {
            LastReceivedAtMs = tick > frontier.ConfirmedTick ? frontier.UtcHighWatermark : frontier.LastReceivedAtMs,
            ConfirmedTick = tick,
            NextSequence = nextSequence
        };
    }

    public static WorldTroopSettlement[] SettleLosses(IReadOnlyList<WorldTroopResult> troops, int injuryPercent, int availableBeds)
    {
        if (injuryPercent is < 0 or > 100 || availableBeds < 0) { throw new ArgumentOutOfRangeException(nameof(injuryPercent)); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<WorldTroopSettlement>(troops.Count);
        foreach (var troop in troops.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(troop.Id) || !seen.Add(troop.Id) || troop.N < 0 || troop.K < 0 || troop.K > troop.N)
                { throw new ArgumentException("Invalid or duplicate troop casualty result.", nameof(troops)); }
            var injured = checked((int)((long)troop.K * injuryPercent / 100));
            var admitted = Math.Min(injured, availableBeds);
            availableBeds -= admitted;
            result.Add(new(troop.Id, troop.N, troop.K, troop.N - troop.K, admitted, troop.K - admitted));
        }
        return result.ToArray();
    }
}
