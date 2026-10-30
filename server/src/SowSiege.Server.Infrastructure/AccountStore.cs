using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;

namespace SowSiege.Server.Infrastructure;

public sealed partial class AccountStore(NpgsqlDataSource dataSource, EstateApplication application, TimeProvider time, DomainRegistry? domains = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var schemaLock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(575)", connection, transaction);
        await schemaLock.ExecuteNonQueryAsync(cancellation);
        await using var bootstrap = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS server_schema(version integer PRIMARY KEY)", connection, transaction);
        await bootstrap.ExecuteNonQueryAsync(cancellation);
        await using var existing = new NpgsqlCommand("SELECT max(version) FROM server_schema", connection, transaction);
        object? oldVersion = await existing.ExecuteScalarAsync(cancellation);
        if (oldVersion is int old && old is not 1 and not 2)
        {
            throw new InvalidDataException("Unsupported server schema version.");
        }
        await using var command = new NpgsqlCommand(Schema.Sql + (oldVersion is int installed && installed == 2 ? "" : Schema.ExtensionsSql), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellation);
        await using var version = new NpgsqlCommand("SELECT max(version) FROM server_schema", connection, transaction);
        if (await version.ExecuteScalarAsync(cancellation) is not int current || current != 2)
        {
            throw new InvalidDataException("Unsupported server schema version.");
        }
        await transaction.CommitAsync(cancellation);
    }

    public async Task<AccountCreated> CreateAsync(CancellationToken cancellation)
    {
        var now = time.GetUtcNow();
        var account = application.Create(now.ToUnixTimeMilliseconds());
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiry = now.AddDays(1);
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await WriteAsync(connection, transaction, account, true, cancellation);
        await using var session = new NpgsqlCommand("INSERT INTO sessions(token_hash,account_id,expires_at) VALUES($1,$2,$3)", connection, transaction);
        session.Parameters.AddWithValue(Hash(token)); session.Parameters.AddWithValue(account.Id); session.Parameters.AddWithValue(expiry);
        await session.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return new(token, expiry, EstateApplication.View(account));
    }

    public async Task<Guid?> AuthenticateAsync(string token, CancellationToken cancellation)
    {
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)))
        {
            return null;
        }
        await using var command = dataSource.CreateCommand("SELECT account_id FROM sessions WHERE token_hash=$1 AND expires_at>$2");
        command.Parameters.AddWithValue(Hash(token)); command.Parameters.AddWithValue(time.GetUtcNow());
        return await command.ExecuteScalarAsync(cancellation) is Guid id ? id : null;
    }

    public Task<string> ExecuteAsync(Guid id, EstateCommand? command, CancellationToken cancellation) => ExecuteTransactionAsync(id, command, null, false, cancellation);

    public Task<string> ExecuteAccountAsync(Guid id, AccountCommand? command, CancellationToken cancellation) => ExecuteTransactionAsync(id, null, command, true, cancellation);

    private async Task<string> ExecuteTransactionAsync(Guid id, EstateCommand? command, AccountCommand? domainCommand, bool accountResponse, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var account = await ReadAsync(connection, transaction, id, cancellation);
        Guid? commandId = domainCommand?.CommandId ?? command?.CommandId;
        string payloadHash = domainCommand is not null ? Hash("account-v1:" + JsonSerializer.Serialize(domainCommand, Json))
            : command is null ? "" : Hash(JsonSerializer.Serialize(command, Json));
        if (commandId is not null)
        {
            await using var receipt = new NpgsqlCommand("SELECT payload_hash,response FROM commands WHERE account_id=$1 AND command_id=$2", connection, transaction);
            receipt.Parameters.AddWithValue(id); receipt.Parameters.AddWithValue(commandId.Value);
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
        if (domainCommand is not null && domainCommand.ExpectedVersion != account.Version)
        {
            throw new CommandConflictException("version-conflict");
        }
        long now = time.GetUtcNow().ToUnixTimeMilliseconds();
        bool hasDomains = domains?.Handlers.Count > 0;
        var applied = application.Apply(account, hasDomains ? null : command, now);
        var advanced = await ApplyDomainsAsync(connection, transaction, applied.Account, domainCommand, hasDomains ? command : null, now, cancellation);
        var estateReply = applied.Reply with { Done = hasDomains && command is not null ? advanced.Done : applied.Reply.Done, Reason = hasDomains && command is not null ? advanced.Reason : applied.Reply.Reason, Estate = EstateApplication.View(advanced.Account, applied.Reply.Estate.CreditedSeconds, applied.Reply.Estate.Clock) };
        applied = new(advanced.Account, estateReply);
        await WriteAsync(connection, transaction, applied.Account, false, cancellation);
        string response = accountResponse
            ? JsonSerializer.Serialize(new AccountReply(advanced.Done, advanced.Reason, applied.Reply.Estate, advanced.Views, applied.Account.State.Wallet.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new ResourceAmount(p.Key, p.Value)).Concat(advanced.Resources.Select(r => new ResourceAmount(r.Id, r.Amount))).ToArray()), Json)
            : JsonSerializer.Serialize(applied.Reply, Json);
        if (commandId is not null)
        {
            await using var receipt = new NpgsqlCommand("INSERT INTO commands VALUES($1,$2,$3,$4)", connection, transaction);
            receipt.Parameters.AddWithValue(id); receipt.Parameters.AddWithValue(commandId.Value);
            receipt.Parameters.AddWithValue(payloadHash); receipt.Parameters.AddWithValue(response);
            await receipt.ExecuteNonQueryAsync(cancellation);
        }
        foreach (var pair in applied.Account.State.Wallet)
        {
            int delta = pair.Value - account.State.Wallet.GetValueOrDefault(pair.Key);
            if (delta == 0)
            {
                continue;
            }
            await using var ledger = new NpgsqlCommand("INSERT INTO resource_events VALUES($1,$2,$3,$4)", connection, transaction);
            ledger.Parameters.AddWithValue(id); ledger.Parameters.AddWithValue(applied.Account.Version);
            ledger.Parameters.AddWithValue(pair.Key); ledger.Parameters.AddWithValue(delta);
            await ledger.ExecuteNonQueryAsync(cancellation);
        }
        await using var notice = new NpgsqlCommand("INSERT INTO outbox VALUES($1,$2,'estate-updated')", connection, transaction);
        notice.Parameters.AddWithValue(id); notice.Parameters.AddWithValue(applied.Account.Version);
        await notice.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return response;
    }

    private static async Task<StoredAccount> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, CancellationToken cancellation)
    {
        await using var command = new NpgsqlCommand("SELECT version,content_hash,payload,utc_highwater,logical_seconds,fraction_ms,codec_version,config_version FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation))
        {
            throw new InvalidDataException("Account missing.");
        }
        var decoded = MetaSaveCodec.Decode((byte[])reader[2]);
        if (!decoded.Valid || decoded.State?.Estate is null || reader.GetInt32(6) != MetaSaveCodec.EstateVersion || decoded.State.Estate.ConfigVersion != reader.GetString(7))
        {
            throw new InvalidDataException("Stored account payload invalid.");
        }
        return new(id, reader.GetInt64(0), reader.GetString(1), decoded.State, new(reader.GetInt64(3), reader.GetInt64(4), reader.GetInt32(5)));
    }

    private static async Task WriteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, StoredAccount account, bool insert, CancellationToken cancellation)
    {
        const string columns = "version=$2,content_hash=$3,config_version=$4,codec_version=$5,payload=$6,utc_highwater=$7,logical_seconds=$8,fraction_ms=$9";
        string sql = insert ? "INSERT INTO accounts(id,version,content_hash,config_version,codec_version,payload,utc_highwater,logical_seconds,fraction_ms) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9)" : "UPDATE accounts SET " + columns + " WHERE id=$1";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(account.Id); command.Parameters.AddWithValue(account.Version);
        command.Parameters.AddWithValue(account.ContentHash); command.Parameters.AddWithValue(account.State.Estate!.ConfigVersion);
        command.Parameters.AddWithValue(MetaSaveCodec.EstateVersion); command.Parameters.AddWithValue(MetaSaveCodec.Encode(account.State));
        command.Parameters.AddWithValue(account.Clock.HighWatermarkMilliseconds); command.Parameters.AddWithValue(account.Clock.LogicalSeconds);
        command.Parameters.AddWithValue(account.Clock.RemainderMilliseconds);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
