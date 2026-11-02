using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

/// <summary>Trusted arrival snapshot only. No HTTP endpoint accepts these values from a client.</summary>
public sealed record GatheringArrival(Guid MarchId, Guid AccountId, string TargetId, long Generation,
    int Soldiers, long ReturnTravelMilliseconds, bool MarchManaged = false);
public sealed record GatheringVisit(Guid MarchId, long ArrivalOrder, string Status, string Reason,
    int Cargo, long? CompletionAtMs, long? ReturnAtMs, long? DueAtMs);

/// <summary>Lock order: world row, accounts in UUID order, resource/arrival rows. All methods using a supplied
/// transaction require the caller to own that world's lock. No method acquires an alliance lock.</summary>
public sealed class GatheringStore(NpgsqlDataSource source, WorldStore world, WorldMapContent map,
    GatheringContent content, AccountStore accounts, TimeProvider time)
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS gathering_schema(version integer PRIMARY KEY, content_hash text NOT NULL);
        CREATE SEQUENCE IF NOT EXISTS world_gathering_sequence;
        CREATE TABLE IF NOT EXISTS world_gathering_nodes(
          world_id text NOT NULL, target_id text NOT NULL, material text NOT NULL, payload jsonb NOT NULL,
          due_at bigint, sequence bigint NOT NULL DEFAULT nextval('world_gathering_sequence'),
          PRIMARY KEY(world_id,target_id), FOREIGN KEY(world_id,target_id) REFERENCES world_targets(world_id,target_id));
        CREATE TABLE IF NOT EXISTS world_gathering_arrivals(
          march_id uuid PRIMARY KEY, world_id text NOT NULL REFERENCES world_instances(world_id), target_id text NOT NULL,
          account_id uuid REFERENCES accounts(id) ON DELETE SET NULL, request_hash text NOT NULL,
          arrival_order bigint NOT NULL UNIQUE DEFAULT nextval('world_gathering_sequence'),
          generation bigint NOT NULL, soldiers integer NOT NULL CHECK(soldiers>0), travel_ms bigint NOT NULL CHECK(travel_ms>=0),
          managed boolean NOT NULL, material text NOT NULL, status text NOT NULL,
          reason text NOT NULL, cargo integer NOT NULL DEFAULT 0 CHECK(cargo>=0), completion_at bigint, return_at bigint,
          CHECK(status IN ('collecting','returning','settled','forfeited')));
        CREATE INDEX IF NOT EXISTS gathering_return_due ON world_gathering_arrivals(world_id,return_at,arrival_order) WHERE status='returning';
        CREATE TABLE IF NOT EXISTS world_gathering_generations(
          world_id text NOT NULL, target_id text NOT NULL, generation bigint NOT NULL,
          created_stock integer NOT NULL, confirmed_gathered integer NOT NULL,
          PRIMARY KEY(world_id,target_id,generation), CHECK(created_stock=confirmed_gathered));
        """;
    private sealed record StoredArrival(GatheringVisit Visit, Guid? Account, string Target, string Material, bool Managed, long Travel, string Hash);
    private sealed record Due(string Kind, string Entity, long At, long Sequence);

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var tx = await connection.BeginTransactionAsync(cancellation);
        await Run(connection, tx, "SELECT pg_advisory_xact_lock(18019)", cancellation);
        await Run(connection, tx, Sql, cancellation);
        await using (var versions = new NpgsqlCommand("SELECT version,content_hash FROM gathering_schema", connection, tx))
        await using (var rows = await versions.ExecuteReaderAsync(cancellation))
        {
            while (await rows.ReadAsync(cancellation))
            {
                if (rows.GetInt32(0) != 1 || rows.GetString(1) != content.Hash) { throw new InvalidDataException("Gathering schema/content migration required."); }
            }
        }
        await world.LockAllocationAsync(connection, tx, 0, cancellation);
        await Run(connection, tx, "INSERT INTO gathering_schema VALUES(1,$1) ON CONFLICT DO NOTHING", cancellation, content.Hash);
        foreach (var target in map.InitialTargets.Where(t => t.Kind == "resource"))
        {
            if (!content.Materials.TryGetValue(target.ContentId, out string? material)) { throw new InvalidDataException("Unmapped gathering target material."); }
            var node = WorldGathering.Create(target.TargetId, target.Generation, content.Policy, 0);
            await Run(connection, tx, "INSERT INTO world_gathering_nodes(world_id,target_id,material,payload) VALUES($1,$2,$3,$4::jsonb) ON CONFLICT DO NOTHING",
                cancellation, map.WorldId, target.TargetId, material, JsonSerializer.Serialize(node, AccountStore.Json));
        }
        await tx.CommitAsync(cancellation);
    }

    /// <summary>Server integration/testing surface. Arrival parameters must come from a persisted validated march.</summary>
    public async Task<GatheringVisit> ArriveAsync(GatheringArrival arrival, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var tx = await connection.BeginTransactionAsync(cancellation);
        long now = await LockAsync(connection, tx, cancellation);
        await RequireStandalone(connection, tx, cancellation);
        await RecoverActivitiesAsync(connection, tx, now, cancellation);
        var result = await ArriveAsync(connection, tx, arrival, now, cancellation);
        await tx.CommitAsync(cancellation);
        return result;
    }

    internal async Task<GatheringVisit> ArriveAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        GatheringArrival arrival, long at, CancellationToken cancellation)
    {
        if (arrival.MarchId == Guid.Empty || arrival.AccountId == Guid.Empty || arrival.Soldiers <= 0 || arrival.Generation <= 0 || arrival.ReturnTravelMilliseconds < 0)
        { throw new CommandConflictException("invalid-gathering-arrival"); }
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(arrival, AccountStore.Json)));
        var old = await ReadArrival(connection, tx, arrival.MarchId, cancellation);
        if (old is not null)
        {
            if (old.Hash != hash) { throw new CommandConflictException("march-id-reused"); }
            return old.Visit;
        }
        await LockAccount(connection, tx, arrival.AccountId, cancellation);
        var node = await ReadNode(connection, tx, arrival.TargetId, cancellation);
        while ((node.Lease?.DueAtMs ?? node.RespawnAtMs) is { } due && due <= at)
        {
            await ProcessNode(connection, tx, arrival.TargetId, due, cancellation);
            node = await ReadNode(connection, tx, arrival.TargetId, cancellation);
        }
        string material = await Scalar<string>(connection, tx, "SELECT material FROM world_gathering_nodes WHERE world_id=$1 AND target_id=$2", cancellation, map.WorldId, arrival.TargetId);
        long order = await Scalar<long>(connection, tx, "SELECT nextval('world_gathering_sequence')", cancellation);
        var transition = WorldGathering.Claim(node, arrival.Generation, arrival.MarchId, order, arrival.Soldiers, at);
        await Run(connection, tx, """
            INSERT INTO world_gathering_arrivals(march_id,world_id,target_id,account_id,request_hash,arrival_order,generation,soldiers,travel_ms,managed,material,status,reason,completion_at,return_at)
            VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)
            """, cancellation, arrival.MarchId, map.WorldId, arrival.TargetId, arrival.AccountId, hash, order,
            arrival.Generation, arrival.Soldiers, arrival.ReturnTravelMilliseconds, arrival.MarchManaged, material,
            transition.Done ? "collecting" : "returning", transition.Reason,
            transition.Done ? DBNull.Value : at, transition.Done ? DBNull.Value : checked(at + arrival.ReturnTravelMilliseconds));
        await Persist(connection, tx, transition, cancellation);
        return (await ReadArrival(connection, tx, arrival.MarchId, cancellation))!.Visit;
    }

    public async Task<GatheringVisit> CancelAsync(Guid marchId, Guid accountId, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var tx = await connection.BeginTransactionAsync(cancellation);
        long now = await LockAsync(connection, tx, cancellation);
        var old = await ReadArrival(connection, tx, marchId, cancellation) ?? throw new CommandConflictException("unknown-gathering-march");
        if (old.Account != accountId) { throw new CommandConflictException("gathering-not-owned"); }
        await LockAccount(connection, tx, accountId, cancellation);
        var result = await ProcessActivityAsync(connection, tx, marchId, now, true, cancellation);
        await tx.CommitAsync(cancellation);
        return result;
    }

    internal async Task<GatheringVisit> ProcessActivityAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid marchId, long at, bool cancel, CancellationToken cancellation)
    {
        var old = await ReadArrival(connection, tx, marchId, cancellation) ?? throw new CommandConflictException("unknown-gathering-march");
        if (old.Visit.Status != "collecting") { return old.Visit; }
        var node = await ReadNode(connection, tx, old.Target, cancellation);
        var change = cancel ? WorldGathering.Cancel(node, node.Generation, marchId, old.Visit.ArrivalOrder, at) : WorldGathering.Advance(node, at);
        await Persist(connection, tx, change, cancellation);
        return (await ReadArrival(connection, tx, marchId, cancellation))!.Visit;
    }

    /// <summary>Called inside the same account writer transaction that restores troops/releases march locks.</summary>
    internal async Task<DomainCredit[]> ReturnCreditsAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid marchId, Guid accountId, long at, CancellationToken cancellation)
    {
        var arrival = await ReadArrival(connection, tx, marchId, cancellation) ?? throw new CommandConflictException("unknown-gathering-march");
        if (arrival.Account != accountId) { throw new CommandConflictException("gathering-not-owned"); }
        if (arrival.Visit.Status == "settled") { return []; }
        if (arrival.Visit.Status != "returning" || arrival.Visit.ReturnAtMs > at) { throw new CommandConflictException("gathering-return-not-due"); }
        await Run(connection, tx, "UPDATE world_gathering_arrivals SET status='settled' WHERE march_id=$1", cancellation, marchId);
        return arrival.Visit.Cargo == 0 ? [] : [new(arrival.Material, arrival.Visit.Cargo)];
    }

    public async Task RecoverAsync(CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var tx = await connection.BeginTransactionAsync(cancellation);
        long now = await LockAsync(connection, tx, cancellation);
        await RequireStandalone(connection, tx, cancellation);
        // Lock all accounts this recovery can touch in a fixed order, before touching per-entity rows.
        await using (var owners = new NpgsqlCommand("SELECT id FROM accounts WHERE id IN (SELECT account_id FROM world_gathering_arrivals WHERE world_id=$1 AND NOT managed AND status IN ('collecting','returning')) ORDER BY id FOR UPDATE", connection, tx))
        {
            owners.Parameters.AddWithValue(map.WorldId);
            await owners.ExecuteNonQueryAsync(cancellation);
        }
        while (await NextDue(connection, tx, now, true, cancellation) is { } due)
        {
            if (due.Kind == "node") { await ProcessNode(connection, tx, due.Entity, due.At, cancellation); continue; }
            Guid march = Guid.Parse(due.Entity);
            var arrival = (await ReadArrival(connection, tx, march, cancellation))!;
            if (arrival.Account is null)
            {
                await Run(connection, tx, "UPDATE world_gathering_arrivals SET status='forfeited' WHERE march_id=$1", cancellation, march);
                continue;
            }
            await accounts.ExecuteWorldAsync(connection, tx, arrival.Account.Value, march, due.At,
                async (_, ct) => new("world-gathering", [], await ReturnCreditsAsync(connection, tx, march, arrival.Account.Value, due.At, ct)), cancellation);
        }
        await tx.CommitAsync(cancellation);
    }

    private async Task RequireStandalone(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken cancellation)
    {
        long count = await Scalar<long>(connection, tx, "SELECT count(*) FROM world_gathering_arrivals WHERE world_id=$1 AND managed AND status IN ('collecting','returning')", cancellation, map.WorldId);
        if (count != 0) { throw new CommandConflictException("march-scheduler-required"); }
    }
    private async Task RecoverActivitiesAsync(NpgsqlConnection connection, NpgsqlTransaction tx, long now, CancellationToken cancellation)
    {
        while (await NextDue(connection, tx, now, false, cancellation) is { } due)
        { await ProcessNode(connection, tx, due.Entity, due.At, cancellation); }
    }
    private async Task ProcessNode(NpgsqlConnection connection, NpgsqlTransaction tx, string target, long at, CancellationToken cancellation)
    {
        var node = await ReadNode(connection, tx, target, cancellation);
        await Persist(connection, tx, node.Lease is null ? WorldGathering.Respawn(node, node.Generation, at) : WorldGathering.Advance(node, at), cancellation);
    }
    private async Task<Due?> NextDue(NpgsqlConnection connection, NpgsqlTransaction tx, long now, bool returns, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("""
            SELECT kind,entity,due_at,sequence FROM (
              SELECT 'node' AS kind,target_id AS entity,due_at,sequence FROM world_gathering_nodes WHERE world_id=$1
              UNION ALL
              SELECT 'return',march_id::text,return_at,arrival_order FROM world_gathering_arrivals a
              WHERE world_id=$1 AND $3 AND NOT managed AND status='returning'
                AND NOT EXISTS(SELECT 1 FROM auth_deleted_accounts d WHERE d.account_id=a.account_id)
            ) jobs WHERE due_at<=$2 ORDER BY due_at,sequence,entity COLLATE "C" LIMIT 1
            """, connection, tx);
        query.Parameters.AddWithValue(map.WorldId); query.Parameters.AddWithValue(now); query.Parameters.AddWithValue(returns);
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)) : null;
    }
    private async Task Persist(NpgsqlConnection connection, NpgsqlTransaction tx, GatheringTransition change, CancellationToken cancellation)
    {
        WorldGathering.Validate(change.State);
        var node = change.State;
        await Run(connection, tx, "UPDATE world_gathering_nodes SET payload=$3::jsonb,due_at=$4,sequence=CASE WHEN due_at IS DISTINCT FROM $4 THEN nextval('world_gathering_sequence') ELSE sequence END WHERE world_id=$1 AND target_id=$2", cancellation,
            map.WorldId, node.TargetId, JsonSerializer.Serialize(node, AccountStore.Json), (object?)(node.Lease?.DueAtMs ?? node.RespawnAtMs) ?? DBNull.Value);
        if (change.Completion is { } done)
        {
            await Run(connection, tx, "UPDATE world_gathering_arrivals SET status='returning',reason=$2,cargo=$3,completion_at=$4,return_at=$4+travel_ms WHERE march_id=$1 AND status='collecting'",
                cancellation, done.MarchId, done.Reason, done.Cargo, done.AtMs);
        }
        if (change.RetiredGeneration is { } retired)
        {
            await Run(connection, tx, "INSERT INTO world_gathering_generations VALUES($1,$2,$3,$4,$5)", cancellation,
                map.WorldId, retired.TargetId, retired.Generation, retired.CreatedStock, retired.ConfirmedGathered);
            await Run(connection, tx, "UPDATE world_targets SET generation=$3 WHERE world_id=$1 AND target_id=$2", cancellation, map.WorldId, node.TargetId, node.Generation);
        }
    }
    public async Task<GatheringVisit?> FindAsync(Guid marchId, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        return (await ReadArrival(connection, null, marchId, cancellation))?.Visit;
    }
    public async Task<GatheringNode> NodeAsync(string targetId, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        return await ReadNode(connection, null, targetId, cancellation);
    }
    private async Task<GatheringNode> ReadNode(NpgsqlConnection connection, NpgsqlTransaction? tx, string targetId, CancellationToken cancellation)
    {
        string json = await Scalar<string>(connection, tx, "SELECT payload::text FROM world_gathering_nodes WHERE world_id=$1 AND target_id=$2", cancellation, map.WorldId, targetId);
        var node = JsonSerializer.Deserialize<GatheringNode>(json, AccountStore.Json) ?? throw new InvalidDataException("Missing gathering node.");
        WorldGathering.Validate(node);
        return node;
    }
    private async Task<StoredArrival?> ReadArrival(NpgsqlConnection connection, NpgsqlTransaction? tx, Guid march, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("""
            SELECT a.arrival_order,a.status,a.reason,a.cargo,a.completion_at,a.return_at,
              CASE WHEN a.status='collecting' THEN n.due_at ELSE a.return_at END,
              a.account_id,a.target_id,a.material,a.managed,a.travel_ms,a.request_hash
            FROM world_gathering_arrivals a JOIN world_gathering_nodes n ON n.world_id=a.world_id AND n.target_id=a.target_id
            WHERE a.march_id=$1 AND a.world_id=$2
            """, connection, tx);
        query.Parameters.AddWithValue(march); query.Parameters.AddWithValue(map.WorldId);
        await using var r = await query.ExecuteReaderAsync(cancellation);
        if (!await r.ReadAsync(cancellation)) { return null; }
        return new(new(march, r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4),
            r.IsDBNull(5) ? null : r.GetInt64(5), r.IsDBNull(6) ? null : r.GetInt64(6)), r.IsDBNull(7) ? null : r.GetGuid(7),
            r.GetString(8), r.GetString(9), r.GetBoolean(10), r.GetInt64(11), r.GetString(12));
    }
    private async Task<long> LockAsync(NpgsqlConnection connection, NpgsqlTransaction tx, CancellationToken cancellation)
    {
        await world.LockAllocationAsync(connection, tx, time.GetUtcNow().ToUnixTimeMilliseconds(), cancellation);
        return await Scalar<long>(connection, tx, "SELECT utc_highwater FROM world_instances WHERE world_id=$1", cancellation, map.WorldId);
    }
    private static async Task LockAccount(NpgsqlConnection connection, NpgsqlTransaction tx, Guid id, CancellationToken cancellation)
    {
        await Scalar<Guid>(connection, tx, "SELECT id FROM accounts WHERE id=$1 FOR UPDATE", cancellation, id);
        await using var deleted = new NpgsqlCommand("SELECT 1 FROM auth_deleted_accounts WHERE account_id=$1", connection, tx);
        deleted.Parameters.AddWithValue(id);
        if (await deleted.ExecuteScalarAsync(cancellation) is not null) { throw new CommandConflictException("account-deleted"); }
    }
    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql, CancellationToken cancellation, params object[] args)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var arg in args) { command.Parameters.AddWithValue(arg); }
        return await command.ExecuteScalarAsync(cancellation) is T value ? value : throw new CommandConflictException("gathering-state-missing");
    }
    private static async Task Run(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, CancellationToken cancellation, params object[] args)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        foreach (var arg in args)
        {
            if (arg == DBNull.Value) { command.Parameters.Add(new NpgsqlParameter { Value = arg, NpgsqlDbType = NpgsqlDbType.Bigint }); }
            else { command.Parameters.AddWithValue(arg); }
        }
        await command.ExecuteNonQueryAsync(cancellation);
    }
}
