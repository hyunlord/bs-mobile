using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace SowSiege.Server.Infrastructure;

/// <summary>Abuse limits (non-secret server configuration, appsettings.json "Auth:Limits"). Exceeding one returns 429.</summary>
public sealed record AuthLimits(int GuestPerIpPerHour, int GuestPerKeyPerDay, int ChallengePerIpPerMinute, int ChallengePerAccountPerMinute);
/// <summary>GuestCreationEnabled is true only for Development with Prototype:Enabled; release settings keep guest creation closed.</summary>
/// <summary>
/// PseudonymKey (server secret, never in the repository) keys the hash that replaces a purged account's ID in retained records.
/// DeletionGraceDays is the ADR0080 §5 window in which the same device key can still sign in and restore a deleted account.
/// </summary>
public sealed record AuthOptions(TimeSpan AccessLifetime, TimeSpan RefreshLifetime, TimeSpan ChallengeLifetime, AuthLimits Limits, bool GuestCreationEnabled,
    byte[]? PseudonymKey = null, int DeletionGraceDays = 30)
{
    public TimeSpan DeletionGrace => TimeSpan.FromDays(DeletionGraceDays);

    public static bool GuestCreationOpen(string environment, string? prototypeEnabled) => environment == "Development" && prototypeEnabled == "true";
}
/// <summary>Restored is true when this sign-in cancelled a pending deletion (ADR0080 §5).</summary>
public sealed record AuthTokens(Guid AccountId, Guid KeyId, string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt,
    bool Restored = false);
public sealed record AuthChallenge(Guid KeyId, string Nonce, DateTimeOffset ExpiresAt);
public sealed class AuthException(string code) : Exception(code);

/// <summary>
/// ADR0080 option A, stage 1: guest accounts bound to a device key, rotating refresh tokens and in-app account deletion.
/// The device keeps its P-256 private key; the server stores only the public key and verifies a signature over a one-time nonce.
/// Access tokens are rows in the existing sessions table (hash only), so AccountStore.AuthenticateAsync is unchanged.
/// Refresh tokens rotate on every use; presenting a used one revokes its whole family. Apple/Google linking is stage 2.
/// </summary>
public sealed partial class AuthStore(NpgsqlDataSource dataSource, AccountStore accounts, AuthOptions options, TimeProvider time)
{
    internal const string Sql = """
        CREATE TABLE IF NOT EXISTS auth_schema(version integer PRIMARY KEY);
        INSERT INTO auth_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS auth_device_keys(
          key_id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), public_key bytea NOT NULL,
          created_at timestamptz NOT NULL, revoked_at timestamptz);
        CREATE TABLE IF NOT EXISTS auth_challenges(
          nonce_hash text PRIMARY KEY, key_id uuid NOT NULL REFERENCES auth_device_keys(key_id), expires_at timestamptz NOT NULL, used boolean NOT NULL);
        CREATE TABLE IF NOT EXISTS auth_refresh_tokens(
          token_hash text PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), key_id uuid NOT NULL REFERENCES auth_device_keys(key_id),
          family uuid NOT NULL, expires_at timestamptz NOT NULL, used_at timestamptz, revoked_at timestamptz);
        CREATE TABLE IF NOT EXISTS auth_deleted_accounts(account_id uuid PRIMARY KEY REFERENCES accounts(id), deleted_at timestamptz NOT NULL);
        CREATE TABLE IF NOT EXISTS auth_rate(kind text NOT NULL, subject text NOT NULL, window_start bigint NOT NULL, count integer NOT NULL,
          PRIMARY KEY(kind,subject,window_start));
        CREATE INDEX IF NOT EXISTS auth_challenges_expires ON auth_challenges(expires_at);
        CREATE INDEX IF NOT EXISTS auth_refresh_expires ON auth_refresh_tokens(expires_at);
        """;

    public async Task InitializeAsync(CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await Run(connection, transaction, "SELECT pg_advisory_xact_lock(80080)", cancellation);
        await Run(connection, transaction, Sql, cancellation);
        await using var version = new NpgsqlCommand("SELECT max(version) FROM auth_schema", connection, transaction);
        if (await version.ExecuteScalarAsync(cancellation) is not int current || current is not (1 or 2))
        {
            throw new InvalidDataException("Unsupported auth schema version.");
        }
        if (current == 1) { await Run(connection, transaction, SocialSql, cancellation); }
        await transaction.CommitAsync(cancellation);
    }

    /// <summary>Creates a guest account for a device public key (SubjectPublicKeyInfo, ECDSA P-256).</summary>
    public async Task<AuthTokens> CreateGuestAsync(byte[] publicKey, string clientIp, CancellationToken cancellation)
    {
        if (!options.GuestCreationEnabled)
        {
            throw new AuthException("guest-disabled");
        }
        ValidatePublicKey(publicKey);
        await using (var limits = await dataSource.OpenConnectionAsync(cancellation))
        {
            await using var limitTransaction = await limits.BeginTransactionAsync(cancellation);
            await LimitAsync(limits, limitTransaction, cancellation, ("guest-ip", clientIp, 3_600_000L, options.Limits.GuestPerIpPerHour),
                ("guest-key", Convert.ToHexString(SHA256.HashData(publicKey)), 86_400_000L, options.Limits.GuestPerKeyPerDay));
            await PruneAsync(limits, limitTransaction, cancellation);
            await limitTransaction.CommitAsync(cancellation);
        }
        var created = await accounts.CreateAsync(cancellation);
        Guid account = (await accounts.AuthenticateAsync(created.Token, cancellation)) ?? throw new AuthException("account-create-failed");
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        // The prototype creation session is replaced by the short access token below.
        await Run(connection, transaction, "DELETE FROM sessions WHERE token_hash=$1", cancellation, Hash(created.Token));
        var keyId = Guid.NewGuid();
        await Run(connection, transaction, "INSERT INTO auth_device_keys VALUES($1,$2,$3,$4,NULL)", cancellation, keyId, account, publicKey, time.GetUtcNow());
        var tokens = await IssueAsync(connection, transaction, account, keyId, Guid.NewGuid(), cancellation);
        await transaction.CommitAsync(cancellation);
        return tokens;
    }

    public async Task<AuthChallenge> ChallengeAsync(Guid keyId, string clientIp, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var (owner, _, _) = await KeyAsync(connection, transaction, keyId, cancellation, allowPendingDeletion: true);
        await LimitAsync(connection, transaction, cancellation, ("challenge-ip", clientIp, 60_000L, options.Limits.ChallengePerIpPerMinute),
            ("challenge-account", owner.ToString("D"), 60_000L, options.Limits.ChallengePerAccountPerMinute));
        await PruneAsync(connection, transaction, cancellation);
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = time.GetUtcNow().Add(options.ChallengeLifetime);
        await Run(connection, transaction, "INSERT INTO auth_challenges VALUES($1,$2,$3,false)", cancellation, Hash(nonce), keyId, expires);
        await transaction.CommitAsync(cancellation);
        return new(keyId, nonce, expires);
    }

    /// <summary>
    /// Fixed-window counters. A rejected request rolls back its increment, so the counter stays at the limit and keeps refusing
    /// until the window ends.
    /// </summary>
    private async Task LimitAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellation,
        params (string Kind, string Subject, long WindowMs, int Limit)[] limits)
    {
        long now = time.GetUtcNow().ToUnixTimeMilliseconds();
        foreach (var (kind, subject, window, limit) in limits)
        {
            await using var count = new NpgsqlCommand("""
                INSERT INTO auth_rate VALUES($1,$2,$3,1) ON CONFLICT(kind,subject,window_start) DO UPDATE SET count=auth_rate.count+1 RETURNING count
                """, connection, transaction);
            count.Parameters.AddWithValue(kind); count.Parameters.AddWithValue(subject); count.Parameters.AddWithValue(now - now % window);
            if (await count.ExecuteScalarAsync(cancellation) is int used && used > limit)
            {
                throw new AuthException("rate-limited");
            }
        }
    }

    /// <summary>
    /// Cleanup on issue: expired or used challenges, refresh tokens past expiry (reuse detection needs them only until then) and old
    /// rate windows, in bounded batches so one request never does unbounded work.
    /// </summary>
    internal async Task PruneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellation, Guid? refreshAccount = null)
    {
        var now = time.GetUtcNow();
        await Run(connection, transaction, "DELETE FROM auth_challenges WHERE ctid IN (SELECT ctid FROM auth_challenges WHERE used OR expires_at<=$1 LIMIT 500)", cancellation, now);
        // A refresh already owns its account lock; do not reach another account's credential rows from that transaction.
        await Run(connection, transaction, "DELETE FROM auth_refresh_tokens WHERE ctid IN (SELECT ctid FROM auth_refresh_tokens WHERE expires_at<=$1 AND ($2::uuid IS NULL OR account_id=$2) LIMIT 500)",
            cancellation, now, refreshAccount is { } owner ? owner : DBNull.Value);
        await Run(connection, transaction, "DELETE FROM auth_rate WHERE ctid IN (SELECT ctid FROM auth_rate WHERE window_start<$1 LIMIT 500)", cancellation,
            now.ToUnixTimeMilliseconds() - 86_400_000L);
    }

    /// <summary>Signs in with the device key: the signature (IEEE P1363, SHA-256) covers "sow-auth-v1\n{keyId}\n{nonce}".</summary>
    public async Task<AuthTokens> SignInAsync(Guid keyId, string nonce, byte[] signature, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        Guid discovered;
        await using (var discover = new NpgsqlCommand("SELECT account_id FROM auth_device_keys WHERE key_id=$1", connection, transaction))
        {
            discover.Parameters.AddWithValue(keyId);
            discovered = await discover.ExecuteScalarAsync(cancellation) is Guid owner ? owner : throw new AuthException("key-invalid");
        }
        await LockCredentialAccountAsync(connection, transaction, discovered, "key-invalid", cancellation);
        var (account, publicKey, pendingDeletion) = await KeyAsync(connection, transaction, keyId, cancellation, allowPendingDeletion: true);
        if (account != discovered) { throw new AuthException("key-invalid"); }
        // The nonce is consumed in the same statement that checks it, so a replayed signature finds nothing.
        int consumed = await Run(connection, transaction,
            "UPDATE auth_challenges SET used=true WHERE nonce_hash=$1 AND key_id=$2 AND used=false AND expires_at>$3", cancellation, Hash(nonce), keyId, time.GetUtcNow());
        if (consumed != 1)
        {
            throw new AuthException("challenge-invalid");
        }
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out _);
        if (!key.VerifyData(SignedBytes(keyId, nonce), signature, HashAlgorithmName.SHA256))
        {
            await transaction.CommitAsync(cancellation); // keep the nonce consumed
            throw new AuthException("signature-invalid");
        }
        if (pendingDeletion)
        {
            // Signing in with the same device key during the grace period cancels the deletion. The account row lock orders this
            // against PurgeExpiredAsync, which re-checks the deletion mark under the same lock.
            if (await Run(connection, transaction, "DELETE FROM auth_deleted_accounts WHERE account_id=$1", cancellation, account) != 1)
            {
                throw new AuthException("key-invalid");
            }
        }
        var tokens = await IssueAsync(connection, transaction, account, keyId, Guid.NewGuid(), cancellation);
        await transaction.CommitAsync(cancellation);
        return tokens with { Restored = pendingDeletion };
    }

    public static byte[] SignedBytes(Guid keyId, string nonce) => Encoding.UTF8.GetBytes("sow-auth-v1\n" + keyId.ToString("D") + "\n" + nonce);

    /// <summary>Rotates a refresh token. Reusing an already used token revokes its whole family (theft signal).</summary>
    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        Guid discovered;
        await using (var discover = new NpgsqlCommand("SELECT account_id FROM auth_refresh_tokens WHERE token_hash=$1", connection, transaction))
        {
            discover.Parameters.AddWithValue(Hash(refreshToken));
            discovered = await discover.ExecuteScalarAsync(cancellation) is Guid owner ? owner : throw new AuthException("refresh-invalid");
        }
        await LockCredentialAccountAsync(connection, transaction, discovered, "refresh-invalid", cancellation);
        await using var read = new NpgsqlCommand("SELECT account_id,key_id,family,expires_at,used_at,revoked_at FROM auth_refresh_tokens WHERE token_hash=$1 FOR UPDATE", connection, transaction);
        read.Parameters.AddWithValue(Hash(refreshToken));
        Guid account, keyId, family; DateTimeOffset expires; bool used, revoked;
        await using (var reader = await read.ExecuteReaderAsync(cancellation))
        {
            if (!await reader.ReadAsync(cancellation))
            {
                throw new AuthException("refresh-invalid");
            }
            (account, keyId, family, expires, used, revoked) = (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetFieldValue<DateTimeOffset>(3), !reader.IsDBNull(4), !reader.IsDBNull(5));
        }
        if (account != discovered) { throw new AuthException("refresh-invalid"); }
        var now = time.GetUtcNow();
        if (used || revoked)
        {
            await Run(connection, transaction, "UPDATE auth_refresh_tokens SET revoked_at=$2 WHERE family=$1 AND revoked_at IS NULL", cancellation, family, now);
            await transaction.CommitAsync(cancellation);
            throw new AuthException("refresh-reused");
        }
        if (expires <= now)
        {
            throw new AuthException("refresh-expired");
        }
        await KeyAsync(connection, transaction, keyId, cancellation);
        await Run(connection, transaction, "UPDATE auth_refresh_tokens SET used_at=$2 WHERE token_hash=$1", cancellation, Hash(refreshToken), now);
        await PruneAsync(connection, transaction, cancellation, account);
        var tokens = await IssueAsync(connection, transaction, account, keyId, family, cancellation);
        await transaction.CommitAsync(cancellation);
        return tokens;
    }

    /// <summary>
    /// In-app account deletion (Apple 5.1.1(v), Google Play), ADR0080 §5: revokes every session and refresh token and marks the
    /// account for deletion. The device key stays so the owner can restore the account by signing in within the grace period;
    /// PurgeExpiredAsync removes it afterwards. Alliance membership and pending applications are removed at once when that module is
    /// installed; a deleted leader's alliance passes to the longest-standing remaining member (GDD19 §3 [가설]). Returns the purge time.
    /// </summary>
    public async Task<DateTimeOffset> DeleteAccountAsync(Guid account, CancellationToken cancellation)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var deleted = await DeleteAccountOnceAsync(account, cancellation);
            if (deleted is { } purgeAt) { return purgeAt; }
        }
        throw new AuthException("state-changed");
    }

    private async Task<DateTimeOffset?> DeleteAccountOnceAsync(Guid account, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var now = time.GetUtcNow();
        bool alliances;
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('alliance_members') IS NOT NULL", connection, transaction))
        {
            alliances = await exists.ExecuteScalarAsync(cancellation) is true;
        }
        Guid? alliance = null;
        if (alliances)
        {
            // A changed membership retries the whole transaction, never acquiring a new alliance lock after an account lock.
            Guid? seen = await AllianceOfAsync(connection, transaction, account, cancellation);
            if (seen is { } id)
            {
                await Run(connection, transaction, "SELECT 1 FROM alliances WHERE id=$1 FOR UPDATE", cancellation, id);
            }
            bool found = false;
            await using (var locked = new NpgsqlCommand("""
                SELECT id FROM accounts WHERE id=$1 OR id IN (SELECT account_id FROM alliance_members WHERE alliance_id=$2)
                ORDER BY id FOR UPDATE
                """, connection, transaction))
            {
                locked.Parameters.AddWithValue(account);
                locked.Parameters.AddWithValue(seen is { } discoveredAlliance ? discoveredAlliance : DBNull.Value);
                await using var reader = await locked.ExecuteReaderAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    found |= reader.GetGuid(0) == account;
                }
            }
            if (!found) { throw new AuthException("key-invalid"); }
            alliance = await AllianceOfAsync(connection, transaction, account, cancellation);
            if (alliance != seen)
            {
                await transaction.RollbackAsync(cancellation);
                return null;
            }
        }
        else
        {
            await LockCredentialAccountAsync(connection, transaction, account, "key-invalid", cancellation);
        }
        await Run(connection, transaction, "INSERT INTO auth_deleted_accounts VALUES($1,$2) ON CONFLICT DO NOTHING", cancellation, account, now);
        await Run(connection, transaction, "DELETE FROM sessions WHERE account_id=$1", cancellation, account);
        await Run(connection, transaction, "UPDATE auth_refresh_tokens SET revoked_at=$2 WHERE account_id=$1 AND revoked_at IS NULL", cancellation, account, now);
        await QueueAppleRevocationsAsync(connection, transaction, account, cancellation);
        DateTimeOffset deletedAt;
        await using (var mark = new NpgsqlCommand("SELECT deleted_at FROM auth_deleted_accounts WHERE account_id=$1", connection, transaction))
        {
            mark.Parameters.AddWithValue(account);
            await using var reader = await mark.ExecuteReaderAsync(cancellation);
            await reader.ReadAsync(cancellation);
            deletedAt = reader.GetFieldValue<DateTimeOffset>(0);
        }
        if (alliances)
        {
            await Run(connection, transaction, "DELETE FROM alliance_applications WHERE account_id=$1", cancellation, account);
        }
        if (alliance is { } joined)
        {
            await Run(connection, transaction, "DELETE FROM alliance_members WHERE account_id=$1", cancellation, account);
            await Run(connection, transaction, """
                UPDATE alliances SET member_count=member_count-1, status=CASE WHEN member_count-1=0 THEN 'dissolved' ELSE status END WHERE id=$1
                """, cancellation, joined);
            // GDD19 §3 [가설]: until delegation exists, a deleted leader passes the alliance to the longest-standing remaining member.
            await Run(connection, transaction, """
                WITH heir AS (
                  SELECT account_id FROM alliance_members WHERE alliance_id=$1 AND EXISTS (SELECT 1 FROM alliances WHERE id=$1 AND leader=$2)
                  ORDER BY joined_ms, account_id LIMIT 1)
                , promoted AS (
                  UPDATE alliance_members SET role='leader' WHERE account_id=(SELECT account_id FROM heir) RETURNING account_id)
                UPDATE alliances SET leader=(SELECT account_id FROM promoted) WHERE id=$1 AND EXISTS (SELECT 1 FROM promoted)
                """, cancellation, joined, account);
        }
        await transaction.CommitAsync(cancellation);
        return deletedAt.Add(options.DeletionGrace);
    }

    private static async Task LockCredentialAccountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, string error, CancellationToken cancellation)
    {
        await using var locked = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction);
        locked.Parameters.AddWithValue(account);
        if (await locked.ExecuteScalarAsync(cancellation) is null) { throw new AuthException(error); }
    }

    /// <summary>
    /// Periodic job (ADR0080 §5): permanently deletes accounts whose grace period has ended, at most <paramref name="batch"/> per call,
    /// one transaction each. Every row keyed by the account is deleted, except records kept for their own retention period, whose
    /// account IDs are replaced by <see cref="Pseudonym"/>: alliance chat authors (the body follows the 200-message rule) and report
    /// reporters/authors (180 days). The purchase ledger (5 years) joins the retained list when payments exist.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(int batch, CancellationToken cancellation)
    {
        if (options.PseudonymKey is not { Length: >= 32 })
        {
            throw new InvalidOperationException("Auth:PseudonymKey (>=32 bytes hex) required.");
        }
        var cutoff = time.GetUtcNow().Subtract(options.DeletionGrace);
        var due = new List<Guid>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellation))
        {
            await using var query = new NpgsqlCommand("SELECT account_id FROM auth_deleted_accounts WHERE deleted_at<=$1 ORDER BY deleted_at,account_id LIMIT $2", connection);
            query.Parameters.AddWithValue(cutoff); query.Parameters.AddWithValue(batch);
            await using var reader = await query.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation)) { due.Add(reader.GetGuid(0)); }
        }
        int purged = 0;
        foreach (Guid account in due)
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellation);
            await using var transaction = await connection.BeginTransactionAsync(cancellation);
            await using (var locked = new NpgsqlCommand("""
                SELECT 1 FROM accounts a JOIN auth_deleted_accounts d ON d.account_id=a.id WHERE a.id=$1 AND d.deleted_at<=$2 FOR UPDATE OF a SKIP LOCKED
                """, connection, transaction))
            {
                locked.Parameters.AddWithValue(account); locked.Parameters.AddWithValue(cutoff);
                if (await locked.ExecuteScalarAsync(cancellation) is null)
                {
                    continue; // restored meanwhile, or another worker holds it
                }
            }
            string pseudonym = Pseudonym(account);
            await PurgeSocialAsync(connection, transaction, account, cancellation);
            if (await TableExistsAsync(connection, transaction, "alliance_members", cancellation))
            {
                await Run(connection, transaction, "UPDATE alliance_messages SET author=NULL, author_ref=$2 WHERE author=$1", cancellation, account, pseudonym);
                await Run(connection, transaction, "UPDATE alliance_reports SET reporter=NULL, reporter_ref=$2 WHERE reporter=$1", cancellation, account, pseudonym);
                await Run(connection, transaction, "UPDATE alliance_reports SET author=NULL, author_ref=$2 WHERE author=$1", cancellation, account, pseudonym);
                await Run(connection, transaction, "UPDATE alliances SET leader=NULL WHERE leader=$1", cancellation, account);
                await Run(connection, transaction, "DELETE FROM alliance_help_grants WHERE giver=$1 OR requester=$1", cancellation, account);
                await Run(connection, transaction, "DELETE FROM alliance_help_requests WHERE requester=$1", cancellation, account);
                await Run(connection, transaction, "DELETE FROM alliance_blocks WHERE account_id=$1 OR blocked=$1", cancellation, account);
                foreach (string table in new[] { "alliance_restrictions", "alliance_applications", "alliance_members" })
                {
                    await Run(connection, transaction, $"DELETE FROM {table} WHERE account_id=$1", cancellation, account);
                }
            }
            // Other accounts' cached command replies (idempotency) can name this account, e.g. an alliance member list.
            await Run(connection, transaction, "UPDATE commands SET response=replace(response,$1,$2) WHERE account_id<>$3 AND strpos(response,$1)>0",
                cancellation, account.ToString("D"), pseudonym, account);
            await Run(connection, transaction, "DELETE FROM auth_challenges WHERE key_id IN (SELECT key_id FROM auth_device_keys WHERE account_id=$1)", cancellation, account);
            await Run(connection, transaction, "DELETE FROM auth_rate WHERE subject=$1", cancellation, account.ToString("D"));
            foreach (string table in new[] { "auth_refresh_tokens", "auth_device_keys", "sessions", "commands", "resource_events", "outbox", "domain_events",
                "account_domains", "account_job_clocks", "account_held_materials", "auth_deleted_accounts" })
            {
                if (await TableExistsAsync(connection, transaction, table, cancellation))
                {
                    await Run(connection, transaction, $"DELETE FROM {table} WHERE account_id=$1", cancellation, account);
                }
            }
            await Run(connection, transaction, "DELETE FROM accounts WHERE id=$1", cancellation, account);
            await transaction.CommitAsync(cancellation);
            purged++;
        }
        return purged;
    }

    /// <summary>Keyed hash (HMAC-SHA256, server secret) of an account ID: links retained records without storing the ID.</summary>
    public string Pseudonym(Guid account) =>
        "p1:" + Convert.ToHexString(HMACSHA256.HashData(options.PseudonymKey ?? throw new InvalidOperationException("Auth:PseudonymKey required."),
            Encoding.UTF8.GetBytes(account.ToString("D")))).ToLowerInvariant();

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table, CancellationToken cancellation)
    {
        await using var exists = new NpgsqlCommand("SELECT to_regclass($1) IS NOT NULL", connection, transaction);
        exists.Parameters.AddWithValue(table);
        return await exists.ExecuteScalarAsync(cancellation) is true;
    }

    private static async Task<Guid?> AllianceOfAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, CancellationToken cancellation)
    {
        await using var query = new NpgsqlCommand("SELECT alliance_id FROM alliance_members WHERE account_id=$1", connection, transaction);
        query.Parameters.AddWithValue(account);
        return await query.ExecuteScalarAsync(cancellation) is Guid id ? id : null;
    }

    private async Task<AuthTokens> IssueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, Guid keyId, Guid family, CancellationToken cancellation)
    {
        var now = time.GetUtcNow();
        string access = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string refresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var accessExpires = now.Add(options.AccessLifetime);
        var refreshExpires = now.Add(options.RefreshLifetime);
        await Run(connection, transaction, "INSERT INTO sessions(token_hash,account_id,expires_at) VALUES($1,$2,$3)", cancellation, Hash(access), account, accessExpires);
        await Run(connection, transaction, "INSERT INTO auth_refresh_tokens VALUES($1,$2,$3,$4,$5,NULL,NULL)", cancellation, Hash(refresh), account, keyId, family, refreshExpires);
        return new(account, keyId, access, accessExpires, refresh, refreshExpires);
    }

    /// <summary>An account marked for deletion is accepted only when <paramref name="allowPendingDeletion"/> and still inside the grace period.</summary>
    private async Task<(Guid Account, byte[] PublicKey, bool PendingDeletion)> KeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid keyId,
        CancellationToken cancellation, bool allowPendingDeletion = false)
    {
        await using var read = new NpgsqlCommand("""
            SELECT k.account_id,k.public_key,d.deleted_at FROM auth_device_keys k LEFT JOIN auth_deleted_accounts d ON d.account_id=k.account_id
            WHERE k.key_id=$1 AND k.revoked_at IS NULL
            """, connection, transaction);
        read.Parameters.AddWithValue(keyId);
        await using var reader = await read.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation))
        {
            throw new AuthException("key-invalid");
        }
        bool pending = !reader.IsDBNull(2);
        if (pending && (!allowPendingDeletion || reader.GetFieldValue<DateTimeOffset>(2).Add(options.DeletionGrace) <= time.GetUtcNow()))
        {
            throw new AuthException("key-invalid");
        }
        return (reader.GetGuid(0), (byte[])reader[1], pending);
    }

    private static void ValidatePublicKey(byte[] publicKey)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out int read);
            if (read != publicKey.Length || key.KeySize != 256)
            {
                throw new AuthException("key-unsupported");
            }
        }
        catch (CryptographicException)
        {
            throw new AuthException("key-unsupported");
        }
    }

    private static async Task<int> Run(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, c, t);
        foreach (object value in values) { command.Parameters.AddWithValue(value); }
        return await command.ExecuteNonQueryAsync(ct);
    }

    // Same token hashing as AccountStore sessions.
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
