using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Sim;

namespace SowSiege.Server.Infrastructure;

/// <summary>Verification budget (server configuration): replay size, wall-clock timeout and concurrent re-executions.</summary>
public sealed record RunVerifierOptions(string DataDirectory, string Profile, int MaxReplayBytes, TimeSpan Timeout, int MaxConcurrent,
    long MinSubmitIntervalMilliseconds = 10_000, long PlayTimeToleranceMilliseconds = 2_000);

/// <summary>
/// ADR0077 replay settlement. Re-executes a submitted replay with the shared Core (read-only use of
/// <see cref="ReplayRunner.Verify"/>) outside the account transaction, then settles the verdict once
/// through the account writer. The client never supplies seed, options, result hash or reward.
/// </summary>
public sealed class RunVerifier
{
    // Normal play commands only. Debug, test and host-reward commands are rejected (ADR0077 "debug replay 거부").
    private static readonly HashSet<ReplayCommandKind> Allowed =
        [ReplayCommandKind.Advance, ReplayCommandKind.ChooseCard, ReplayCommandKind.RerollCards, ReplayCommandKind.BanCard, ReplayCommandKind.LockCard, ReplayCommandKind.SetAimMode];

    private readonly RunVerifierOptions options;
    private readonly ContentCatalog catalog;
    private readonly SemaphoreSlim slots;

    public string DataHash { get; }

    public RunVerifier(RunVerifierOptions options)
    {
        this.options = options;
        catalog = ContentLoader.Load(options.DataDirectory, profileName: options.Profile);
        DataHash = ContentLoader.Hash(options.DataDirectory, false);
        slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrent));
    }

    /// <summary>The exact options a replay header must carry for a run issued with this seed.</summary>
    public InteractiveOptions ExpectedOptions(int seed) =>
        new(new RunOptions(seed, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, DataHash);

    /// <summary>
    /// Real play time: a replay of N ticks cannot be submitted sooner than N/tickRate seconds after issue (minus a small tolerance).
    /// Offline-generated replays submitted right after issue are rejected here, before any Core re-execution.
    /// </summary>
    public bool PlayedLongEnough(long issuedAtMs, long nowMs, int ticks) =>
        nowMs - issuedAtMs >= (long)ticks * 1000 / catalog.Tuning.TickRate - options.PlayTimeToleranceMilliseconds;

    public async Task<ExpeditionVerdict> VerifyAsync(Guid runId, int seed, byte[] replay, long issuedAtMs, long nowMs, CancellationToken cancellation)
    {
        string replayHash = Convert.ToHexString(SHA256.HashData(replay)).ToLowerInvariant();
        ExpeditionVerdict Reject(string verdict) => new(runId, verdict, null, 0, null, replayHash);
        if (replay.Length == 0 || replay.Length > options.MaxReplayBytes)
        {
            return Reject("too-large");
        }
        ReplayDocument document;
        try
        {
            using var input = new MemoryStream(replay, false);
            document = ReplayCodec.Read(input);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or ArgumentException or FormatException or DecoderFallbackException)
        {
            return Reject("malformed");
        }
        if (!PlayedLongEnough(issuedAtMs, nowMs, document.End.Tick))
        {
            return Reject("too-early");
        }
        if (document.Header.Options != ExpectedOptions(seed))
        {
            return Reject("options-mismatch");
        }
        if (document.Commands.Any(c => !Allowed.Contains(c.Kind)))
        {
            return Reject("debug-command");
        }
        if (!await slots.WaitAsync(options.Timeout, cancellation))
        {
            return Reject("timeout");
        }
        var work = Task.Run(() => ReplayRunner.Verify(catalog, DataHash, document), CancellationToken.None);
        // The Core run cannot be interrupted; it holds its slot until it ends so the CPU budget stays bounded.
        _ = work.ContinueWith(_ => slots.Release(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        if (await Task.WhenAny(work, Task.Delay(options.Timeout, cancellation)) != work)
        {
            return Reject("timeout");
        }
        try
        {
            var result = await work;
            return result.Seed == seed ? new(runId, "verified", result.EndKind.ToString(), result.Tick, result.StateHash, replayHash) : Reject("options-mismatch");
        }
        catch (InvalidDataException)
        {
            return Reject("mismatch");
        }
    }

    /// <summary>Verify then settle once. A later or concurrent submission for the same run settles to already-settled; the run status, not the request, prevents a second grant.</summary>
    public async Task<string> SubmitAsync(AccountStore store, Guid accountId, Guid runId, byte[] replay, CancellationToken cancellation)
    {
        var view = JsonSerializer.Deserialize<AccountReply>(await store.ExecuteAccountAsync(accountId, null, cancellation), AccountStore.Json)!;
        var runs = view.Domains[ResearchDomain.Name].GetProperty("runs").Deserialize<ExpeditionRun[]>(AccountStore.Json) ?? [];
        var run = runs.SingleOrDefault(r => r.RunId == runId) ?? throw new CommandConflictException("unknown-run");
        long now = view.Domains[ResearchDomain.Name].GetProperty("serverUtcMs").GetInt64();
        if (run.Status == "open" && run.LastSubmitAtMs > 0 && now - run.LastSubmitAtMs < options.MinSubmitIntervalMilliseconds)
        {
            // Spacing per run (one open run per account): bounds how often one account can occupy the shared verify slot.
            throw new CommandConflictException("submit-too-soon");
        }
        var verdict = await VerifyAsync(runId, run.Seed, replay, run.IssuedAtMs, now, cancellation);
        if (verdict.Verdict == "too-early")
        {
            // Cheap to detect and never re-executed, so nothing is recorded.
            throw new CommandConflictException("too-early");
        }
        var commandId = Guid.NewGuid();
        long version = view.Estate.Version;
        for (int attempt = 0; ; attempt++)
        {
            var command = new AccountCommand(commandId, version, ResearchDomain.Name, ExpeditionRules.SettleOperation, JsonSerializer.SerializeToElement(verdict, AccountStore.Json));
            try
            {
                return await store.ExecuteAccountAsync(accountId, command, cancellation);
            }
            catch (CommandConflictException conflict) when (conflict.Message == "version-conflict" && attempt < 2)
            {
                var latest = JsonSerializer.Deserialize<AccountReply>(await store.ExecuteAccountAsync(accountId, null, cancellation), AccountStore.Json)!;
                version = latest.Estate.Version;
            }
        }
    }
}
