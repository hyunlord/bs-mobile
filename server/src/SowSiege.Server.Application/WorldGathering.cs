using System.Numerics;

namespace SowSiege.Server.Application;

/// <summary>Gameplay quantities are supplied by versioned content; this type has no tuning defaults.</summary>
public sealed record GatheringPolicy(int Stock, int UnitsPerSoldierMinute, int CapacityPerSoldier, long RespawnMilliseconds);
public sealed record GatheringLease(Guid MarchId, long ArrivalOrder, int Soldiers, long StartedAtMs, long DueAtMs, int Cargo);
public sealed record GatheringNode(string TargetId, long Generation, GatheringPolicy Policy, int RemainingStock,
    int ConfirmedGathered, long UtcHighWatermark, long LastArrivalOrder, GatheringLease? Lease, long? RespawnAtMs);
public sealed record GatheringCompletion(string TargetId, long Generation, Guid MarchId, long ArrivalOrder, int Cargo, long AtMs, string Reason);
public sealed record GatheringGeneration(string TargetId, long Generation, int CreatedStock, int ConfirmedGathered);
public sealed record GatheringTransition(GatheringNode State, bool Done, string Reason,
    GatheringCompletion? Completion = null, GatheringGeneration? RetiredGeneration = null);

/// <summary>
/// Pure GDD18 resource-node transitions. The store must serialize calls, bind one immutable arrival order to each march,
/// and persist node plus completion/retired-generation records in one transaction. Arrival order is allocated when arrival
/// is first committed, not when a march departs. It therefore increases in actual arrival order, even for crossing routes.
/// The bounded watermark rejects replayed arrivals without an ever-growing receipt list inside each node.
/// This layer neither credits a wallet nor proves PostgreSQL concurrency or return settlement.
/// </summary>
public static class WorldGathering
{
    private const long MillisecondsPerMinute = 60_000;

    public static GatheringNode Create(string targetId, long generation, GatheringPolicy policy, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var node = new GatheringNode(targetId, generation, policy, policy.Stock, 0, nowMs, 0, null, null);
        Validate(node);
        return node;
    }

    public static GatheringTransition Claim(GatheringNode node, long generation, Guid marchId, long arrivalOrder, int soldiers, long nowMs)
    {
        Validate(node);
        if (marchId == Guid.Empty || arrivalOrder <= 0 || soldiers <= 0) { return new(node, false, "invalid-arrival"); }
        if (generation != node.Generation) { return new(node, false, "stale-generation"); }
        if (arrivalOrder <= node.LastArrivalOrder)
        {
            // Only the exact active lease is repeatable; rejected or completed claims never reacquire the source.
            bool active = node.Lease is { } lease && lease.ArrivalOrder == arrivalOrder && lease.MarchId == marchId && lease.Soldiers == soldiers;
            return new(node, active, active ? "already-collecting" : "arrival-already-observed");
        }
        var advanced = Advance(node, nowMs);
        var current = advanced.State with { LastArrivalOrder = arrivalOrder };
        if (current.Lease is not null) { return advanced with { State = current, Done = false, Reason = "occupied" }; }
        if (current.RemainingStock == 0) { return advanced with { State = current, Done = false, Reason = "depleted" }; }
        long capacity = checked((long)soldiers * current.Policy.CapacityPerSoldier);
        long rate = checked((long)soldiers * current.Policy.UnitsPerSoldierMinute);
        long units = Math.Min(current.RemainingStock, capacity);
        long duration = CeilingDivide(checked(units * MillisecondsPerMinute), rate);
        var claimed = new GatheringLease(marchId, arrivalOrder, soldiers, current.UtcHighWatermark,
            checked(current.UtcHighWatermark + duration), 0);
        return advanced with { State = current with { Lease = claimed }, Done = true, Reason = "collecting" };
    }

    /// <summary>Progress uses total elapsed time, so splitting observations cannot lose fractional production.</summary>
    public static GatheringTransition Advance(GatheringNode node, long nowMs)
    {
        Validate(node);
        long now = Math.Max(node.UtcHighWatermark, nowMs);
        var current = node with { UtcHighWatermark = now };
        if (current.Lease is not { } lease) { return new(current, true, "observed"); }
        long rate = checked((long)lease.Soldiers * node.Policy.UnitsPerSoldierMinute);
        long capacity = checked((long)lease.Soldiers * node.Policy.CapacityPerSoldier);
        long effectiveNow = Math.Min(now, lease.DueAtMs);
        long sourceAtStart = checked((long)node.RemainingStock + lease.Cargo);
        long limit = Math.Min(sourceAtStart, capacity);
        // BigInteger prevents a very late observation from overflowing elapsed*rate before the stock bound is applied.
        var produced = (BigInteger)(effectiveNow - lease.StartedAtMs) * rate / MillisecondsPerMinute;
        int cargo = checked((int)BigInteger.Min(limit, produced));
        int delta = checked(cargo - lease.Cargo);
        current = current with
        {
            RemainingStock = checked(current.RemainingStock - delta),
            ConfirmedGathered = checked(current.ConfirmedGathered + delta),
            Lease = lease with { Cargo = cargo }
        };
        if (now < lease.DueAtMs) { return new(current, true, "collecting"); }
        return Release(current, lease.DueAtMs, current.RemainingStock == 0 ? "depleted" : "capacity-reached");
    }

    public static GatheringTransition Cancel(GatheringNode node, long generation, Guid marchId, long arrivalOrder, long nowMs)
    {
        Validate(node);
        if (generation != node.Generation) { return new(node, false, "stale-generation"); }
        if (node.Lease is not { } lease || lease.MarchId != marchId || lease.ArrivalOrder != arrivalOrder)
        {
            return new(node, false, "lease-not-owned");
        }
        var advanced = Advance(node, nowMs);
        // A deadline crossed before cancellation already released the cargo at that earlier deadline.
        return advanced.Completion is not null ? advanced : Release(advanced.State, advanced.State.UtcHighWatermark, "cancelled");
    }

    /// <summary>The scheduler invokes this due event after any earlier gathering completion, preserving that receipt.</summary>
    public static GatheringTransition Respawn(GatheringNode node, long generation, long nowMs)
    {
        Validate(node);
        if (generation != node.Generation) { return new(node, false, "stale-generation"); }
        var advanced = Advance(node, nowMs);
        var current = advanced.State;
        if (current.RemainingStock != 0 || current.Lease is not null || current.RespawnAtMs is not { } due)
        {
            return advanced with { Done = false, Reason = "not-depleted" };
        }
        if (current.UtcHighWatermark < due) { return advanced with { Done = false, Reason = "respawn-not-due" }; }
        var retired = new GatheringGeneration(current.TargetId, current.Generation, current.Policy.Stock, current.ConfirmedGathered);
        var next = current with
        {
            Generation = checked(current.Generation + 1), RemainingStock = current.Policy.Stock,
            ConfirmedGathered = 0, Lease = null, RespawnAtMs = null
        };
        return advanced with { State = next, Done = true, Reason = "respawned", RetiredGeneration = retired };
    }

    private static GatheringTransition Release(GatheringNode node, long atMs, string reason)
    {
        var lease = node.Lease ?? throw new InvalidDataException("Gathering release needs a lease.");
        var completed = new GatheringCompletion(node.TargetId, node.Generation, lease.MarchId, lease.ArrivalOrder, lease.Cargo, atMs, reason);
        var released = node with
        {
            Lease = null,
            RespawnAtMs = node.RemainingStock == 0 ? checked(atMs + node.Policy.RespawnMilliseconds) : null
        };
        Validate(released);
        return new(released, true, reason, completed);
    }

    private static long CeilingDivide(long numerator, long denominator) => numerator / denominator + (numerator % denominator == 0 ? 0 : 1);

    public static void Validate(GatheringNode node)
    {
        if (string.IsNullOrWhiteSpace(node.TargetId) || node.Generation <= 0 || node.Policy is not { Stock: > 0, UnitsPerSoldierMinute: > 0, CapacityPerSoldier: > 0, RespawnMilliseconds: > 0 } ||
            node.UtcHighWatermark < 0 || node.LastArrivalOrder < 0 || node.RemainingStock < 0 || node.ConfirmedGathered < 0 ||
            (long)node.RemainingStock + node.ConfirmedGathered != node.Policy.Stock)
        {
            throw new InvalidDataException("Invalid gathering source ledger.");
        }
        if ((node.RemainingStock == 0) != (node.RespawnAtMs is not null) || node.RespawnAtMs is < 0)
        {
            throw new InvalidDataException("Invalid gathering respawn deadline.");
        }
        if (node.Lease is not { } lease) { return; }
        if (node.RemainingStock == 0 || node.RespawnAtMs is not null || lease.MarchId == Guid.Empty || lease.ArrivalOrder <= 0 ||
            lease.ArrivalOrder > node.LastArrivalOrder || lease.Soldiers <= 0 || lease.Cargo < 0 || lease.Cargo > node.ConfirmedGathered ||
            lease.Cargo >= checked((long)lease.Soldiers * node.Policy.CapacityPerSoldier) || lease.StartedAtMs < 0 ||
            lease.StartedAtMs > node.UtcHighWatermark || lease.DueAtMs <= node.UtcHighWatermark)
        {
            throw new InvalidDataException("Invalid gathering lease.");
        }
        long units = Math.Min(checked((long)node.RemainingStock + lease.Cargo), checked((long)lease.Soldiers * node.Policy.CapacityPerSoldier));
        long duration = CeilingDivide(checked(units * MillisecondsPerMinute), checked((long)lease.Soldiers * node.Policy.UnitsPerSoldierMinute));
        if (lease.DueAtMs != checked(lease.StartedAtMs + duration)) { throw new InvalidDataException("Gathering deadline does not match source and capacity."); }
        var expectedCargo = (BigInteger)(node.UtcHighWatermark - lease.StartedAtMs) * lease.Soldiers * node.Policy.UnitsPerSoldierMinute / MillisecondsPerMinute;
        if (expectedCargo != lease.Cargo) { throw new InvalidDataException("Gathering cargo does not match the observed clock."); }
    }
}
