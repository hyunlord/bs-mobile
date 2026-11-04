using SowSiege.Core;
using SowSiege.Sim;

namespace SowSiege.Server.Infrastructure;

public sealed record WorldPrefixExecution(ReplayCommand[] Commands, int Tick, long NextSequence, WorldCombatResult? Result, byte[]? Recovery);

/// <summary>Bounded Core re-execution. Call outside database locks and commit against the captured run revision.</summary>
public sealed class WorldCombatExecutor
{
    private readonly RunVerifierOptions options;
    private readonly SemaphoreSlim slots;
    public ContentCatalog Catalog { get; }
    public string DataHash { get; }
    // Admission and execution each have a timeout; the durable receipt must cover both waits.
    public long VerificationTimeoutMilliseconds => checked((long)Math.Ceiling(options.Timeout.TotalMilliseconds * 2));

    public WorldCombatExecutor(RunVerifierOptions options)
    {
        if (options.MaxReplayBytes <= 0 || options.MaxConcurrent <= 0 || options.Timeout <= TimeSpan.Zero || options.PlayTimeToleranceMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        this.options = options;
        slots = new(options.MaxConcurrent);
        Catalog = ContentLoader.Load(options.DataDirectory, profileName: options.Profile);
        DataHash = ContentLoader.Hash(options.DataDirectory, false);
    }

    public InteractiveOptions ExpectedOptions(WorldCombatSnapshot snapshot) =>
        new(new RunOptions(snapshot.Provenance.Seed, snapshot.Hero.Id, Catalog.Tuning.DefaultEstate, "manual", ManualCards: true),
            AimMode.Movement, DataHash, WorldCombat: WorldCombatCodec.Freeze(new(snapshot)));

    public Task<WorldPrefixExecution> ValidatePrefixAsync(InteractiveOptions frozenOptions, ReplayCommand[] accepted,
        ReplayCommand[] proposed, long issuedAt, long now, CancellationToken cancellation) => BudgetAsync(() =>
    {
        CheckPrefix(accepted, proposed, false);
        var policy = frozenOptions.WorldCombat?.Snapshot.Input ?? throw new InvalidDataException("Missing world snapshot.");
        if (proposed.Length - accepted.Length > policy.MaximumChunkCommands)
        {
            throw new InvalidDataException("Chunk command budget exceeded.");
        }

        using var chunk = new MemoryStream();
        for (var i = accepted.Length; i < proposed.Length; i++)
        {
            ReplayCodec.WriteCommand(chunk, proposed[i]);
        }

        if (chunk.Length > policy.MaximumChunkBytes)
        {
            throw new InvalidDataException("Chunk byte budget exceeded.");
        }

        var session = Execute(frozenOptions, proposed);
        CheckTime(issuedAt, now, session.View.CaptureFrame().Tick);
        return Capture(session, proposed);
    }, cancellation);

    public Task<WorldPrefixExecution> RecoverAsync(InteractiveOptions frozenOptions, ReplayCommand[] accepted,
        int throughTick, CancellationToken cancellation) => BudgetAsync(() =>
    {
        if (throughTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(throughTick));
        }

        var session = Execute(frozenOptions, accepted);
        var commands = accepted.ToList();
        using var encoded = new MemoryStream();
        foreach (var command in accepted)
        {
            ReplayCodec.WriteCommand(encoded, command);
        }

        while (session.View.Status != RunStatus.Completed && session.View.CaptureFrame().Tick < throughTick)
        {
            var tick = session.View.CaptureFrame().Tick;
            var command = session.View.Status == RunStatus.AwaitingCard
                ? new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard,
                    CardId: session.View.CaptureCards().Cards.OrderBy(x => x, StringComparer.Ordinal).First())
                : new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance);
            ReplayCodec.WriteCommand(encoded, command);
            if (encoded.Length > options.MaxReplayBytes)
            {
                throw new InvalidDataException("Recovery prefix budget exceeded.");
            }

            session.Apply(command);
            commands.Add(command);
        }
        return Capture(session, commands.ToArray());
    }, cancellation);

    public Task<WorldCombatResult> VerifyAsync(InteractiveOptions frozenOptions, ReplayCommand[] accepted,
        byte[] replayBytes, long issuedAt, long now, CancellationToken cancellation) => BudgetAsync(() =>
    {
        if (replayBytes.Length == 0 || replayBytes.Length > options.MaxReplayBytes)
        {
            throw new InvalidDataException("Replay byte budget exceeded.");
        }

        using var stream = new MemoryStream(replayBytes, false);
        var replay = ReplayCodec.ReadWorld(stream);
        using var expectedHeader = new MemoryStream();
        using var actualHeader = new MemoryStream();
        ReplayCodec.WriteHeader(expectedHeader, ReplayCodec.Header(frozenOptions));
        ReplayCodec.WriteHeader(actualHeader, replay.Header);
        if (!expectedHeader.ToArray().AsSpan().SequenceEqual(actualHeader.ToArray()))
        {
            throw new InvalidDataException("Frozen contract mismatch.");
        }

        CheckPrefix(accepted, replay.Commands, true);
        CheckTime(issuedAt, now, replay.End.Result.TickOrRound);
        var result = WorldReplayRunner.Verify(Catalog, DataHash, replay);
        CheckTroops(frozenOptions, result);
        return result;
    }, cancellation);

    public Task<ArmyAutoResult> ResolveAutoAsync(ArmyAutoInput input, CancellationToken cancellation) => BudgetAsync(() =>
    {
        if (input.Mode is not (WorldCombatMode.AutoPve or WorldCombatMode.CityPvp))
        {
            throw new InvalidDataException("Invalid auto mode.");
        }

        return ArmyAuto.Resolve(input);
    }, cancellation);

    private InteractiveSession Execute(InteractiveOptions frozenOptions, IReadOnlyList<ReplayCommand> commands)
    {
        if (frozenOptions.WorldCombat?.Snapshot.Mode != WorldCombatMode.DirectPve || frozenOptions.DataHash != DataHash)
        {
            throw new InvalidDataException("Unavailable direct world contract/catalog.");
        }

        var session = new InteractiveSession(Catalog, frozenOptions);
        using var encoded = new MemoryStream();
        foreach (var command in commands)
        {
            CheckCommand(command);
            ReplayCodec.WriteCommand(encoded, command);
            if (encoded.Length > options.MaxReplayBytes)
            {
                throw new InvalidDataException("Prefix byte budget exceeded.");
            }

            session.Apply(command);
        }
        return session;
    }

    private static WorldPrefixExecution Capture(InteractiveSession session, ReplayCommand[] commands) =>
        new(commands.ToArray(), session.View.CaptureFrame().Tick, session.NextSequence,
            session.View.Status == RunStatus.Completed ? session.GetWorldCombatResult() : null,
            session.View.Status == RunStatus.Completed ? null : WorldRecoveryCodec.Capture(session, commands));

    private static void CheckPrefix(IReadOnlyList<ReplayCommand> accepted, IReadOnlyList<ReplayCommand> proposed, bool exact)
    {
        if (proposed.Count < accepted.Count || exact && proposed.Count != accepted.Count)
        {
            throw new InvalidDataException("Prefix length mismatch.");
        }

        for (var i = 0; i < proposed.Count; i++)
        {
            CheckCommand(proposed[i]);
            if (i < accepted.Count && proposed[i] != accepted[i])
            {
                throw new InvalidDataException("Accepted input is immutable.");
            }
        }
    }

    private static void CheckCommand(ReplayCommand command)
    {
        if (command.Kind is not (ReplayCommandKind.Advance or ReplayCommandKind.ChooseCard or ReplayCommandKind.RerollCards or
            ReplayCommandKind.BanCard or ReplayCommandKind.LockCard or ReplayCommandKind.SetAimMode))
        {
            throw new InvalidDataException("Non-player command rejected.");
        }
    }

    private void CheckTime(long issuedAt, long now, int tick)
    {
        if (issuedAt < 0 || now < issuedAt || tick < 0 ||
            now - issuedAt < (long)tick * 1000 / Catalog.Tuning.TickRate - options.PlayTimeToleranceMilliseconds)
        {
            throw new InvalidDataException("Replay advances ahead of server UTC.");
        }
    }

    private static void CheckTroops(InteractiveOptions expected, WorldCombatResult result)
    {
        var troops = expected.WorldCombat!.Snapshot.Troops;
        if (result.Troops.Count != troops.Count)
        {
            throw new InvalidDataException("Troop result mismatch.");
        }

        for (var i = 0; i < troops.Count; i++)
        {
            if (result.Troops[i].Id != troops[i].Id || result.Troops[i].N != troops[i].N || result.Troops[i].K < 0 || result.Troops[i].K > troops[i].N)
            {
                throw new InvalidDataException("Troop result mismatch.");
            }
        }
    }

    private async Task<T> BudgetAsync<T>(Func<T> execute, CancellationToken cancellation)
    {
        if (!await slots.WaitAsync(options.Timeout, cancellation))
        {
            throw new TimeoutException("World verification queue exhausted.");
        }

        var work = Task.Run(execute, CancellationToken.None);
        // Core is synchronous: timeout does not release admission until actual CPU work finishes.
        _ = work.ContinueWith(completed => { _ = completed.Exception; slots.Release(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return await work.WaitAsync(options.Timeout, cancellation);
    }
}
