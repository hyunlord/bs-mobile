using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed record WorldCombatRun(Guid RunId, Guid MarchId, Guid AccountId, string TargetId, long Generation,
    string EncounterId, string Status, long Revision, long DueAtMs, WorldCombatIssuedContract? Contract,
    ReplayCommand[] Commands, WorldInputFrontier Frontier, WorldCombatResult? Result, DomainCredit[] Credits)
{
    public Guid? VerificationId { get; init; }
    public long? VerificationUntilMs { get; init; }
    public long? BeforeVerificationDueAtMs { get; init; }
    public long? DefenderVersion { get; init; }
    public string? DefenderDomains { get; init; }
    public WorldTroopResult[]? DefenderResult { get; init; }
}
public sealed class WorldCombatRecoveryRequiredException(Guid runId) : Exception("world-combat-recovery-required")
{
    public Guid RunId { get; } = runId;
}
public sealed record WorldCombatRespawn(string TargetId, long DueAtMs, long Sequence);

/// <summary>World row, account UUID order, then run/target rows. Supplied transactions belong to the march writer.</summary>
public sealed partial class WorldCombatStore(NpgsqlDataSource source, WorldMapContent map, WorldCombatContent content,
    WorldCombatContractFactory factory, WorldCombatExecutor executor,
    AccountStore accounts,TrainingHandler training,TrainingContent trainingContent,CitySecurityPolicy securityPolicy)
{
    internal static readonly JsonSerializerOptions Json = CreateJson();
    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(AccountStore.Json);
        options.Converters.Add(new InputConverter());
        return options;
    }
    private sealed class InputConverter : JsonConverter<PlayerInput>
    {
        public override PlayerInput Read(ref Utf8JsonReader reader,Type type,JsonSerializerOptions options)
        {
            using var document=JsonDocument.ParseValue(ref reader);
            var input=document.RootElement;
            return new(input.GetProperty("x").GetInt16(),input.GetProperty("y").GetInt16());
        }
        public override void Write(Utf8JsonWriter writer,PlayerInput input,JsonSerializerOptions options)
        {
            writer.WriteStartObject();writer.WriteNumber("x",input.X);writer.WriteNumber("y",input.Y);writer.WriteEndObject();
        }
    }

    private const string Sql = """
        CREATE TABLE IF NOT EXISTS world_combat_schema(version integer PRIMARY KEY);
        INSERT INTO world_combat_schema VALUES(1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS world_combat_runs(
          run_id uuid PRIMARY KEY,march_id uuid NOT NULL UNIQUE,account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,
          world_id text NOT NULL REFERENCES world_instances(world_id),target_id text NOT NULL,generation bigint NOT NULL,
          revision bigint NOT NULL,payload jsonb NOT NULL);
        CREATE TABLE IF NOT EXISTS world_combat_eligibility(
          account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE CASCADE,eligibility_id text NOT NULL,
          run_id uuid NOT NULL,granted_ms bigint NOT NULL,PRIMARY KEY(account_id,eligibility_id));
        CREATE TABLE IF NOT EXISTS world_combat_targets(
          world_id text NOT NULL,target_id text NOT NULL,run_id uuid REFERENCES world_combat_runs(run_id) ON DELETE SET NULL DEFERRABLE INITIALLY DEFERRED,respawn_at bigint,
          sequence bigint NOT NULL DEFAULT nextval('world_gathering_sequence'),
          PRIMARY KEY(world_id,target_id),FOREIGN KEY(world_id,target_id) REFERENCES world_targets(world_id,target_id));

        """;

    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var t = await c.BeginTransactionAsync(ct);
        await Run(c,t,"SELECT pg_advisory_xact_lock(18083)",ct);
        await Run(c,t,Sql,ct);
        await using var q = new NpgsqlCommand("SELECT version FROM world_combat_schema WHERE version<>1",c,t);
        if (await q.ExecuteScalarAsync(ct) is not null)
        {
            throw new InvalidDataException("Unsupported world combat schema.");
        }

        await t.CommitAsync(ct);
    }

    public async Task<WorldCombatRun?> GetAsync(Guid accountId, Guid marchId, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await using var q = new NpgsqlCommand("SELECT payload::text FROM world_combat_runs WHERE account_id=$1 AND march_id=$2",c);
        q.Parameters.AddWithValue(accountId); q.Parameters.AddWithValue(marchId);
        return await q.ExecuteScalarAsync(ct) is string json ? Decode(json) : null;
    }

    public async Task<MarchActivityResult> ArriveAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        var old = await Find(c,t,march.MarchId,ct);
        if (old is not null)
        {
            return Next(old,march,at);
        }

        await using var target = new NpgsqlCommand("SELECT content_id FROM world_targets WHERE world_id=$1 AND target_id=$2 AND generation=$3 AND kind IN ('monster','stronghold')",c,t);
        target.Parameters.AddWithValue(map.WorldId);target.Parameters.AddWithValue(march.TargetId);target.Parameters.AddWithValue(march.Generation);
        if (await target.ExecuteScalarAsync(ct) is not string encounter)
        {
            return new("returning",checked(at+march.TravelMilliseconds),"target-gone");
        }

        await Run(c,t,"INSERT INTO world_combat_targets(world_id,target_id) VALUES($1,$2) ON CONFLICT DO NOTHING",ct,map.WorldId,march.TargetId);
        await using var lease = new NpgsqlCommand("UPDATE world_combat_targets SET run_id=$3 WHERE world_id=$1 AND target_id=$2 AND run_id IS NULL AND respawn_at IS NULL RETURNING target_id",c,t);
        var runId = Guid.NewGuid();
        lease.Parameters.AddWithValue(map.WorldId);lease.Parameters.AddWithValue(march.TargetId);lease.Parameters.AddWithValue(runId);
        if (await lease.ExecuteScalarAsync(ct) is null)
        {
            return new("returning",checked(at+march.TravelMilliseconds),"target-busy");
        }

        var run = new WorldCombatRun(runId,march.MarchId,march.AccountId,march.TargetId,march.Generation,encounter,"choice",0,
            checked(at+content.Rules.ChoiceTimeoutMilliseconds),null,[],new(at,at,0,0),null,[]);
        await Insert(c,t,run,ct);
        return Next(run,march,at);
    }

    public async Task<MarchActivityResult> AdvanceAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,bool cancel,CancellationToken ct)
    {
        var run = await Find(c,t,march.MarchId,ct) ?? throw new InvalidDataException("Missing battle receipt.");
        if (run.Status == "choice")
        {
            run = run with { Status="recalled", Revision=checked(run.Revision+1), DueAtMs=checked(at+march.TravelMilliseconds) };
            await Release(c,t,run,ct);await Save(c,t,run,ct);
            return Next(run,march,at);
        }
        // Cancellation of a running battle requires the verified Core quit result, never an invented K=0 result.
        if (run.Status is "active" or "recovery-pending")
        {
            if (cancel)
            {
                throw new CommandConflictException("battle-requires-verified-quit");
            }

            throw new WorldCombatRecoveryRequiredException(run.RunId);
        }
        return Next(run,march,at);
    }

    public async Task<WorldCombatReturn?> PrepareReturnAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldMarch march,long at,CancellationToken ct)
    {
        var run = await Find(c,t,march.MarchId,ct);
        if (run?.Result is null)
        {
            return null;
        }

        if (run.Status == "returned")
        {
            throw new InvalidDataException("Combat return already applied.");
        }

        if (run.Status != "committed")
        {
            throw new InvalidDataException("Unverified combat cannot return troops.");
        }

        await Save(c,t,run with { Status="returned",Revision=checked(run.Revision+1) },ct);
        return new(run.Result.Troops.ToArray(),run.Contract!.Award.InjuryPercent,run.Credits);
    }

    public async Task<WorldCombatRespawn?> NextEventAsync(NpgsqlConnection c,NpgsqlTransaction t,long now,CancellationToken ct)
    {
        await using var q = new NpgsqlCommand("SELECT target_id,respawn_at,sequence FROM world_combat_targets WHERE world_id=$1 AND respawn_at<=$2 ORDER BY respawn_at,sequence,target_id LIMIT 1",c,t);
        q.Parameters.AddWithValue(map.WorldId);q.Parameters.AddWithValue(now);
        await using var r=await q.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct)?new(r.GetString(0),r.GetInt64(1),r.GetInt64(2)):null;
    }

    public async Task RecoverTargetAsync(NpgsqlConnection c,NpgsqlTransaction t,WorldCombatRespawn due,CancellationToken ct)
    {
        await using var claim=new NpgsqlCommand("UPDATE world_combat_targets SET run_id=NULL,respawn_at=NULL WHERE world_id=$1 AND target_id=$2 AND respawn_at=$3 RETURNING target_id",c,t);
        claim.Parameters.AddWithValue(map.WorldId);claim.Parameters.AddWithValue(due.TargetId);claim.Parameters.AddWithValue(due.DueAtMs);
        if(await claim.ExecuteScalarAsync(ct) is null)
        {
            return;
        }

        await Run(c,t,"UPDATE world_targets SET generation=generation+1,created_ms=$3 WHERE world_id=$1 AND target_id=$2",ct,map.WorldId,due.TargetId,due.DueAtMs);
    }

    private static MarchActivityResult Next(WorldCombatRun run,WorldMarch march,long at) => new(
        run.Status=="choice"?"battle-choice":run.Status is "active" or "recovery-pending"?"battle-active":"returning",run.DueAtMs,run.Status);
    private async Task Release(NpgsqlConnection c,NpgsqlTransaction t,WorldCombatRun run,CancellationToken ct) =>
        await Run(c,t,"UPDATE world_combat_targets SET run_id=NULL WHERE world_id=$1 AND target_id=$2 AND run_id=$3",ct,map.WorldId,run.TargetId,run.RunId);
    private async Task Insert(NpgsqlConnection c,NpgsqlTransaction t,WorldCombatRun run,CancellationToken ct) =>
        await Run(c,t,"INSERT INTO world_combat_runs VALUES($1,$2,$3,$4,$5,$6,$7,$8::jsonb)",ct,run.RunId,run.MarchId,run.AccountId,map.WorldId,run.TargetId,run.Generation,run.Revision,JsonSerializer.Serialize(run,Json));
    private static async Task Save(NpgsqlConnection c,NpgsqlTransaction t,WorldCombatRun run,CancellationToken ct) =>
        await Run(c,t,"UPDATE world_combat_runs SET revision=$2,payload=$3::jsonb WHERE run_id=$1",ct,run.RunId,run.Revision,JsonSerializer.Serialize(run,Json));
    private static async Task<WorldCombatRun?> Find(NpgsqlConnection c,NpgsqlTransaction t,Guid marchId,CancellationToken ct)
    {
        await using var q=new NpgsqlCommand("SELECT payload::text FROM world_combat_runs WHERE march_id=$1 FOR UPDATE",c,t);
        q.Parameters.AddWithValue(marchId);
        return await q.ExecuteScalarAsync(ct) is string json?Decode(json):null;
    }
    private static WorldCombatRun Decode(string json)=>JsonSerializer.Deserialize<WorldCombatRun>(json,Json)??throw new InvalidDataException("Missing combat payload.");
    private static async Task Run(NpgsqlConnection c,NpgsqlTransaction t,string sql,CancellationToken ct,params object[] values)
    {
        await using var q=new NpgsqlCommand(sql,c,t);
        foreach(var value in values)
        {
            q.Parameters.AddWithValue(value);
        }

        await q.ExecuteNonQueryAsync(ct);
    }
}
