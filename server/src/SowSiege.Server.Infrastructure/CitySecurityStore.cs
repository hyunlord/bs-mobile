using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

/// <summary>
/// City security owns no account wallet writes. Lock order: optional world lock, accounts UUID ascending,
/// then city security rows. Membership is read after account locks; this store never locks an alliance row.
/// March hooks participate in the caller's transaction and must precede its final commit.
/// </summary>
public sealed class CitySecurityStore(NpgsqlDataSource source, CitySecurityPolicy policy, TimeProvider time, WorldRecoveryGate? gate = null)
{
    private static readonly JsonSerializerOptions Json = AccountStore.Json;
    private readonly WorldRecoveryGate recovery = gate ?? new(source, time);

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await Run(c, t, "SELECT pg_advisory_xact_lock(642)", ct);
        await Run(c, t, "CREATE TABLE IF NOT EXISTS city_security_schema(version integer PRIMARY KEY)", ct);
        var old = await Scalar(c, t, "SELECT max(version) FROM city_security_schema", ct);
        if (old is int version && version != 1) { throw new InvalidDataException("Unsupported city security schema."); }
        await Run(c, t, """
            CREATE TABLE IF NOT EXISTS city_security(
              account_id uuid PRIMARY KEY REFERENCES accounts(id) ON DELETE CASCADE, content_hash text NOT NULL,
              state_json jsonb NOT NULL);
            CREATE TABLE IF NOT EXISTS city_hostilities(
              march_id uuid PRIMARY KEY, actor uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, target uuid REFERENCES accounts(id) ON DELETE SET NULL,
              operation text NOT NULL CHECK(operation IN ('scout','attack')), dispatched_ms bigint NOT NULL,
              arrival_json jsonb, returned_ms bigint);
            CREATE TABLE IF NOT EXISTS city_shield_commands(
              account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, command_id uuid NOT NULL, reply_json jsonb NOT NULL,
              PRIMARY KEY(account_id,command_id));
            CREATE TABLE IF NOT EXISTS city_security_events(
              sequence bigserial PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
              entity_id uuid NOT NULL, at_ms bigint NOT NULL, kind text NOT NULL);
            INSERT INTO city_security_schema VALUES(1) ON CONFLICT DO NOTHING;
            """, ct);
        await t.CommitAsync(ct);
    }

    /// <summary>Called once inside account creation, with its original server UTC; never on device login or recovery.</summary>
    public Task RegisterCreatedAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid account, long createdAtMs, CancellationToken ct) =>
        Save(c, t, account, CitySecurityRules.Create(createdAtMs, policy, true), ct, true);

    public Task<CityShieldState> ViewAsync(Guid account, CancellationToken ct) =>
        recovery.ExecuteAsync(async (c, t, now, token) =>
        {
            now = await LockAccounts(c, t, [account], now, token);
            var state = await Read(c, t, account, now, token);
            await Save(c, t, account, state, token);
            return state;
        }, ct);

    public Task<CityShieldReply> ActivateAsync(Guid account, Guid commandId, CancellationToken ct)
    {
        if (commandId == Guid.Empty) { throw new CommandConflictException("invalid-command"); }
        return recovery.ExecuteAsync(async (c, t, now, token) =>
        {
            now = await LockAccounts(c, t, [account], now, token);
            var existing = await Scalar(c, t, "SELECT reply_json::text FROM city_shield_commands WHERE account_id=$1 AND command_id=$2", token, account, commandId);
            if (existing is string json) { return JsonSerializer.Deserialize<CityShieldReply>(json, Json)!; }
            var state = await Read(c, t, account, now, token);
            bool active = (bool)(await Scalar(c, t, "SELECT EXISTS(SELECT 1 FROM city_hostilities WHERE actor=$1 AND returned_ms IS NULL)", token, account))!;
            var reply = CitySecurityRules.Activate(state, now, active, policy);
            await Save(c, t, account, reply.State, token);
            await WriteJson(c, t, "INSERT INTO city_shield_commands VALUES($1,$2,$3)", account, commandId, reply, token);
            await Event(c, t, account, commandId, reply.State.UtcHighWatermark, reply.Reason, token);
            return reply;
        }, ct);
    }

    /// <summary>Invoke only after march route, generation, troop, hero and slot checks succeed in this same transaction.</summary>
    public async Task<CitySecurityDecision> DispatchAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid marchId,
        Guid actor, Guid target, string operation, long now, CancellationToken ct)
    {
        ValidateIdentity(marchId, actor, target, operation);
        now = await LockAccounts(c, t, [actor, target], now, ct);
        await using (var query = new NpgsqlCommand("SELECT actor,target,operation FROM city_hostilities WHERE march_id=$1", c, t))
        {
            query.Parameters.AddWithValue(marchId);
            await using var reader = await query.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetGuid(0) != actor || reader.GetGuid(1) != target || reader.GetString(2) != operation)
                {
                    throw new CommandConflictException("march-id-reused");
                }
                return new(true, "dispatched");
            }
        }
        var mine = await Read(c, t, actor, now, ct);
        var theirs = await Read(c, t, target, now, ct);
        now = Math.Max(mine.UtcHighWatermark, theirs.UtcHighWatermark);
        string reason = await Eligibility(c, t, actor, target, theirs, now, ct);
        if (reason.Length == 0 && operation == "attack")
        {
            var returned = await Scalar(c, t, "SELECT max(returned_ms) FROM city_hostilities WHERE actor=$1 AND target=$2 AND operation='attack'", ct, actor, target);
            if (returned is long last && now < checked(last + policy.RepeatAttackCooldownMilliseconds)) { reason = "repeat-attack-cooldown"; }
        }
        // Rejected dispatch advances only the security clock; it does not remove protection or register a march.
        await Save(c, t, actor, mine with { UtcHighWatermark = now, ShieldUntilMs = reason.Length == 0 ? 0 : mine.ShieldUntilMs }, ct);
        await Save(c, t, target, theirs with { UtcHighWatermark = now }, ct);
        if (reason.Length != 0) { return new(false, reason); }
        await Run(c, t, "INSERT INTO city_hostilities(march_id,actor,target,operation,dispatched_ms) VALUES($1,$2,$3,$4,$5)", ct, marchId, actor, target, operation, now);
        await Event(c, t, actor, marchId, now, "dispatched-" + operation, ct);
        return new(true, "dispatched");
    }

    /// <summary>Caller must advance both account domains to this arrival time through the common writer before this snapshot.</summary>
    public async Task<CitySecurityDecision> ArriveAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid marchId,
        Guid actor, Guid target, string operation, long now, CancellationToken ct)
    {
        ValidateIdentity(marchId, actor, target, operation);
        now = await LockAccounts(c, t, [actor, target], now, ct, allowDeleted: true, allowMissing: true);
        var saved = await Hostility(c, t, marchId, actor, target, operation, ct);
        if (saved.Reply is not null) { return saved.Reply; }
        if (saved.Returned) { throw new CommandConflictException("march-returned"); }
        if (saved.TargetMissing) { return new(false, "account-missing"); }
        var mine = await Read(c, t, actor, now, ct);
        var theirs = await Read(c, t, target, now, ct);
        now = Math.Max(mine.UtcHighWatermark, theirs.UtcHighWatermark);
        string reason = await Eligibility(c, t, actor, target, theirs, now, ct);
        var decision = new CitySecurityDecision(reason.Length == 0, reason.Length == 0 ? "arrived" : reason,
            reason.Length == 0 && operation == "scout" ? await Scout(c, t, target, now, ct) : null);
        await Save(c, t, actor, mine with { UtcHighWatermark = now }, ct);
        await Save(c, t, target, theirs with { UtcHighWatermark = now }, ct);
        await using var write = new NpgsqlCommand("UPDATE city_hostilities SET arrival_json=$2 WHERE march_id=$1", c, t);
        write.Parameters.AddWithValue(marchId); write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(decision, Json));
        await write.ExecuteNonQueryAsync(ct);
        await Event(c, t, actor, marchId, now, "arrival-" + decision.Reason, ct);
        return decision;
    }

    public async Task ReturnAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid marchId, Guid actor, long now, CancellationToken ct)
    {
        now = await LockAccounts(c, t, [actor], now, ct, allowDeleted: true);
        var state = await Read(c, t, actor, now, ct);
        now = state.UtcHighWatermark;
        await using var update = new NpgsqlCommand("UPDATE city_hostilities SET returned_ms=$3 WHERE march_id=$1 AND actor=$2 AND returned_ms IS NULL", c, t);
        update.Parameters.AddWithValue(marchId); update.Parameters.AddWithValue(actor); update.Parameters.AddWithValue(now);
        if (await update.ExecuteNonQueryAsync(ct) == 0)
        {
            if (await Scalar(c, t, "SELECT 1 FROM city_hostilities WHERE march_id=$1 AND actor=$2", ct, marchId, actor) is null)
            {
                throw new CommandConflictException("unknown-hostile-march");
            }
            return;
        }
        await Save(c, t, actor, state with { HostileUntilMs = Math.Max(state.HostileUntilMs, checked(now + policy.HostileReturnCooldownMilliseconds)) }, ct);
        await Event(c, t, actor, marchId, now, "returned", ct);
    }

    private async Task<CityScoutReport> Scout(NpgsqlConnection c, NpgsqlTransaction t, Guid target, long now, CancellationToken ct)
    {
        var payload = (byte[])(await Scalar(c, t, "SELECT payload FROM accounts WHERE id=$1", ct, target))!;
        var decoded = MetaSaveCodec.Decode(payload);
        if (!decoded.Valid || decoded.State is null) { throw new InvalidDataException("Invalid scout account snapshot."); }
        var troops = new List<ScoutTroops>();
        var stored = await Scalar(c, t, "SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='training'", ct, target);
        if (stored is string json)
        {
            using var document = JsonDocument.Parse(json);
            foreach (var unit in document.RootElement.GetProperty("units").EnumerateArray())
            {
                string id = unit.GetProperty("unitId").GetString() ?? throw new InvalidDataException("Invalid troop id.");
                int ready = unit.GetProperty("ready").GetInt32();
                if (ready < 0) { throw new InvalidDataException("Invalid ready troops."); }
                troops.Add(new(id, ready));
            }
        }
        return new(now, false, troops.OrderBy(u => u.UnitId, StringComparer.Ordinal).ToArray(),
            policy.Materials.Select(m => new ScoutLoot(m.MaterialId,
                CitySecurityRules.LootLimit(decoded.State.Wallet.GetValueOrDefault(m.MaterialId), m.ProtectedAmount, policy.LootPermille))).ToArray());
    }

    private async Task<string> Eligibility(NpgsqlConnection c, NpgsqlTransaction t, Guid actor, Guid target, CityShieldState shield, long now, CancellationToken ct)
    {
        if (actor == target) { return "own-city"; }
        if ((bool)(await Scalar(c, t, "SELECT EXISTS(SELECT 1 FROM auth_deleted_accounts WHERE account_id=ANY($1))", ct, new[] { actor, target }))!)
        {
            return "account-deleted";
        }
        if ((bool)(await Scalar(c, t, """
            SELECT EXISTS(SELECT 1 FROM alliance_members a JOIN alliance_members b ON a.alliance_id=b.alliance_id
              WHERE a.account_id=$1 AND b.account_id=$2)
            """, ct, actor, target))!) { return "same-alliance"; }
        return CitySecurityRules.Protected(shield, now) ? "target-protected" : "";
    }

    private async Task<CityShieldState> Read(NpgsqlConnection c, NpgsqlTransaction t, Guid account, long now, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT content_hash,state_json::text FROM city_security WHERE account_id=$1", c, t);
        query.Parameters.AddWithValue(account);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) { return CitySecurityRules.Create(now, policy, false); }
        if (reader.GetString(0) != policy.Hash) { throw new CommandConflictException("city-security-content-mismatch"); }
        var state = JsonSerializer.Deserialize<CityShieldState>(reader.GetString(1), Json) ?? throw new InvalidDataException("Invalid shield state.");
        return state with { UtcHighWatermark = Math.Max(now, state.UtcHighWatermark) };
    }

    private async Task Save(NpgsqlConnection c, NpgsqlTransaction t, Guid account, CityShieldState state, CancellationToken ct, bool insertOnly = false)
    {
        await using var query = new NpgsqlCommand("INSERT INTO city_security VALUES($1,$2,$3)" +
            (insertOnly ? "" : " ON CONFLICT(account_id) DO UPDATE SET state_json=excluded.state_json"), c, t);
        query.Parameters.AddWithValue(account); query.Parameters.AddWithValue(policy.Hash);
        query.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(state, Json));
        await query.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> LockAccounts(NpgsqlConnection c, NpgsqlTransaction t, Guid[] ids, long now, CancellationToken ct, bool allowDeleted = false, bool allowMissing = false)
    {
        int found = 0;
        await using (var query = new NpgsqlCommand("SELECT id,utc_highwater FROM accounts WHERE id=ANY($1) ORDER BY id FOR UPDATE", c, t))
        {
            query.Parameters.AddWithValue(ids);
            await using var reader = await query.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { found++; now = Math.Max(now, reader.GetInt64(1)); }
        }
        if (!allowMissing && found != ids.Distinct().Count()) { throw new CommandConflictException("account-missing"); }
        var jobClock = await Scalar(c, t, "SELECT max(utc_highwater) FROM account_job_clocks WHERE account_id=ANY($1)", ct, ids);
        if (jobClock is long value) { now = Math.Max(now, value); }
        if (!allowDeleted && (bool)(await Scalar(c, t, "SELECT EXISTS(SELECT 1 FROM auth_deleted_accounts WHERE account_id=ANY($1))", ct, ids))!)
        {
            throw new CommandConflictException("account-deleted");
        }
        return now;
    }

    private static async Task<(CitySecurityDecision? Reply, bool Returned, bool TargetMissing)> Hostility(NpgsqlConnection c, NpgsqlTransaction t,
        Guid marchId, Guid actor, Guid target, string operation, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand("SELECT actor,target,operation,arrival_json::text,returned_ms FROM city_hostilities WHERE march_id=$1", c, t);
        query.Parameters.AddWithValue(marchId);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) { throw new CommandConflictException("unknown-hostile-march"); }
        if (reader.GetGuid(0) != actor || (!reader.IsDBNull(1) && reader.GetGuid(1) != target) || reader.GetString(2) != operation) { throw new CommandConflictException("march-id-reused"); }
        return (reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<CitySecurityDecision>(reader.GetString(3), Json), !reader.IsDBNull(4), reader.IsDBNull(1));
    }

    private static void ValidateIdentity(Guid march, Guid actor, Guid target, string operation)
    {
        if (march == Guid.Empty || actor == Guid.Empty || target == Guid.Empty || operation is not ("scout" or "attack"))
        {
            throw new CommandConflictException("invalid-hostile-march");
        }
    }

    private static Task Event(NpgsqlConnection c, NpgsqlTransaction t, Guid account, Guid entity, long now, string kind, CancellationToken ct) =>
        Run(c, t, "INSERT INTO city_security_events(account_id,entity_id,at_ms,kind) VALUES($1,$2,$3,$4)", ct, account, entity, now, kind);

    private static async Task WriteJson(NpgsqlConnection c, NpgsqlTransaction t, string sql, Guid account, Guid command, object value, CancellationToken ct)
    {
        await using var query = new NpgsqlCommand(sql, c, t);
        query.Parameters.AddWithValue(account); query.Parameters.AddWithValue(command);
        query.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(value, Json));
        await query.ExecuteNonQueryAsync(ct);
    }

    private static async Task<object?> Scalar(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params object[] values)
    {
        await using var query = new NpgsqlCommand(sql, c, t);
        foreach (object value in values) { query.Parameters.AddWithValue(value); }
        object? result = await query.ExecuteScalarAsync(ct);
        return result is DBNull ? null : result;
    }

    private static async Task Run(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params object[] values)
    {
        await using var query = new NpgsqlCommand(sql, c, t);
        foreach (object value in values) { query.Parameters.AddWithValue(value); }
        await query.ExecuteNonQueryAsync(ct);
    }

}
