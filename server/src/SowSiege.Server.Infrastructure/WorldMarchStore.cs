using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed record WorldMarchCommand(Guid CommandId, long ExpectedVersion, string Operation,
    string? TargetId = null, long? Generation = null, string? HeroId = null, MarchTroops[]? Troops = null, Guid? MarchId = null);
public sealed record WorldMarch(Guid MarchId, Guid AccountId, Guid? TargetAccountId, string TargetId, long Generation,
    string Operation, string HeroId, MarchTroops[] Troops, WorldCoordinate[] Path, long StartedAtMs,
    long TravelMilliseconds, long DueAtMs, long Sequence, string State, string Reason, JsonElement Snapshot);
public sealed record MarchActivityResult(string State, long DueAtMs, string Reason);
public sealed record WorldMarchReply(bool Done, string Reason, WorldMarch? March);

// Server-only transactional adapters. Dispatch validates eligibility; activity callbacks never take client rewards.
public interface IWorldMarchActivity
{
    bool Supports(string operation);
    Task DispatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, WorldMarch march, long at, CancellationToken cancellation);
    Task<MarchActivityResult> ArriveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, WorldMarch march, long at, CancellationToken cancellation);
    Task<MarchActivityResult> AdvanceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, WorldMarch march, long at, bool cancel, CancellationToken cancellation);
    Task<DomainCredit[]> ReturnAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, WorldMarch march, long at, CancellationToken cancellation);
}

/// <summary>World row, then involved account UUIDs, then march rows. Each recovered event commits separately in persisted due order.</summary>
public sealed class WorldMarchStore(NpgsqlDataSource source, WorldStore world, WorldMapContent map, AccountStore accounts,
    WorldMarchContent content, TrainingHandler training, TrainingContent trainingContent, ResearchContent research,
    TimeProvider time, IEnumerable<IWorldMarchActivity> activities, WorldRecoveryGate? recoveryGate = null) : IWorldRecoveryBarrier
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS world_march_schema(version integer PRIMARY KEY);
        INSERT INTO world_march_schema VALUES(1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS world_marches(
          world_id text NOT NULL REFERENCES world_instances(world_id), march_id uuid NOT NULL,
          account_id uuid NOT NULL REFERENCES accounts(id), target_account_id uuid REFERENCES accounts(id),
          hero_id text NOT NULL, sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE,
          state text NOT NULL CHECK(state IN ('outbound','gathering','returning','returned')),
          due_at bigint NOT NULL, payload jsonb NOT NULL, PRIMARY KEY(world_id,march_id));
        CREATE UNIQUE INDEX IF NOT EXISTS world_march_hero ON world_marches(account_id,hero_id) WHERE state<>'returned';
        CREATE INDEX IF NOT EXISTS world_march_due ON world_marches(world_id,due_at,sequence,march_id) WHERE state<>'returned';
        CREATE TABLE IF NOT EXISTS world_march_events(
          world_id text NOT NULL, march_id uuid NOT NULL, ordinal integer NOT NULL,
          at_ms bigint NOT NULL, kind text NOT NULL, payload jsonb NOT NULL,
          PRIMARY KEY(world_id,march_id,ordinal), FOREIGN KEY(world_id,march_id) REFERENCES world_marches(world_id,march_id));
        """;

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var c = await source.OpenConnectionAsync(cancellation);
        await using var t = await c.BeginTransactionAsync(cancellation);
        await Run(c, t, "SELECT pg_advisory_xact_lock(18041)", cancellation);
        await Run(c, t, "CREATE TABLE IF NOT EXISTS world_march_schema(version integer PRIMARY KEY)", cancellation);
        await using (var v = new NpgsqlCommand("SELECT version FROM world_march_schema WHERE version<>1", c, t))
        {
            if (await v.ExecuteScalarAsync(cancellation) is not null) { throw new InvalidDataException("Unsupported march schema."); }
        }
        await Run(c, t, Sql, cancellation);
        await t.CommitAsync(cancellation);
        recoveryGate?.Attach(this);
    }

    public async Task<T> ExecuteAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, long, CancellationToken, Task<T>> action,
        CancellationToken cancellation)
    {
        while (true)
        {
            await RecoverAsync(cancellation);
            await using var c = await source.OpenConnectionAsync(cancellation);
            await using var t = await c.BeginTransactionAsync(cancellation);
            long now = await LockWorld(c, t, cancellation);
            if (await NextDue(c, t, now, cancellation) is not null) { continue; }
            T result = await action(c, t, now, cancellation);
            await t.CommitAsync(cancellation);
            return result;
        }
    }

    public async Task<WorldMarch[]> GetAsync(Guid account, CancellationToken cancellation)
    {
        await RecoverAsync(cancellation);
        await using var c = await source.OpenConnectionAsync(cancellation);
        await using var q = new NpgsqlCommand("SELECT payload::text FROM world_marches WHERE world_id=$1 AND account_id=$2 ORDER BY sequence", c);
        q.Parameters.AddWithValue(map.WorldId); q.Parameters.AddWithValue(account);
        var rows = new List<WorldMarch>();
        await using var r = await q.ExecuteReaderAsync(cancellation);
        while (await r.ReadAsync(cancellation)) { rows.Add(Decode(r.GetString(0))); }
        return rows.ToArray();
    }

    public async Task<string> ExecuteAsync(Guid account, WorldMarchCommand command, CancellationToken cancellation)
    {
        if (command.CommandId == Guid.Empty || command.ExpectedVersion < 0) { throw new CommandConflictException("invalid-command"); }
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("world-march-v1:" + JsonSerializer.Serialize(command, AccountStore.Json))));
        while (true)
        {
            await RecoverAsync(cancellation);
            await using var c = await source.OpenConnectionAsync(cancellation);
            await using var t = await c.BeginTransactionAsync(cancellation);
            long now = await LockWorld(c, t, cancellation);
            if (await NextDue(c, t, now, cancellation) is not null) { continue; }
            WorldMarch? previous = command.Operation == "cancel" && command.MarchId is { } id ? await Find(c, t, id, cancellation) : null;
            var target = command.TargetId is { } targetId ? await Target(c, t, targetId, cancellation) : null;
            await LockAccounts(c, t, account, previous?.TargetAccountId ?? target?.AccountId, cancellation);
            await using (var receipt = new NpgsqlCommand("SELECT payload_hash,response FROM commands WHERE account_id=$1 AND command_id=$2", c, t))
            {
                receipt.Parameters.AddWithValue(account); receipt.Parameters.AddWithValue(command.CommandId);
                await using var r = await receipt.ExecuteReaderAsync(cancellation);
                if (await r.ReadAsync(cancellation))
                {
                    if (r.GetString(0) != hash) { throw new CommandConflictException("command-id-reused"); }
                    return r.GetString(1);
                }
            }
            WorldMarch? result = null;
            await accounts.ExecuteWorldAsync(c, t, account, command.CommandId, now, async (context, ct) =>
            {
                if (context.Account.Version - 1 != command.ExpectedVersion) { throw new CommandConflictException("version-conflict"); }
                if (command.Operation == "cancel")
                {
                    if (previous is null || previous.AccountId != account) { throw new CommandConflictException("unknown-march"); }
                    result = await Cancel(c, t, previous, context.UtcMilliseconds, ct);
                    return new("world-march", [], []);
                }
                var activity = Activity(command.Operation);
                if (context.HasHeldMaterials) { throw new CommandConflictException("held-materials-pending"); }
                if (target is null || target.Generation != command.Generation ||
                    (command.Operation == "gather" ? target.Kind != "resource" : target.Kind != "city"))
                { throw new CommandConflictException("invalid-target-generation"); }
                if (target.AccountId == account) { throw new CommandConflictException("self-target"); }
                if (string.IsNullOrWhiteSpace(command.HeroId) || command.Troops is null) { throw new CommandConflictException("invalid-deployment"); }
                var recruited = context.States.TryGetValue("recruit", out var roster) ? roster.Deserialize<RecruitState>(AccountStore.Json) : null;
                if (command.HeroId != content.BasicHero && recruited?.Heroes.ContainsKey(command.HeroId) != true) { throw new CommandConflictException("hero-not-owned"); }
                var researchState = context.States.TryGetValue(ResearchDomain.Name, out var rs) ? rs.Deserialize<ResearchState>(AccountStore.Json) : null;
                if (command.HeroId == content.BasicHero && researchState?.Runs?.Any(r => r.Status == "open" && r.ExpiresAtMs >= context.UtcMilliseconds) == true)
                { throw new CommandConflictException("hero-busy"); }
                await using (var slots = new NpgsqlCommand("SELECT hero_id FROM world_marches WHERE account_id=$1 AND state<>'returned'", c, t))
                {
                    slots.Parameters.AddWithValue(account);
                    var heroes = new List<string>();
                    await using var r = await slots.ExecuteReaderAsync(ct);
                    while (await r.ReadAsync(ct)) { heroes.Add(r.GetString(0)); }
                    if (heroes.Contains(command.HeroId)) { throw new CommandConflictException("hero-busy"); }
                    if (heroes.Count >= content.Slots) { throw new CommandConflictException("march-slots-full"); }
                }
                int level = context.Account.State.Estate!.Buildings.Single(b => b.Kind == trainingContent.Rules.BarracksKind).Level;
                if (!content.Capacity.TryGetValue(level, out int capacity)) { throw new InvalidDataException("Unknown march capacity level."); }
                if (command.Operation == "scout" && command.Troops.Sum(x => (long)x.Count) != 1) { throw new CommandConflictException("scout-requires-one-soldier"); }
                var troops = training.Deploy(new(context.Account, context.States["training"], context.UtcMilliseconds, command.CommandId), command.Troops, capacity);
                if (!troops.Done) { throw new CommandConflictException(troops.Reason); }
                var city = await City(c, t, account, ct) ?? throw new CommandConflictException("city-required");
                var path = map.ShortestPath(new(city.X, city.Y), new(target.X, target.Y)) ?? throw new CommandConflictException("unreachable-target");
                int speed = researchState is null ? 0 : ResearchRules.Effects(research, researchState.Levels).SingleOrDefault(e => e.Domain == "march.speedPercent")?.Applied ?? 0;
                long travel = WorldMarchRules.TravelMilliseconds(path.Length - 1, content.EdgeMilliseconds, speed, content.MaximumSpeedPercent);
                var snapshot = JsonSerializer.SerializeToElement(new { policyHash = content.Hash, mapHash = map.Hash, basicHero = content.BasicHero,
                    trainingHash = training.ContentHash, researchHash = research.Hash, speedPercent = speed, domains = context.States }, AccountStore.Json);
                result = new(command.CommandId, account, target.AccountId, target.TargetId, target.Generation, command.Operation,
                    command.HeroId, command.Troops.OrderBy(x => x.UnitId, StringComparer.Ordinal).ToArray(), path, context.UtcMilliseconds,
                    travel, checked(context.UtcMilliseconds + travel), 0, "outbound", "dispatched", snapshot);
                await activity.DispatchAsync(c, t, result, context.UtcMilliseconds, ct);
                result = await Insert(c, t, result, ct);
                return new("world-march", [new("training", troops.State, troops.Events)], []);
            }, cancellation);
            string response = JsonSerializer.Serialize(new WorldMarchReply(true, result!.Reason, result), AccountStore.Json);
            await Run(c, t, "INSERT INTO commands(account_id,command_id,payload_hash,response) VALUES($1,$2,$3,$4)", cancellation,
                account, command.CommandId, hash, response);
            await t.CommitAsync(cancellation);
            return response;
        }
    }

    public async Task<int> RecoverAsync(CancellationToken cancellation)
    {
        int count = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            await using var c = await source.OpenConnectionAsync(cancellation);
            await using var t = await c.BeginTransactionAsync(cancellation);
            long cutoff = await LockWorld(c, t, cancellation);
            var march = await NextDue(c, t, cutoff, cancellation);
            if (march is null) { await t.CommitAsync(cancellation); return count; }
            await LockAccounts(c, t, march.AccountId, march.TargetAccountId, cancellation);
            long at = march.DueAtMs;
            if (march.TargetAccountId is { } defender)
            {
                await accounts.ExecuteWorldAsync(c, t, defender, march.MarchId, at, (_, _) => Task.FromResult(new WorldAccountChanges("world-march", [], [])), cancellation);
            }
            await accounts.ExecuteWorldAsync(c, t, march.AccountId, march.MarchId, at, async (context, ct) =>
            {
                if (march.State == "returning")
                {
                    var returned = training.ReturnUnharmed(new(context.Account, context.States["training"], context.UtcMilliseconds, march.MarchId), march.Troops);
                    if (!returned.Done) { throw new InvalidDataException("March deployment ledger mismatch: " + returned.Reason); }
                    var credits = await Activity(march.Operation).ReturnAsync(c, t, march, at, ct);
                    await Save(c, t, march with { State = "returned", Reason = "returned" }, at, ct);
                    return new("world-march", [new("training", returned.State, returned.Events)], credits);
                }
                MarchActivityResult next;
                if (march.State == "outbound")
                {
                    var target = await Target(c, t, march.TargetId, ct);
                    next = target is null || target.Generation != march.Generation
                        ? new("returning", checked(at + march.TravelMilliseconds), "target-gone")
                        : await Activity(march.Operation).ArriveAsync(c, t, march, at, ct);
                }
                else { next = await Activity(march.Operation).AdvanceAsync(c, t, march, at, false, ct); }
                ValidateActivity(next, at);
                await Save(c, t, march with { State = next.State, DueAtMs = next.DueAtMs, Reason = next.Reason }, at, ct);
                return new("world-march", [], []);
            }, cancellation);
            await t.CommitAsync(cancellation);
            count++;
        }
    }

    private async Task<WorldMarch> Cancel(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, long at, CancellationToken ct)
    {
        if (march.State is "returning" or "returned") { return march; }
        MarchActivityResult next = march.State == "outbound"
            ? new("returning", checked(at + WorldMarchRules.ReturnTravelMilliseconds(march.StartedAtMs, march.DueAtMs, at)), "recalled")
            : await Activity(march.Operation).AdvanceAsync(c, t, march, at, true, ct);
        ValidateActivity(next, at);
        var changed = march with { State = next.State, DueAtMs = next.DueAtMs, Reason = next.Reason };
        await Save(c, t, changed, at, ct);
        return changed;
    }

    private static void ValidateActivity(MarchActivityResult next, long at)
    {
        if (next.State is not ("gathering" or "returning") || next.DueAtMs < at || (next.State == "gathering" && next.DueAtMs == at))
        { throw new InvalidDataException("Activity returned a non-progressing march deadline."); }
    }
    private IWorldMarchActivity Activity(string operation) => activities.SingleOrDefault(a => a.Supports(operation))
        ?? throw new CommandConflictException("march-operation-unavailable");
    private async Task<long> LockWorld(NpgsqlConnection c, NpgsqlTransaction t, CancellationToken ct)
    {
        await world.LockAllocationAsync(c, t, time.GetUtcNow().ToUnixTimeMilliseconds(), ct);
        await using var q = new NpgsqlCommand("SELECT utc_highwater FROM world_instances WHERE world_id=$1", c, t);
        q.Parameters.AddWithValue(map.WorldId);
        return (long)(await q.ExecuteScalarAsync(ct))!;
    }
    private static async Task LockAccounts(NpgsqlConnection c, NpgsqlTransaction t, Guid actor, Guid? target, CancellationToken ct)
    {
        Guid[] ids = target.HasValue ? [actor, target.Value] : [actor];
        await using var q = new NpgsqlCommand("SELECT id FROM accounts WHERE id=ANY($1) ORDER BY id FOR UPDATE", c, t);
        q.Parameters.AddWithValue(ids.Distinct().ToArray());
        await using var r = await q.ExecuteReaderAsync(ct);
        int found = 0;
        while (await r.ReadAsync(ct)) { found++; }
        if (found != ids.Distinct().Count()) { throw new CommandConflictException("unknown-account"); }
    }
    private async Task<WorldMarch?> NextDue(NpgsqlConnection c, NpgsqlTransaction t, long now, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("SELECT payload::text FROM world_marches WHERE world_id=$1 AND state<>'returned' AND due_at<=$2 ORDER BY due_at,sequence,march_id LIMIT 1", c, t);
        q.Parameters.AddWithValue(map.WorldId); q.Parameters.AddWithValue(now);
        return await q.ExecuteScalarAsync(ct) is string json ? Decode(json) : null;
    }
    private async Task<WorldMarch?> Find(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("SELECT payload::text FROM world_marches WHERE world_id=$1 AND march_id=$2", c, t);
        q.Parameters.AddWithValue(map.WorldId); q.Parameters.AddWithValue(id);
        return await q.ExecuteScalarAsync(ct) is string json ? Decode(json) : null;
    }
    private async Task<WorldTargetView?> Target(NpgsqlConnection c, NpgsqlTransaction t, string id, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("SELECT target_id,generation,kind,content_id,x,y,account_id FROM world_targets t WHERE world_id=$1 AND target_id=$2 AND NOT EXISTS(SELECT 1 FROM auth_deleted_accounts d WHERE d.account_id=t.account_id)", c, t);
        q.Parameters.AddWithValue(map.WorldId); q.Parameters.AddWithValue(id);
        await using var r = await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5), r.IsDBNull(6) ? null : r.GetGuid(6), null) : null;
    }
    private Task<WorldTargetView?> City(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct) => Target(c, t, "city:" + id.ToString("D"), ct);
    private async Task<WorldMarch> Insert(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("INSERT INTO world_marches(world_id,march_id,account_id,target_account_id,hero_id,state,due_at,payload) VALUES($1,$2,$3,$4,$5,$6,$7,$8) RETURNING sequence", c, t);
        q.Parameters.AddWithValue(map.WorldId); q.Parameters.AddWithValue(march.MarchId); q.Parameters.AddWithValue(march.AccountId);
        q.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)march.TargetAccountId ?? DBNull.Value); q.Parameters.AddWithValue(march.HeroId);
        q.Parameters.AddWithValue(march.State); q.Parameters.AddWithValue(march.DueAtMs); q.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(march, AccountStore.Json));
        march = march with { Sequence = (long)(await q.ExecuteScalarAsync(ct))! };
        await Save(c, t, march, march.StartedAtMs, ct);
        return march;
    }
    private async Task Save(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch march, long at, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(march, AccountStore.Json);
        await using var q = new NpgsqlCommand("UPDATE world_marches SET state=$3,due_at=$4,payload=$5 WHERE world_id=$1 AND march_id=$2", c, t);
        q.Parameters.AddWithValue(map.WorldId); q.Parameters.AddWithValue(march.MarchId); q.Parameters.AddWithValue(march.State);
        q.Parameters.AddWithValue(march.DueAtMs); q.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json); await q.ExecuteNonQueryAsync(ct);
        await using var e = new NpgsqlCommand("INSERT INTO world_march_events SELECT $1,$2,coalesce(max(ordinal),-1)+1,$3,$4,$5 FROM world_march_events WHERE world_id=$1 AND march_id=$2", c, t);
        e.Parameters.AddWithValue(map.WorldId); e.Parameters.AddWithValue(march.MarchId); e.Parameters.AddWithValue(at);
        e.Parameters.AddWithValue(march.Reason); e.Parameters.AddWithValue(NpgsqlDbType.Jsonb, json); await e.ExecuteNonQueryAsync(ct);
    }
    private static WorldMarch Decode(string json) => JsonSerializer.Deserialize<WorldMarch>(json, AccountStore.Json) ?? throw new InvalidDataException("Invalid march state.");
    private static async Task Run(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params object[] values)
    {
        await using var q = new NpgsqlCommand(sql, c, t);
        foreach (var value in values) { q.Parameters.AddWithValue(value); }
        await q.ExecuteNonQueryAsync(ct);
    }
}
