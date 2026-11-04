using System.Data;
using Npgsql;
using SowSiege.Server.Application;

namespace SowSiege.Server.Infrastructure;

public sealed class WorldCapacityException() : Exception("world-capacity-exhausted");
public sealed record WorldTargetView(string TargetId, long Generation, string Kind, string ContentId,
    int X, int Y, Guid? AccountId, string? AllianceName);
public sealed record WorldChunkView(string WorldId, string ContentHash, int ChunkX, int ChunkY,
    WorldTile[] Tiles, WorldTargetView[] Targets);

/// <summary>
/// World topology is pinned by its content hash; mutable target identities live in PostgreSQL.
/// Allocation lock order: world row, then account row. Never enter this store while holding an account lock.
/// Future interactions must take world, alliances (UUID order), accounts (UUID order), then targets.
/// </summary>
public sealed class WorldStore(NpgsqlDataSource source, WorldMapContent map)
{
    private const string Sql = """
        CREATE TABLE IF NOT EXISTS world_schema(version integer PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS world_instances(
          world_id text PRIMARY KEY, content_hash text NOT NULL, utc_highwater bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS world_locations(
          world_id text NOT NULL REFERENCES world_instances(world_id), x integer NOT NULL CHECK(x>=0), y integer NOT NULL CHECK(y>=0),
          city_slot boolean NOT NULL, PRIMARY KEY(world_id,x,y), UNIQUE(world_id,x,y,city_slot));
        CREATE TABLE IF NOT EXISTS world_targets(
          world_id text NOT NULL REFERENCES world_instances(world_id), target_id text NOT NULL, generation bigint NOT NULL CHECK(generation>0),
          kind text NOT NULL CHECK(kind IN ('city','resource','monster','stronghold')), content_id text NOT NULL,
          x integer NOT NULL, y integer NOT NULL, city_slot boolean NOT NULL,
          account_id uuid UNIQUE REFERENCES accounts(id) ON DELETE CASCADE, created_ms bigint NOT NULL,
          PRIMARY KEY(world_id,target_id), UNIQUE(world_id,x,y),
          FOREIGN KEY(world_id,x,y,city_slot) REFERENCES world_locations(world_id,x,y,city_slot),
          CHECK((kind='city')=city_slot), CHECK((kind='city')=(account_id IS NOT NULL)));
        CREATE INDEX IF NOT EXISTS world_targets_chunk ON world_targets(world_id,x,y);
        INSERT INTO world_schema VALUES(1) ON CONFLICT DO NOTHING;
        """;

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await Run(connection, transaction, "SELECT pg_advisory_xact_lock(18018)", cancellation);
        await Run(connection, transaction, "CREATE TABLE IF NOT EXISTS world_schema(version integer PRIMARY KEY)", cancellation);
        await using (var version = new NpgsqlCommand("SELECT version FROM world_schema ORDER BY version", connection, transaction))
        await using (var rows = await version.ExecuteReaderAsync(cancellation))
        {
            while (await rows.ReadAsync(cancellation))
            {
                if (rows.GetInt32(0) != 1) { throw new InvalidDataException("Unsupported world schema version."); }
            }
        }
        await Run(connection, transaction, Sql, cancellation);
        await using (var existing = new NpgsqlCommand("SELECT world_id,content_hash FROM world_instances FOR UPDATE", connection, transaction))
        await using (var rows = await existing.ExecuteReaderAsync(cancellation))
        {
            if (await rows.ReadAsync(cancellation))
            {
                if (rows.GetString(0) != map.WorldId || rows.GetString(1) != map.Hash || await rows.ReadAsync(cancellation))
                {
                    throw new InvalidDataException("World content changed; explicit migration required.");
                }
                await rows.DisposeAsync();
                await transaction.CommitAsync(cancellation);
                return;
            }
        }
        await Run(connection, transaction, "INSERT INTO world_instances VALUES($1,$2,0)", cancellation, map.WorldId, map.Hash);
        // One array insert avoids one command per tile. Only validated, connected locations enter the database.
        var locations = map.CitySlots.Select(p => (p.X, p.Y, City: true))
            .Concat(map.InitialTargets.Select(t => (t.X, t.Y, City: false))).ToArray();
        await Run(connection, transaction, """
            INSERT INTO world_locations SELECT $1,x,y,city FROM unnest($2::integer[],$3::integer[],$4::boolean[]) AS p(x,y,city)
            """, cancellation, map.WorldId, locations.Select(p => p.X).ToArray(), locations.Select(p => p.Y).ToArray(), locations.Select(p => p.City).ToArray());
        foreach (var target in map.InitialTargets)
        {
            await Run(connection, transaction, """
                INSERT INTO world_targets(world_id,target_id,generation,kind,content_id,x,y,city_slot,account_id,created_ms)
                VALUES($1,$2,$3,$4,$5,$6,$7,false,NULL,0)
                """, cancellation, map.WorldId, target.TargetId, (long)target.Generation, target.Kind, target.ContentId, target.X, target.Y);
        }
        await transaction.CommitAsync(cancellation);
    }

    internal async Task LockAllocationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long nowMs, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand("SELECT content_hash FROM world_instances WHERE world_id=$1 FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue(map.WorldId);
        if (await command.ExecuteScalarAsync(cancellation) is not string hash || hash != map.Hash)
        {
            throw new InvalidDataException("World not initialized or content mismatch.");
        }
        await Run(connection, transaction, "UPDATE world_instances SET utc_highwater=greatest(utc_highwater,$2) WHERE world_id=$1", cancellation, map.WorldId, nowMs);
    }

    // The caller owns the world lock and has inserted/locked the account in the same transaction.
    internal async Task<WorldTargetView> AllocateCityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, CancellationToken cancellation)
    {
        var existing = await CityAsync(connection, transaction, account, cancellation);
        if (existing is not null) { return existing; }
        await using var insert = new NpgsqlCommand("""
            INSERT INTO world_targets(world_id,target_id,generation,kind,content_id,x,y,city_slot,account_id,created_ms)
            SELECT p.world_id,$2,1,'city','account:city',p.x,p.y,true,$3,w.utc_highwater
            FROM world_locations p JOIN world_instances w USING(world_id)
            WHERE p.world_id=$1 AND p.city_slot AND NOT EXISTS(
              SELECT 1 FROM world_targets t WHERE t.world_id=p.world_id AND t.x=p.x AND t.y=p.y)
            ORDER BY p.x,p.y LIMIT 1 RETURNING target_id,generation,kind,content_id,x,y,account_id
            """, connection, transaction);
        insert.Parameters.AddWithValue(map.WorldId); insert.Parameters.AddWithValue("city:" + account.ToString("D")); insert.Parameters.AddWithValue(account);
        await using var reader = await insert.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation)) { throw new WorldCapacityException(); }
        return Target(reader);
    }

    /// <summary>Idempotent enrollment for pre-world accounts; never rewrites their gameplay payload or version.</summary>
    public async Task<WorldTargetView> EnsureCityAsync(Guid account, long nowMs, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await LockAllocationAsync(connection, transaction, nowMs, cancellation);
        await using (var locked = new NpgsqlCommand("SELECT id FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            locked.Parameters.AddWithValue(account);
            if (await locked.ExecuteScalarAsync(cancellation) is not Guid) { throw new CommandConflictException("unknown-account"); }
        }
        await using (var deleted = new NpgsqlCommand("SELECT 1 FROM auth_deleted_accounts WHERE account_id=$1", connection, transaction))
        {
            deleted.Parameters.AddWithValue(account);
            if (await deleted.ExecuteScalarAsync(cancellation) is not null) { throw new CommandConflictException("account-deleted"); }
        }
        var city = await AllocateCityAsync(connection, transaction, account, cancellation);
        await transaction.CommitAsync(cancellation);
        return city;
    }

    public async Task<WorldTargetView?> FindCityAsync(Guid account, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var city = await CityAsync(connection, transaction, account, cancellation);
        await transaction.CommitAsync(cancellation);
        return city;
    }

    private async Task<WorldTargetView?> CityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("SELECT target_id,generation,kind,content_id,x,y,account_id FROM world_targets WHERE world_id=$1 AND account_id=$2", connection, transaction);
        query.Parameters.AddWithValue(map.WorldId); query.Parameters.AddWithValue(account);
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        return await reader.ReadAsync(cancellation) ? Target(reader) : null;
    }

    public async Task<WorldChunkView> ChunkAsync(int chunkX, int chunkY, CancellationToken cancellation)
    {
        await using var connection = await source.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellation);
        var result = await ChunkAsync(connection, transaction, chunkX, chunkY, cancellation);
        await transaction.CommitAsync(cancellation);
        return result;
    }

    public async Task<WorldChunkView> ChunkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        int chunkX, int chunkY, CancellationToken cancellation)
    {
        var tiles = map.Chunk(chunkX, chunkY);
        int x = checked(chunkX * map.ChunkSize), y = checked(chunkY * map.ChunkSize);
        await using var query = new NpgsqlCommand("""
            SELECT t.target_id,t.generation,t.kind,t.content_id,t.x,t.y,t.account_id,a.name
            FROM world_targets t LEFT JOIN alliance_members m ON m.account_id=t.account_id
            LEFT JOIN alliances a ON a.id=m.alliance_id AND a.status='active'
            WHERE t.world_id=$1 AND t.x >= $2 AND t.x < $3 AND t.y >= $4 AND t.y < $5
              AND NOT EXISTS(SELECT 1 FROM auth_deleted_accounts d WHERE d.account_id=t.account_id)
            ORDER BY t.x,t.y,t.target_id
            """, connection, transaction);
        query.Parameters.AddWithValue(map.WorldId); query.Parameters.AddWithValue(x); query.Parameters.AddWithValue(Math.Min(map.Width, x + map.ChunkSize));
        query.Parameters.AddWithValue(y); query.Parameters.AddWithValue(Math.Min(map.Height, y + map.ChunkSize));
        var targets = new List<WorldTargetView>();
        await using (var rows = await query.ExecuteReaderAsync(cancellation))
        {
            while (await rows.ReadAsync(cancellation))
            {
                targets.Add(Target(rows) with { AllianceName = rows.IsDBNull(7) ? null : rows.GetString(7) });
            }
        }
        return new(map.WorldId, map.Hash, chunkX, chunkY, tiles, targets.ToArray());
    }

    private static WorldTargetView Target(NpgsqlDataReader reader) => new(reader.GetString(0), reader.GetInt64(1), reader.GetString(2),
        reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetGuid(6), null);

    private static async Task Run(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellation, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) { command.Parameters.AddWithValue(value); }
        await command.ExecuteNonQueryAsync(cancellation);
    }
}
