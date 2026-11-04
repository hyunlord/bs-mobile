namespace SowSiege.Server.Infrastructure;

/// <summary>Operational scheduling limit; does not change persisted gameplay deadlines.</summary>
public sealed class WorldRecoveryOptions
{
    public WorldRecoveryOptions(int batchSize = 128)
    {
        if (batchSize is < 1 or > 4096) { throw new ArgumentOutOfRangeException(nameof(batchSize), "Recovery batch size must be between 1 and 4096."); }
        BatchSize = batchSize;
    }

    public int BatchSize { get; }
}

/// <summary>
/// ProcessedEvents counts selected due events, including combat attempts whose optimistic commit loses.
/// CommittedEvents counts durable event transitions, including a successfully rescheduled combat recovery.
/// CombatRecoveryAttempts counts calls made after releasing locks for combat replay.
/// HasMore and OldestDueAtMs describe remaining overdue work at the final locked observation;
/// OldestDueAtMs is null when none remains, and never describes a future event.
/// Concurrent callers may change the backlog after this observation.
/// </summary>
public sealed record WorldRecoveryBatchResult(int ProcessedEvents, int CommittedEvents,
    int CombatRecoveryAttempts, bool HasMore, long? OldestDueAtMs);

/// <summary>The bounded recovery pass left overdue work; retry admission without recording a command receipt.</summary>
public sealed class WorldRecoveryPendingException : Exception
{
    public WorldRecoveryPendingException() : base("world-recovery-pending") { }
}
