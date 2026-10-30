using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Infrastructure;

public sealed record RecruitOptions(byte[] SeedKey);

/// <summary>
/// Hero recruit module. It owns its tables and its own schema version so it does not take a server_schema
/// migration number; it shares the account row lock, account version, command receipts and outbox.
/// </summary>
public sealed class RecruitStore(NpgsqlDataSource dataSource, RecruitPool pool, RecruitOptions options)
{
    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS recruit_schema(version integer PRIMARY KEY);
        INSERT INTO recruit_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS hero_accounts(
          account_id uuid PRIMARY KEY REFERENCES accounts(id), gems bigint NOT NULL CHECK (gems >= 0),
          initial_gems bigint NOT NULL, pity integer NOT NULL CHECK (pity >= 0), draws bigint NOT NULL CHECK (draws >= 0),
          pool_family text NOT NULL, pool_version text NOT NULL);
        CREATE TABLE IF NOT EXISTS hero_roster(
          account_id uuid NOT NULL REFERENCES accounts(id), hero_id text NOT NULL, shards integer NOT NULL CHECK (shards >= 0),
          PRIMARY KEY(account_id,hero_id));
        CREATE TABLE IF NOT EXISTS recruit_ledger(
          account_id uuid NOT NULL REFERENCES accounts(id), draw_index bigint NOT NULL, command_id uuid NOT NULL,
          account_version bigint NOT NULL, pool_family text NOT NULL, pool_version text NOT NULL, policy_hash text NOT NULL,
          seed_hex text NOT NULL, roll bigint NOT NULL, guaranteed boolean NOT NULL, pity_before integer NOT NULL, pity_after integer NOT NULL,
          hero_id text NOT NULL, rarity text NOT NULL, outcome text NOT NULL, shards integer NOT NULL,
          gems_before bigint NOT NULL, gems_after bigint NOT NULL,
          PRIMARY KEY(account_id,draw_index), UNIQUE(account_id,command_id));
        """;

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var schemaLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(16016)", connection, transaction);
        await schemaLock.ExecuteNonQueryAsync(cancellation);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellation);
        await using var version = new NpgsqlCommand("SELECT max(version) FROM recruit_schema", connection, transaction);
        if (await version.ExecuteScalarAsync(cancellation) is not int current || current != 1)
        {
            throw new InvalidDataException("Unsupported recruit schema version.");
        }
        await transaction.CommitAsync(cancellation);
    }

    public async Task<RecruitOdds> OddsAsync(Guid id, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        long version = await AccountVersionAsync(connection, transaction, id, false, cancellation);
        var hero = await HeroAccountAsync(connection, transaction, id, cancellation);
        await transaction.CommitAsync(cancellation);
        return new(pool.PoolFamilyId, pool.PoolVersion, pool.PolicyHash, pool.CostGems, pool.GuaranteeAfterMisses, pool.GuaranteedRarity,
            pool.DuplicateShards, pool.Odds(0), pool.Odds(hero.Pity), hero.Pity, Math.Max(0, pool.GuaranteeAfterMisses - hero.Pity) + 1,
            hero.Gems, version);
    }

    public async Task<string> RecruitAsync(Guid id, RecruitCommand command, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        long version = await AccountVersionAsync(connection, transaction, id, true, cancellation);
        string payloadHash = Hash("hero-recruit\n" + JsonSerializer.Serialize(command, AccountStore.Json));
        await using (var receipt = new NpgsqlCommand("SELECT payload_hash,response FROM commands WHERE account_id=$1 AND command_id=$2", connection, transaction))
        {
            receipt.Parameters.AddWithValue(id); receipt.Parameters.AddWithValue(command.CommandId);
            await using var reader = await receipt.ExecuteReaderAsync(cancellation);
            if (await reader.ReadAsync(cancellation))
            {
                if (reader.GetString(0) != payloadHash)
                {
                    throw new CommandConflictException("command-id-reused");
                }
                return reader.GetString(1);
            }
        }
        if (command.ExpectedVersion != version)
        {
            throw new CommandConflictException("version-conflict");
        }
        var hero = await HeroAccountAsync(connection, transaction, id, cancellation);
        string? reason = command.PoolVersion != pool.PoolVersion ? "pool-changed"
            : hero.PoolFamily == pool.PoolFamilyId && hero.PoolVersion != pool.PoolVersion ? "pool-version-unsupported"
            : hero.Gems < pool.CostGems ? "insufficient-gems" : null;
        RecruitReply reply;
        if (reason is not null)
        {
            // Rejection before commit: no gem, counter, roster or account-version change; the receipt replays it.
            reply = new(false, reason, null, hero.Gems, hero.Pity, version, await RosterAsync(connection, transaction, id, cancellation));
        }
        else
        {
            var draw = RecruitDraw.Draw(pool, options.SeedKey, id, hero.Draws, hero.Pity);
            int shards = 0;
            await using (var owned = new NpgsqlCommand("SELECT shards FROM hero_roster WHERE account_id=$1 AND hero_id=$2 FOR UPDATE", connection, transaction))
            {
                owned.Parameters.AddWithValue(id); owned.Parameters.AddWithValue(draw.Entry.HeroId);
                bool duplicate = await owned.ExecuteScalarAsync(cancellation) is int;
                shards = duplicate ? pool.DuplicateShards[draw.Entry.Rarity] : 0;
                string grant = duplicate
                    ? "UPDATE hero_roster SET shards=shards+$3 WHERE account_id=$1 AND hero_id=$2"
                    : "INSERT INTO hero_roster VALUES($1,$2,$3)";
                await using var write = new NpgsqlCommand(grant, connection, transaction);
                write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(draw.Entry.HeroId); write.Parameters.AddWithValue(shards);
                await write.ExecuteNonQueryAsync(cancellation);
            }
            long gemsAfter = hero.Gems - pool.CostGems;
            long next = checked(version + 1);
            await Execute(connection, transaction, "UPDATE hero_accounts SET gems=$2,pity=$3,draws=draws+1 WHERE account_id=$1", cancellation, id, gemsAfter, draw.PityAfter);
            await Execute(connection, transaction, "UPDATE accounts SET version=$2 WHERE id=$1", cancellation, id, next);
            string outcome = shards == 0 ? "unlock" : "shards";
            await Execute(connection, transaction, "INSERT INTO recruit_ledger VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18)", cancellation,
                id, draw.DrawIndex, command.CommandId, next, pool.PoolFamilyId, pool.PoolVersion, pool.PolicyHash, draw.SeedHex, draw.Roll,
                draw.Guaranteed, draw.PityBefore, draw.PityAfter, draw.Entry.HeroId, draw.Entry.Rarity, outcome, shards, hero.Gems, gemsAfter);
            await Execute(connection, transaction, "INSERT INTO outbox VALUES($1,$2,'hero-recruited')", cancellation, id, next);
            reply = new(true, "recruited", new(draw.DrawIndex, draw.Entry.HeroId, draw.Entry.Rarity, outcome, shards, draw.Guaranteed, draw.PityBefore, draw.PityAfter),
                gemsAfter, draw.PityAfter, next, await RosterAsync(connection, transaction, id, cancellation));
        }
        string response = JsonSerializer.Serialize(reply, AccountStore.Json);
        await Execute(connection, transaction, "INSERT INTO commands VALUES($1,$2,$3,$4)", cancellation, id, command.CommandId, payloadHash, response);
        await transaction.CommitAsync(cancellation);
        return response;
    }

    private sealed record HeroAccount(long Gems, int Pity, long Draws, string PoolFamily, string PoolVersion);

    private async Task<HeroAccount> HeroAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellation)
    {
        // Prototype-only grant: no gem source exists before #122, so a recruit row starts with a fixed development balance.
        await Execute(connection, transaction, "INSERT INTO hero_accounts VALUES($1,$2,$2,0,0,$3,$4) ON CONFLICT DO NOTHING", cancellation,
            id, (long)pool.PrototypeInitialGems, pool.PoolFamilyId, pool.PoolVersion);
        await using var read = new NpgsqlCommand("SELECT gems,pity,draws,pool_family,pool_version FROM hero_accounts WHERE account_id=$1", connection, transaction);
        read.Parameters.AddWithValue(id);
        await using var reader = await read.ExecuteReaderAsync(cancellation);
        await reader.ReadAsync(cancellation);
        return new(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetString(3), reader.GetString(4));
    }

    private static async Task<long> AccountVersionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, bool lockRow, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand("SELECT version FROM accounts WHERE id=$1" + (lockRow ? " FOR UPDATE" : ""), connection, transaction);
        command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync(cancellation) is long version ? version : throw new InvalidDataException("Account missing.");
    }

    private static async Task<HeroHolding[]> RosterAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand("SELECT hero_id,shards FROM hero_roster WHERE account_id=$1 ORDER BY hero_id", connection, transaction);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var list = new List<HeroHolding>();
        while (await reader.ReadAsync(cancellation))
        {
            list.Add(new(reader.GetString(0), reader.GetInt32(1)));
        }
        return list.ToArray();
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellation, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (object value in values)
        {
            command.Parameters.AddWithValue(value);
        }
        await command.ExecuteNonQueryAsync(cancellation);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
