using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Infrastructure;

/// <summary>
/// Research module. Owns its tables and schema version; shares the account row lock, version, wallet payload,
/// resource ledger, command receipts and outbox. Completion time is the server UTC time fixed at start.
/// </summary>
public sealed class ResearchStore(NpgsqlDataSource dataSource, ResearchContent content, TimeProvider time)
{
    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS research_schema(version integer PRIMARY KEY);
        INSERT INTO research_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS research_jobs(
          account_id uuid NOT NULL REFERENCES accounts(id), seq bigint NOT NULL, node_id text NOT NULL, level integer NOT NULL,
          status text NOT NULL CHECK (status IN ('active','completed')), started_at timestamptz NOT NULL,
          completes_at timestamptz NOT NULL, completed_at timestamptz, content_hash text NOT NULL, start_command uuid NOT NULL,
          PRIMARY KEY(account_id,seq), UNIQUE(account_id,node_id,level));
        CREATE TABLE IF NOT EXISTS research_levels(
          account_id uuid NOT NULL REFERENCES accounts(id), node_id text NOT NULL, level integer NOT NULL CHECK (level > 0),
          completed_at timestamptz NOT NULL, job_seq bigint NOT NULL, PRIMARY KEY(account_id,node_id));
        CREATE TABLE IF NOT EXISTS research_proofs(
          account_id uuid NOT NULL REFERENCES accounts(id), proof_id text NOT NULL, source text NOT NULL,
          PRIMARY KEY(account_id,proof_id));
        """;

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var schemaLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(17017)", connection, transaction);
        await schemaLock.ExecuteNonQueryAsync(cancellation);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellation);
        await using var version = new NpgsqlCommand("SELECT max(version) FROM research_schema", connection, transaction);
        if (await version.ExecuteScalarAsync(cancellation) is not int current || current != 1)
        {
            throw new InvalidDataException("Unsupported research schema version.");
        }
        await transaction.CommitAsync(cancellation);
    }

    public async Task<ResearchView> ViewAsync(Guid id, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var now = time.GetUtcNow();
        var account = await AccountStore.ReadAsync(connection, transaction, id, cancellation);
        var settled = await SettleAsync(connection, transaction, id, now, cancellation);
        if (settled.Length != 0)
        {
            account = await BumpAsync(connection, transaction, account, account.State, "research-completed", cancellation);
        }
        var view = await ViewAsync(connection, transaction, account, now, settled, cancellation);
        await transaction.CommitAsync(cancellation);
        return view;
    }

    public async Task<string> ExecuteAsync(Guid id, ResearchCommand command, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var now = time.GetUtcNow();
        var account = await AccountStore.ReadAsync(connection, transaction, id, cancellation);
        string payloadHash = Hash("research\n" + JsonSerializer.Serialize(command, AccountStore.Json));
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
        if (command.ExpectedVersion != account.Version)
        {
            throw new CommandConflictException("version-conflict");
        }
        // Finished jobs settle first, in server event order, exactly once (status active -> completed).
        var settled = await SettleAsync(connection, transaction, id, now, cancellation);
        bool done;
        string reason;
        var state = account.State;
        if (command.Type == "complete-research")
        {
            done = settled.Length != 0;
            reason = done ? "completed" : (await ActiveAsync(connection, transaction, id, cancellation)).Count != 0 ? "not-finished" : "no-active-research";
        }
        else
        {
            var node = content.Nodes.SingleOrDefault(n => n.Id == command.NodeId);
            var levels = await LevelsAsync(connection, transaction, id, cancellation);
            var active = await ActiveAsync(connection, transaction, id, cancellation);
            string[] locks = node is null ? [] : ResearchRules.LockReasons(node, state, levels, await ProofsAsync(connection, transaction, id, cancellation));
            reason = node is null ? "unknown-node"
                : levels.GetValueOrDefault(node.Id) >= node.Level ? "already-completed"
                : active.Any(j => j.NodeId == node.Id) ? "already-active"
                : active.Count >= content.ActiveSlots ? "queue-full"
                : locks.Length != 0 ? "locked:" + string.Join(',', locks)
                : !ResearchRules.Affordable(node, state) ? "insufficient-resources"
                : "started";
            done = reason == "started";
            if (done)
            {
                state = ResearchRules.Charge(node!, state);
                await Execute(connection, transaction, """
                    INSERT INTO research_jobs(account_id,seq,node_id,level,status,started_at,completes_at,content_hash,start_command)
                    VALUES($1,(SELECT coalesce(max(seq),0)+1 FROM research_jobs WHERE account_id=$1),$2,$3,'active',$4,$5,$6,$7)
                    """, cancellation, id, node!.Id, node.Level, now, now.AddSeconds(node.Seconds), content.Hash, command.CommandId);
            }
        }
        if (settled.Length != 0 || done && command.Type == "start-research")
        {
            var before = account.State.Wallet;
            account = await BumpAsync(connection, transaction, account, state, command.Type == "start-research" && done ? "research-started" : "research-completed", cancellation);
            foreach (var pair in state.Wallet)
            {
                int delta = pair.Value - before.GetValueOrDefault(pair.Key);
                if (delta != 0)
                {
                    await Execute(connection, transaction, "INSERT INTO resource_events VALUES($1,$2,$3,$4)", cancellation, id, account.Version, pair.Key, delta);
                }
            }
        }
        var view = await ViewAsync(connection, transaction, account, now, settled, cancellation);
        string response = JsonSerializer.Serialize(new ResearchReply(done, reason, view), AccountStore.Json);
        await Execute(connection, transaction, "INSERT INTO commands VALUES($1,$2,$3,$4)", cancellation, id, command.CommandId, payloadHash, response);
        await transaction.CommitAsync(cancellation);
        return response;
    }

    private static async Task<StoredAccount> BumpAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StoredAccount account, MetaState state, string notice, CancellationToken cancellation)
    {
        var updated = account with { Version = checked(account.Version + 1), State = state };
        await AccountStore.WriteAsync(connection, transaction, updated, false, cancellation);
        await Execute(connection, transaction, "INSERT INTO outbox VALUES($1,$2,$3)", cancellation, updated.Id, updated.Version, notice);
        return updated;
    }

    private static async Task<string[]> SettleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, DateTimeOffset now, CancellationToken cancellation)
    {
        var due = new List<(long Seq, string Node, int Level, DateTimeOffset At)>();
        await using (var query = new NpgsqlCommand("SELECT seq,node_id,level,completes_at FROM research_jobs WHERE account_id=$1 AND status='active' AND completes_at<=$2 ORDER BY completes_at,seq", connection, transaction))
        {
            query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(now);
            await using var reader = await query.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                due.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetFieldValue<DateTimeOffset>(3)));
            }
        }
        foreach (var job in due)
        {
            await Execute(connection, transaction, "UPDATE research_jobs SET status='completed',completed_at=$3 WHERE account_id=$1 AND seq=$2 AND status='active'", cancellation, id, job.Seq, job.At);
            await Execute(connection, transaction, "INSERT INTO research_levels VALUES($1,$2,$3,$4,$5)", cancellation, id, job.Node, job.Level, job.At, job.Seq);
        }
        return due.Select(j => j.Node).ToArray();
    }

    private async Task<ResearchView> ViewAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StoredAccount account, DateTimeOffset now, string[] settled, CancellationToken cancellation)
    {
        var levels = await LevelsAsync(connection, transaction, account.Id, cancellation);
        var proofs = await ProofsAsync(connection, transaction, account.Id, cancellation);
        var active = await ActiveAsync(connection, transaction, account.Id, cancellation);
        var nodes = content.Nodes.Select(n =>
        {
            var locks = ResearchRules.LockReasons(n, account.State, levels, proofs);
            string status = levels.GetValueOrDefault(n.Id) >= n.Level ? "completed" : active.Any(j => j.NodeId == n.Id) ? "active" : locks.Length == 0 ? "available" : "locked";
            return new ResearchNodeView(n.Id, n.Category, levels.GetValueOrDefault(n.Id), n.Level, status, locks, n.Cost, n.Seconds, n.Domain, n.Value);
        }).ToArray();
        var jobs = active.Select(j => j with { RemainingSeconds = Math.Max(0, (long)Math.Ceiling((j.CompletesAt - now).TotalSeconds)) }).ToArray();
        return new(account.Version, now, account.State.Wallet, nodes, jobs, ResearchRules.Effects(content, levels), settled);
    }

    private static async Task<List<ResearchJobView>> ActiveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("SELECT node_id,level,started_at,completes_at FROM research_jobs WHERE account_id=$1 AND status='active' ORDER BY seq", connection, transaction);
        query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        var list = new List<ResearchJobView>();
        while (await reader.ReadAsync(cancellation))
        {
            list.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetFieldValue<DateTimeOffset>(3), 0));
        }
        return list;
    }

    private static async Task<Dictionary<string, int>> LevelsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("SELECT node_id,level FROM research_levels WHERE account_id=$1", connection, transaction);
        query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellation))
        {
            map[reader.GetString(0)] = reader.GetInt32(1);
        }
        return map;
    }

    private static async Task<HashSet<string>> ProofsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("SELECT proof_id FROM research_proofs WHERE account_id=$1", connection, transaction);
        query.Parameters.AddWithValue(id);
        await using var reader = await query.ExecuteReaderAsync(cancellation);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellation))
        {
            set.Add(reader.GetString(0));
        }
        return set;
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
