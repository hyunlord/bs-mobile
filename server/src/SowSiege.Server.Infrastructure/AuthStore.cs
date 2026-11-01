using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace SowSiege.Server.Infrastructure;

/// <summary>Abuse limits (non-secret server configuration, appsettings.json "Auth:Limits"). Exceeding one returns 429.</summary>
public sealed record AuthLimits(int GuestPerIpPerHour, int GuestPerKeyPerDay, int ChallengePerIpPerMinute, int ChallengePerAccountPerMinute);
/// <summary>GuestCreationEnabled is true only for Development with Prototype:Enabled; release settings keep guest creation closed.</summary>
public sealed record AuthOptions(TimeSpan AccessLifetime, TimeSpan RefreshLifetime, TimeSpan ChallengeLifetime, AuthLimits Limits, bool GuestCreationEnabled)
{
    public static bool GuestCreationOpen(string environment, string? prototypeEnabled) => environment == "Development" && prototypeEnabled == "true";
}
public sealed record AuthTokens(Guid AccountId, Guid KeyId, string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);
public sealed record AuthChallenge(Guid KeyId, string Nonce, DateTimeOffset ExpiresAt);
public sealed class AuthException(string code) : Exception(code);

/// <summary>
/// ADR0080 option A, stage 1: guest accounts bound to a device key, rotating refresh tokens and in-app account deletion.
/// The device keeps its P-256 private key; the server stores only the public key and verifies a signature over a one-time nonce.
/// Access tokens are rows in the existing sessions table (hash only), so AccountStore.AuthenticateAsync is unchanged.
/// Refresh tokens rotate on every use; presenting a used one revokes its whole family. Apple/Google linking is stage 2.
/// </summary>
public sealed class AuthStore(NpgsqlDataSource dataSource, AccountStore accounts, AuthOptions options, TimeProvider time)
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
        if (await version.ExecuteScalarAsync(cancellation) is not int current || current != 1)
        {
            throw new InvalidDataException("Unsupported auth schema version.");
        }
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
        var (owner, _) = await KeyAsync(connection, transaction, keyId, cancellation);
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
    internal async Task PruneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellation)
    {
        var now = time.GetUtcNow();
        await Run(connection, transaction, "DELETE FROM auth_challenges WHERE ctid IN (SELECT ctid FROM auth_challenges WHERE used OR expires_at<=$1 LIMIT 500)", cancellation, now);
        await Run(connection, transaction, "DELETE FROM auth_refresh_tokens WHERE ctid IN (SELECT ctid FROM auth_refresh_tokens WHERE expires_at<=$1 LIMIT 500)", cancellation, now);
        await Run(connection, transaction, "DELETE FROM auth_rate WHERE ctid IN (SELECT ctid FROM auth_rate WHERE window_start<$1 LIMIT 500)", cancellation,
            now.ToUnixTimeMilliseconds() - 86_400_000L);
    }

    /// <summary>Signs in with the device key: the signature (IEEE P1363, SHA-256) covers "sow-auth-v1\n{keyId}\n{nonce}".</summary>
    public async Task<AuthTokens> SignInAsync(Guid keyId, string nonce, byte[] signature, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var (account, publicKey) = await KeyAsync(connection, transaction, keyId, cancellation);
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
        var tokens = await IssueAsync(connection, transaction, account, keyId, Guid.NewGuid(), cancellation);
        await transaction.CommitAsync(cancellation);
        return tokens;
    }

    public static byte[] SignedBytes(Guid keyId, string nonce) => Encoding.UTF8.GetBytes("sow-auth-v1\n" + keyId.ToString("D") + "\n" + nonce);

    /// <summary>Rotates a refresh token. Reusing an already used token revokes its whole family (theft signal).</summary>
    public async Task<AuthTokens> RefreshAsync(string refreshToken, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var now = time.GetUtcNow();
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
        await PruneAsync(connection, transaction, cancellation);
        var tokens = await IssueAsync(connection, transaction, account, keyId, family, cancellation);
        await transaction.CommitAsync(cancellation);
        return tokens;
    }

    /// <summary>
    /// In-app account deletion (Apple 5.1.1(v), Google Play): revokes every session, refresh token and device key and marks the
    /// account deleted so no new credential can be issued for it. Alliance membership and pending applications are removed when that
    /// module is installed. Gameplay rows stay until the data purge policy (ADR0080 §5) is implemented.
    /// </summary>
    public async Task DeleteAccountAsync(Guid account, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        var now = time.GetUtcNow();
        await Run(connection, transaction, "SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", cancellation, account);
        await Run(connection, transaction, "INSERT INTO auth_deleted_accounts VALUES($1,$2) ON CONFLICT DO NOTHING", cancellation, account, now);
        await Run(connection, transaction, "DELETE FROM sessions WHERE account_id=$1", cancellation, account);
        await Run(connection, transaction, "UPDATE auth_refresh_tokens SET revoked_at=$2 WHERE account_id=$1 AND revoked_at IS NULL", cancellation, account, now);
        await Run(connection, transaction, "UPDATE auth_device_keys SET revoked_at=$2 WHERE account_id=$1 AND revoked_at IS NULL", cancellation, account, now);
        await using (var exists = new NpgsqlCommand("SELECT to_regclass('alliance_members') IS NOT NULL", connection, transaction))
        {
            if (await exists.ExecuteScalarAsync(cancellation) is true)
            {
                await Run(connection, transaction, "DELETE FROM alliance_applications WHERE account_id=$1", cancellation, account);
                await Run(connection, transaction, """
                    UPDATE alliances SET member_count=member_count-1, status=CASE WHEN member_count-1=0 THEN 'dissolved' ELSE status END
                    WHERE id=(SELECT alliance_id FROM alliance_members WHERE account_id=$1)
                    """, cancellation, account);
                await Run(connection, transaction, "DELETE FROM alliance_members WHERE account_id=$1", cancellation, account);
            }
        }
        await transaction.CommitAsync(cancellation);
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

    private static async Task<(Guid Account, byte[] PublicKey)> KeyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid keyId, CancellationToken cancellation)
    {
        await using var read = new NpgsqlCommand("""
            SELECT k.account_id,k.public_key FROM auth_device_keys k
            WHERE k.key_id=$1 AND k.revoked_at IS NULL AND NOT EXISTS (SELECT 1 FROM auth_deleted_accounts d WHERE d.account_id=k.account_id)
            """, connection, transaction);
        read.Parameters.AddWithValue(keyId);
        await using var reader = await read.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation))
        {
            throw new AuthException("key-invalid");
        }
        return (reader.GetGuid(0), (byte[])reader[1]);
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
