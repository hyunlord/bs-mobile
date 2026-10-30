using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;

namespace SowSiege.Server.Application;

internal static class DomainJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
    public static T Read<T>(JsonElement element) => element.Deserialize<T>(Options) ?? throw new JsonException("Empty domain JSON.");
}

public sealed record RecruitState(int Gems, int Pity, long Draws, string PoolFamily, string PoolVersion, SortedDictionary<string, int> Heroes, RecruitResultView? Last = null);
public sealed record RecruitPayload([property: JsonRequired] string PoolVersion);
public sealed record RecruitResultView(long DrawIndex, string HeroId, string Rarity, string Outcome, int Shards, bool Guaranteed, int PityBefore, int PityAfter);
public sealed record RecruitView(string PoolFamilyId, string PoolVersion, string PolicyHash, int CostGems, int GuaranteeAfterMisses, string GuaranteedRarity,
    IReadOnlyDictionary<string, int> DuplicateShards, RecruitOddsEntry[] Base, RecruitOddsEntry[] Next, int Pity, int DrawsUntilGuarantee, int Gems,
    IReadOnlyDictionary<string, int> Heroes, RecruitResultView? Last);

/// <summary>
/// GDD16 hero recruit as a pure ADR0078 domain handler. The draw seed is HMAC(server key, account, draw index, pool version),
/// computed by the store from <see cref="SeedMaterial"/>; this handler never reads keys, clocks or RNG.
/// Gems live in this domain's state until a second gem-spending command needs a shared account currency.
/// </summary>
public sealed class RecruitDomain(RecruitPool pool, int prototypeGems) : IDomainHandler
{
    public const string Name = "recruit";
    public const string GemResource = "slg:gem";
    public string Domain => Name;
    public int StateVersion => 1;
    public string ContentHash => pool.PolicyHash;
    public JsonElement InitialState => DomainJson.Element(new RecruitState(prototypeGems, 0, 0, pool.PoolFamilyId, pool.PoolVersion, new(StringComparer.Ordinal)));

    /// <summary>Development-only gem grant. Any non-Development or non-prototype host gets 0.</summary>
    public static int PrototypeGems(string environment, string? prototypeEnabled, string? configured) =>
        environment == "Development" && prototypeEnabled == "true" && int.TryParse(configured, out int gems) && gems > 0 ? gems : 0;

    public JsonElement View(DomainContext context) { var state = DomainJson.Read<RecruitState>(context.State); return DomainJson.Element(ViewOf(state, state.Last)); }

    public DomainResult Advance(DomainContext context) => new(context.Account.State, context.State, View(context), true, "", []);

    public string? SeedMaterial(DomainContext context, string operation, JsonElement payload) =>
        operation == "recruit" ? RecruitDraw.Material(DomainJson.Read<RecruitState>(context.State).Draws, pool.PoolVersion) : null;

    public DomainResource[] Resources(JsonElement state) => [new(GemResource, DomainJson.Read<RecruitState>(state).Gems)];

    public DomainResult Execute(DomainContext context, string operation, JsonElement payload)
    {
        if (operation != "recruit")
        {
            throw new CommandConflictException("unsupported-operation");
        }
        var request = DomainJson.Read<RecruitPayload>(payload);
        var state = DomainJson.Read<RecruitState>(context.State);
        string? reason = request.PoolVersion != pool.PoolVersion ? "pool-changed"
            : state.PoolFamily == pool.PoolFamilyId && state.PoolVersion != pool.PoolVersion ? "pool-version-unsupported"
            : state.Gems < pool.CostGems ? "insufficient-gems" : null;
        if (reason is not null)
        {
            // Rejected before commit: no gem, counter or roster change.
            return new(context.Account.State, context.State, DomainJson.Element(ViewOf(state, state.Last)), false, reason, []);
        }
        var draw = RecruitDraw.Draw(pool, context.Seed ?? throw new InvalidOperationException("Recruit seed missing."), state.Draws, state.Pity);
        var heroes = new SortedDictionary<string, int>(state.Heroes, StringComparer.Ordinal);
        bool duplicate = heroes.TryGetValue(draw.Entry.HeroId, out int owned);
        int shards = duplicate ? pool.DuplicateShards[draw.Entry.Rarity] : 0;
        heroes[draw.Entry.HeroId] = checked(owned + shards);
        var next = state with { Gems = state.Gems - pool.CostGems, Pity = draw.PityAfter, Draws = state.Draws + 1, Heroes = heroes };
        next = next with { Last = new RecruitResultView(draw.DrawIndex, draw.Entry.HeroId, draw.Entry.Rarity, duplicate ? "shards" : "unlock", shards, draw.Guaranteed, draw.PityBefore, draw.PityAfter) };
        var result = next.Last;
        var ledger = DomainJson.Element(new
        {
            result.DrawIndex, result.HeroId, result.Rarity, result.Outcome, result.Shards, result.Guaranteed, result.PityBefore, result.PityAfter,
            draw.Roll, Seed = draw.SeedHex, pool.PoolFamilyId, pool.PoolVersion, pool.PolicyHash, GemsBefore = state.Gems, GemsAfter = next.Gems
        });
        return new(context.Account.State, DomainJson.Element(next), DomainJson.Element(ViewOf(next, result)), true, "recruited", [new("recruited", ledger)]);
    }

    private RecruitView ViewOf(RecruitState state, RecruitResultView? last) => new(pool.PoolFamilyId, pool.PoolVersion, pool.PolicyHash, pool.CostGems,
        pool.GuaranteeAfterMisses, pool.GuaranteedRarity, pool.DuplicateShards, pool.Odds(0), pool.Odds(state.Pity), state.Pity,
        Math.Max(0, pool.GuaranteeAfterMisses - state.Pity) + 1, state.Gems, state.Heroes, last);
}

public sealed record ResearchJob(long Seq, string NodeId, int Level, long StartedAtMs, long CompletesAtMs);
public sealed record ResearchState(long NextSeq, ResearchJob[] Active, SortedDictionary<string, int> Levels, SortedDictionary<string, long> CompletedAtMs, string[] Proofs,
    ExpeditionRun[]? Runs = null, long NextRun = 1);
/// <summary>A server-issued personal fallback run (ADR0077 replay settlement). RunId is the issuing commandId.</summary>
public sealed record ExpeditionRun(Guid RunId, long Ordinal, int Seed, string ProofId, string Resource, long IssuedAtMs, long ExpiresAtMs,
    string Status, int Attempts, string? Outcome = null, int Granted = 0, string? StateHash = null, long LastSubmitAtMs = 0, int Timeouts = 0, int Promised = 0);
public sealed record ExpeditionStart([property: JsonRequired] string ProofId, [property: JsonRequired] string Resource);
/// <summary>Server-only verdict produced by the replay verifier outside the handler. Never accepted from clients.</summary>
public sealed record ExpeditionVerdict([property: JsonRequired] Guid RunId, [property: JsonRequired] string Verdict, string? EndKind, int Tick, string? StateHash, string ReplayHash);
/// <summary>Run budget: TTL and failed-verification attempts are server configuration; reward caps come from content data.</summary>
public sealed record ExpeditionRules(long TtlMilliseconds, int MaxAttempts, IReadOnlyDictionary<string, int> WalletCaps, int MaxTimeouts = 3)
{
    public const string SettleOperation = "settle-expedition";
    public static int SeedOf(byte[] seed) => (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(seed) & 0x7fffffff);
}
public sealed record ResearchPayload([property: JsonRequired] string NodeId);
public sealed record ResearchNodeView(string Id, string Category, int Level, int MaxLevel, string Status, string[] LockReasons,
    IReadOnlyDictionary<string, int> Cost, long Seconds, string Domain, int Value);
public sealed record ResearchJobView(string NodeId, int Level, long StartedAtMs, long CompletesAtMs, long RemainingSeconds);
public sealed record ResearchView(long ServerUtcMs, ResearchNodeView[] Nodes, ResearchJobView[] Active, ResearchEffect[] Effects, string[] Proofs, ExpeditionRun[] Runs);

/// <summary>
/// GDD17 level-1 research as a pure ADR0078 domain handler. Job times use the store's high-watermark UTC
/// (never the client); Advance settles due jobs once in completion order before any command executes.
/// </summary>
public sealed class ResearchDomain(ResearchContent content, ExpeditionRules? runs = null) : IDomainHandler
{
    public const string Name = "research";
    public string Domain => Name;
    public int StateVersion => 1;
    public string ContentHash => content.Hash;
    public JsonElement InitialState => DomainJson.Element(new ResearchState(1, [], new(StringComparer.Ordinal), new(StringComparer.Ordinal), []));

    public JsonElement View(DomainContext context) => DomainJson.Element(ViewOf(context.Account.State, DomainJson.Read<ResearchState>(context.State), context.UtcMilliseconds));

    public DomainResult Advance(DomainContext context)
    {
        var state = DomainJson.Read<ResearchState>(context.State);
        var due = state.Active.Where(j => j.CompletesAtMs <= context.UtcMilliseconds).OrderBy(j => j.CompletesAtMs).ThenBy(j => j.Seq).ToArray();
        if (due.Length == 0)
        {
            return new(context.Account.State, context.State, View(context), true, "", []);
        }
        var levels = new SortedDictionary<string, int>(state.Levels, StringComparer.Ordinal);
        var completed = new SortedDictionary<string, long>(state.CompletedAtMs, StringComparer.Ordinal);
        foreach (var job in due)
        {
            levels[job.NodeId] = job.Level;
            completed[job.NodeId] = job.CompletesAtMs;
        }
        var next = state with { Active = state.Active.Except(due).ToArray(), Levels = levels, CompletedAtMs = completed };
        var events = due.Select(j => new DomainEvent("research-completed", DomainJson.Element(new { j.Seq, j.NodeId, j.Level, j.CompletesAtMs }))).ToArray();
        return new(context.Account.State, DomainJson.Element(next), DomainJson.Element(ViewOf(context.Account.State, next, context.UtcMilliseconds)), true, "", events);
    }

    public string? SeedMaterial(DomainContext context, string operation, JsonElement payload) =>
        operation == "start-expedition" ? "sow-run-v1\n" + DomainJson.Read<ResearchState>(context.State).NextRun.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

    public DomainResult Execute(DomainContext context, string operation, JsonElement payload) => operation switch
    {
        "start-expedition" => StartExpedition(context, DomainJson.Read<ExpeditionStart>(payload)),
        ExpeditionRules.SettleOperation => SettleExpedition(context, DomainJson.Read<ExpeditionVerdict>(payload)),
        _ => ExecuteResearch(context, operation, payload)
    };

    private DomainResult Reply(DomainContext context, bool done, string reason, MetaState account, ResearchState next, DomainEvent[] events) =>
        new(account, DomainJson.Element(next), DomainJson.Element(ViewOf(account, next, context.UtcMilliseconds)), done, reason, events);

    private DomainResult StartExpedition(DomainContext context, ExpeditionStart request)
    {
        var rules = runs ?? throw new CommandConflictException("unsupported-operation");
        var state = DomainJson.Read<ResearchState>(context.State);
        var list = state.Runs ?? [];
        string? reason = !content.Fallback.ProofIds.Contains(request.ProofId) ? "unknown-proof"
            : !rules.WalletCaps.ContainsKey(request.Resource) || request.Resource == "meta:charter" ? "unknown-resource"
            : list.Any(r => r.Status == "open" && r.ExpiresAtMs > context.UtcMilliseconds) ? "run-open" : null;
        if (reason is not null)
        {
            return Reply(context, false, reason, context.Account.State, state, []);
        }
        // The fallback run needs no troops, research, collected hero or entry cost (GDD17 §3).
        // Promise the material now (GDD17 §4.2): run rewards still waiting in the shared held queue count against pendingMaterialCap.
        int heldNow = context.Held?.Values.Sum() ?? 0;
        int promised = Math.Max(0, Math.Min(content.Fallback.MaterialRewardCap, content.Fallback.PendingMaterialCap - heldNow));
        var run = new ExpeditionRun(context.CommandId ?? throw new InvalidOperationException("Run id missing."), state.NextRun,
            ExpeditionRules.SeedOf(context.Seed ?? throw new InvalidOperationException("Run seed missing.")), request.ProofId, request.Resource,
            context.UtcMilliseconds, checked(context.UtcMilliseconds + rules.TtlMilliseconds), "open", 0, Promised: promised);
        var expired = list.Select(r => r.Status == "open" && r.ExpiresAtMs <= context.UtcMilliseconds ? r with { Status = "expired" } : r);
        var next = state with { Runs = [.. expired, run], NextRun = state.NextRun + 1 };
        return Reply(context, true, promised > 0 ? "issued" : "issued-proof-only", context.Account.State, next, [new("expedition-issued", DomainJson.Element(run))]);
    }

    private DomainResult SettleExpedition(DomainContext context, ExpeditionVerdict verdict)
    {
        var rules = runs ?? throw new CommandConflictException("unsupported-operation");
        var state = DomainJson.Read<ResearchState>(context.State);
        var list = (state.Runs ?? []).ToList();
        int index = list.FindIndex(r => r.RunId == verdict.RunId);
        if (index < 0)
        {
            return Reply(context, false, "unknown-run", context.Account.State, state, []);
        }
        var run = list[index];
        if (run.Status != "open")
        {
            return Reply(context, false, "already-" + run.Status, context.Account.State, state, []);
        }
        if (context.UtcMilliseconds > run.ExpiresAtMs)
        {
            list[index] = run with { Status = "expired" };
            return Reply(context, false, "run-expired", context.Account.State, state with { Runs = list.ToArray() }, [new("expedition-expired", DomainJson.Element(new { run.RunId }))]);
        }
        if (verdict.Verdict == "timeout")
        {
            // Our budget, not the player's replay: no attempt is used, but timeouts per run are capped so retries cannot hold the slot forever.
            int timeouts = run.Timeouts + 1;
            bool exhausted = timeouts >= rules.MaxTimeouts;
            list[index] = run with { Timeouts = timeouts, LastSubmitAtMs = context.UtcMilliseconds, Status = exhausted ? "failed" : "open", Outcome = exhausted ? "verify-budget-exhausted" : null };
            string why = exhausted ? "verify-budget-exhausted" : "verify-timeout";
            return Reply(context, false, why, context.Account.State, state with { Runs = list.ToArray() }, [new("expedition-" + why, DomainJson.Element(new { run.RunId, timeouts }))]);
        }
        if (verdict.Verdict != "verified")
        {
            int attempts = run.Attempts + 1;
            list[index] = run with { Attempts = attempts, LastSubmitAtMs = context.UtcMilliseconds, Status = attempts >= rules.MaxAttempts ? "failed" : "open" };
            var failed = DomainJson.Element(new { run.RunId, verdict.Verdict, attempts, verdict.ReplayHash });
            return Reply(context, false, "replay-" + verdict.Verdict, context.Account.State, state with { Runs = list.ToArray() }, [new("expedition-rejected", failed)]);
        }
        // Verified: the objective of the prototype fallback run is surviving to the profile duration.
        bool succeeded = verdict.EndKind == "Duration";
        string[] proofs = succeeded && !state.Proofs.Contains(run.ProofId) ? [.. state.Proofs, run.ProofId] : state.Proofs;
        // The promised amount goes to the account as issued; the shared writer holds whatever the wallet cannot take.
        int granted = succeeded ? run.Promised : 0;
        list[index] = run with { Status = "settled", Outcome = succeeded ? "objective-complete" : "objective-failed", Granted = granted, StateHash = verdict.StateHash, LastSubmitAtMs = context.UtcMilliseconds };
        var settled = DomainJson.Element(new { run.RunId, run.Seed, run.ProofId, verdict.EndKind, verdict.Tick, verdict.StateHash, verdict.ReplayHash, granted, run.Resource, ProofGranted = succeeded });
        var next = state with { Runs = list.ToArray(), Proofs = proofs };
        return new(context.Account.State, DomainJson.Element(next), DomainJson.Element(ViewOf(context.Account.State, next, context.UtcMilliseconds)),
            true, succeeded ? "objective-complete" : "objective-failed", [new("expedition-settled", settled)], granted > 0 ? [new(run.Resource, granted)] : null);
    }

    private DomainResult ExecuteResearch(DomainContext context, string operation, JsonElement payload)
    {
        var request = DomainJson.Read<ResearchPayload>(payload);
        var state = DomainJson.Read<ResearchState>(context.State);
        var account = context.Account.State;
        var node = content.Nodes.SingleOrDefault(n => n.Id == request.NodeId);
        DomainResult Reply(bool done, string reason, MetaState accountState, ResearchState next, DomainEvent[] events) =>
            new(accountState, DomainJson.Element(next), DomainJson.Element(ViewOf(accountState, next, context.UtcMilliseconds)), done, reason, events);
        if (node is null)
        {
            return Reply(false, "unknown-node", account, state, []);
        }
        bool finished = state.Levels.GetValueOrDefault(node.Id) >= node.Level;
        bool active = state.Active.Any(j => j.NodeId == node.Id);
        if (operation == "complete-research")
        {
            // Settlement already happened in Advance; this only reports it and never grants again.
            return Reply(finished, finished ? "completed" : active ? "not-finished" : "not-started", account, state, []);
        }
        if (operation != "start-research")
        {
            throw new CommandConflictException("unsupported-operation");
        }
        string[] locks = ResearchRules.LockReasons(node, account, state.Levels, state.Proofs.ToHashSet(StringComparer.Ordinal));
        string? reason = finished ? "already-completed" : active ? "already-active" : state.Active.Length >= content.ActiveSlots ? "queue-full"
            : locks.Length != 0 ? "locked:" + string.Join(',', locks) : !ResearchRules.Affordable(node, account) ? "insufficient-resources" : null;
        if (reason is not null)
        {
            return Reply(false, reason, account, state, []);
        }
        var job = new ResearchJob(state.NextSeq, node.Id, node.Level, context.UtcMilliseconds, checked(context.UtcMilliseconds + node.Seconds * 1000));
        var next = state with { NextSeq = state.NextSeq + 1, Active = [.. state.Active, job] };
        var started = DomainJson.Element(new { job.Seq, job.NodeId, job.Level, job.StartedAtMs, job.CompletesAtMs, node.Cost, ContentHash = content.Hash });
        return Reply(true, "started", ResearchRules.Charge(node, account), next, [new("research-started", started)]);
    }

    private ResearchView ViewOf(MetaState account, ResearchState state, long utc)
    {
        var proofs = state.Proofs.ToHashSet(StringComparer.Ordinal);
        var nodes = content.Nodes.Select(n =>
        {
            var locks = ResearchRules.LockReasons(n, account, state.Levels, proofs);
            string status = state.Levels.GetValueOrDefault(n.Id) >= n.Level ? "completed" : state.Active.Any(j => j.NodeId == n.Id) ? "active" : locks.Length == 0 ? "available" : "locked";
            return new ResearchNodeView(n.Id, n.Category, state.Levels.GetValueOrDefault(n.Id), n.Level, status, locks, n.Cost, n.Seconds, n.Domain, n.Value);
        }).ToArray();
        var jobs = state.Active.Select(j => new ResearchJobView(j.NodeId, j.Level, j.StartedAtMs, j.CompletesAtMs, Math.Max(0, (j.CompletesAtMs - utc + 999) / 1000))).ToArray();
        return new(utc, nodes, jobs, ResearchRules.Effects(content, state.Levels), state.Proofs, state.Runs ?? []);
    }
}
