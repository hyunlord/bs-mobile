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

public sealed record RecruitState(int Gems, int Pity, long Draws, string PoolFamily, string PoolVersion, SortedDictionary<string, int> Heroes);
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

    public JsonElement View(DomainContext context) => DomainJson.Element(ViewOf(DomainJson.Read<RecruitState>(context.State), null));

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
            return new(context.Account.State, context.State, DomainJson.Element(ViewOf(state, null)), false, reason, []);
        }
        var draw = RecruitDraw.Draw(pool, context.Seed ?? throw new InvalidOperationException("Recruit seed missing."), state.Draws, state.Pity);
        var heroes = new SortedDictionary<string, int>(state.Heroes, StringComparer.Ordinal);
        bool duplicate = heroes.TryGetValue(draw.Entry.HeroId, out int owned);
        int shards = duplicate ? pool.DuplicateShards[draw.Entry.Rarity] : 0;
        heroes[draw.Entry.HeroId] = checked(owned + shards);
        var next = state with { Gems = state.Gems - pool.CostGems, Pity = draw.PityAfter, Draws = state.Draws + 1, Heroes = heroes };
        var result = new RecruitResultView(draw.DrawIndex, draw.Entry.HeroId, draw.Entry.Rarity, duplicate ? "shards" : "unlock", shards, draw.Guaranteed, draw.PityBefore, draw.PityAfter);
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
public sealed record ResearchState(long NextSeq, ResearchJob[] Active, SortedDictionary<string, int> Levels, SortedDictionary<string, long> CompletedAtMs, string[] Proofs);
public sealed record ResearchPayload([property: JsonRequired] string NodeId);
public sealed record ResearchNodeView(string Id, string Category, int Level, int MaxLevel, string Status, string[] LockReasons,
    IReadOnlyDictionary<string, int> Cost, long Seconds, string Domain, int Value);
public sealed record ResearchJobView(string NodeId, int Level, long StartedAtMs, long CompletesAtMs, long RemainingSeconds);
public sealed record ResearchView(long ServerUtcMs, ResearchNodeView[] Nodes, ResearchJobView[] Active, ResearchEffect[] Effects);

/// <summary>
/// GDD17 level-1 research as a pure ADR0078 domain handler. Job times use the store's high-watermark UTC
/// (never the client); Advance settles due jobs once in completion order before any command executes.
/// </summary>
public sealed class ResearchDomain(ResearchContent content) : IDomainHandler
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

    public DomainResult Execute(DomainContext context, string operation, JsonElement payload)
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
        return new(utc, nodes, jobs, ResearchRules.Effects(content, state.Levels));
    }
}
