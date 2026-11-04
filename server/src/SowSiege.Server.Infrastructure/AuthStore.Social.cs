using System.Security.Cryptography;
using Npgsql;

namespace SowSiege.Server.Infrastructure;

public sealed partial class AuthStore
{
    internal const string SocialSql = """
        CREATE TABLE IF NOT EXISTS auth_social_challenges(
          id uuid PRIMARY KEY, nonce_hash text NOT NULL, provider text NOT NULL, client_id text NOT NULL, purpose text NOT NULL,
          account_id uuid REFERENCES accounts(id), public_key bytea, expires_at timestamptz NOT NULL, used boolean NOT NULL DEFAULT false);
        CREATE INDEX IF NOT EXISTS auth_social_challenges_expiry ON auth_social_challenges(expires_at);
        CREATE TABLE IF NOT EXISTS auth_social_identities(
          provider text NOT NULL, subject text NOT NULL, account_id uuid NOT NULL REFERENCES accounts(id),
          PRIMARY KEY(provider,subject));
        CREATE INDEX IF NOT EXISTS auth_social_identity_account ON auth_social_identities(account_id);
        CREATE TABLE IF NOT EXISTS auth_social_credentials(
          id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES accounts(id), subject_ref text NOT NULL,
          client_id text NOT NULL, credential bytea NOT NULL);
        CREATE INDEX IF NOT EXISTS auth_social_credential_account ON auth_social_credentials(account_id);
        CREATE TABLE IF NOT EXISTS auth_apple_revocations(
          id uuid PRIMARY KEY, subject_ref text NOT NULL, client_id text NOT NULL, credential bytea NOT NULL,
          retry_at timestamptz NOT NULL, attempts integer NOT NULL DEFAULT 0);
        CREATE INDEX IF NOT EXISTS auth_apple_revocations_due ON auth_apple_revocations(retry_at);
        CREATE INDEX IF NOT EXISTS auth_apple_revocations_subject ON auth_apple_revocations(subject_ref);
        INSERT INTO auth_schema VALUES (2) ON CONFLICT DO NOTHING;
        """;

    internal sealed record SocialChallengeState(Guid Id, string Provider, string ClientId, string Purpose, Guid? Account, byte[]? PublicKey, DateTimeOffset ExpiresAt);
    internal sealed record AppleCredential(Guid Id, string SubjectRef, string ClientId, byte[] Encrypted);

    internal async Task<SocialChallenge> CreateSocialChallengeAsync(string provider, string clientId, string purpose, string? accessToken,
        byte[]? publicKey, string clientIp, CancellationToken cancellation)
    {
        if (purpose is not ("link" or "login" or "delete")) { throw new AuthException("social-invalid"); }
        if (purpose == "login")
        {
            if (publicKey is null || publicKey.Length > 512) { throw new AuthException("key-unsupported"); }
            ValidatePublicKey(publicKey);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) { throw new AuthException("key-unsupported"); }
        }
        else if (publicKey is not null) { throw new AuthException("social-invalid"); }
        Guid? account = purpose == "link" ? await accounts.AuthenticateAsync(accessToken ?? "", cancellation) : null;
        if (purpose == "link" && account is null) { throw new AuthException("social-invalid"); }
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        // Rate counters are committed independently of account locks, matching the existing guest creation path.
        await LimitAsync(connection, transaction, cancellation,
            ("social-ip", clientIp, 60_000L, options.Limits.ChallengePerIpPerMinute));
        if (account is { } owner)
        {
            await LimitAsync(connection, transaction, cancellation,
                ("social-account", owner.ToString("D"), 60_000L, options.Limits.ChallengePerAccountPerMinute));
        }
        await Run(connection, transaction, "DELETE FROM auth_social_challenges WHERE id IN (SELECT id FROM auth_social_challenges WHERE used OR expires_at<=$1 LIMIT 500)", cancellation, time.GetUtcNow());
        await transaction.CommitAsync(cancellation);

        await using var insert = await connection.BeginTransactionAsync(cancellation);
        if (account is { } bound)
        {
            await SocialAccountLockAsync(connection, insert, bound, cancellation);
            await RequireSocialSessionAsync(connection, insert, bound, accessToken!, cancellation);
        }
        var id = Guid.NewGuid();
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = time.GetUtcNow().Add(options.ChallengeLifetime);
        await Run(connection, insert, "INSERT INTO auth_social_challenges(id,nonce_hash,provider,client_id,purpose,account_id,public_key,expires_at) VALUES($1,$2,$3,$4,$5,$6,$7,$8)", cancellation,
            id, Hash(nonce), provider, clientId, purpose, account is { } value ? value : DBNull.Value, publicKey is null ? DBNull.Value : publicKey, expires);
        await insert.CommitAsync(cancellation);
        return new(id, nonce, expires);
    }

    internal async Task<SocialChallengeState> ConsumeSocialChallengeAsync(Guid id, string nonce, string purpose, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(nonce) || nonce.Length != 64 || purpose is not ("link" or "login" or "delete")) { throw new AuthException("social-invalid"); }
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await using var query = new NpgsqlCommand("""
            UPDATE auth_social_challenges SET used=true WHERE id=$1 AND nonce_hash=$2 AND purpose=$3 AND NOT used AND expires_at>$4
            RETURNING provider,client_id,purpose,account_id,public_key,expires_at
            """, connection, transaction);
        query.Parameters.AddWithValue(id); query.Parameters.AddWithValue(Hash(nonce)); query.Parameters.AddWithValue(purpose); query.Parameters.AddWithValue(time.GetUtcNow());
        SocialChallengeState state;
        await using (var reader = await query.ExecuteReaderAsync(cancellation))
        {
            if (!await reader.ReadAsync(cancellation)) { throw new AuthException("social-invalid"); }
            state = new(id, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.IsDBNull(4) ? null : (byte[])reader[4], reader.GetFieldValue<DateTimeOffset>(5));
        }
        await transaction.CommitAsync(cancellation); // A failed provider request or device proof still consumes the challenge.
        return state;
    }

    internal async Task LinkSocialIdentityAsync(SocialChallengeState challenge, VerifiedSocialIdentity identity, string accessToken,
        AppleCredential? credential, CancellationToken cancellation)
    {
        Guid account = challenge.Account ?? throw new AuthException("social-invalid");
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        await SocialAccountLockAsync(connection, transaction, account, cancellation);
        await RequireSocialSessionAsync(connection, transaction, account, accessToken, cancellation);
        await CheckSocialChallengeDeadlineAsync(challenge, cancellation);
        await CheckPendingRevocationAsync(connection, transaction, credential, cancellation);
        await Run(connection, transaction, "INSERT INTO auth_social_identities VALUES($1,$2,$3) ON CONFLICT(provider,subject) DO NOTHING", cancellation,
            identity.Provider, identity.Subject, account);
        await using var query = new NpgsqlCommand("SELECT account_id FROM auth_social_identities WHERE provider=$1 AND subject=$2", connection, transaction);
        query.Parameters.AddWithValue(identity.Provider); query.Parameters.AddWithValue(identity.Subject);
        if (await query.ExecuteScalarAsync(cancellation) is not Guid owner || owner != account) { throw new AuthException("social-conflict"); }
        // A uniqueness wait can cross another transaction commit; refresh the pending-work check afterward.
        await CheckPendingRevocationAsync(connection, transaction, credential, cancellation);
        await SaveAppleCredentialAsync(connection, transaction, account, credential, cancellation);
        await transaction.CommitAsync(cancellation);
    }

    internal async Task<(Guid Account, AuthTokens? Tokens)> UseSocialIdentityAsync(SocialChallengeState challenge, VerifiedSocialIdentity identity,
        AppleCredential? credential, CancellationToken cancellation)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellation);
        await using var transaction = await connection.BeginTransactionAsync(cancellation);
        Guid account;
        await using (var find = new NpgsqlCommand("SELECT account_id FROM auth_social_identities WHERE provider=$1 AND subject=$2", connection, transaction))
        {
            find.Parameters.AddWithValue(identity.Provider); find.Parameters.AddWithValue(identity.Subject);
            account = await find.ExecuteScalarAsync(cancellation) is Guid owner ? owner : throw new AuthException("social-invalid");
        }
        await SocialAccountLockAsync(connection, transaction, account, cancellation);
        await CheckSocialChallengeDeadlineAsync(challenge, cancellation);
        await CheckPendingRevocationAsync(connection, transaction, credential, cancellation);
        // The identity cannot migrate or be unlinked; purge and deletion are serialized by the account lock above.
        await SaveAppleCredentialAsync(connection, transaction, account, credential, cancellation);
        AuthTokens? tokens = null;
        if (challenge.Purpose == "login")
        {
            byte[] key = challenge.PublicKey ?? throw new AuthException("social-invalid");
            var keyId = Guid.NewGuid();
            await Run(connection, transaction, "INSERT INTO auth_device_keys VALUES($1,$2,$3,$4,NULL)", cancellation, keyId, account, key, time.GetUtcNow());
            tokens = await IssueAsync(connection, transaction, account, keyId, Guid.NewGuid(), cancellation);
        }
        await transaction.CommitAsync(cancellation);
        return (account, tokens);
    }

    private Task CheckSocialChallengeDeadlineAsync(SocialChallengeState challenge, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (challenge.ExpiresAt <= time.GetUtcNow()) { throw new AuthException("social-invalid"); }
        return Task.CompletedTask;
    }

    private async Task SocialAccountLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, CancellationToken cancellation)
    {
        await using var locked = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction);
        locked.Parameters.AddWithValue(account);
        if (await locked.ExecuteScalarAsync(cancellation) is null) { throw new AuthException("social-invalid"); }
        await using var deleted = new NpgsqlCommand("SELECT 1 FROM auth_deleted_accounts WHERE account_id=$1", connection, transaction);
        deleted.Parameters.AddWithValue(account);
        if (await deleted.ExecuteScalarAsync(cancellation) is not null) { throw new AuthException("social-invalid"); }
    }

    private async Task RequireSocialSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, string token, CancellationToken cancellation)
    {
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c))) { throw new AuthException("social-invalid"); }
        await using var session = new NpgsqlCommand("SELECT 1 FROM sessions WHERE token_hash=$1 AND account_id=$2 AND expires_at>$3", connection, transaction);
        session.Parameters.AddWithValue(Hash(token)); session.Parameters.AddWithValue(account); session.Parameters.AddWithValue(time.GetUtcNow());
        if (await session.ExecuteScalarAsync(cancellation) is null) { throw new AuthException("social-invalid"); }
    }

    private static async Task CheckPendingRevocationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AppleCredential? credential, CancellationToken cancellation)
    {
        if (credential is null) { return; }
        await using var pending = new NpgsqlCommand("SELECT 1 FROM auth_apple_revocations WHERE subject_ref=$1 LIMIT 1", connection, transaction);
        pending.Parameters.AddWithValue(credential.SubjectRef);
        if (await pending.ExecuteScalarAsync(cancellation) is not null) { throw new AuthException("social-revocation-pending"); }
    }

    private static async Task SaveAppleCredentialAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, AppleCredential? credential, CancellationToken cancellation)
    {
        if (credential is null) { return; }
        await Run(connection, transaction, "INSERT INTO auth_social_credentials VALUES($1,$2,$3,$4,$5)", cancellation,
            credential.Id, account, credential.SubjectRef, credential.ClientId, credential.Encrypted);
    }

    internal async Task QueueAppleRevocationsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, CancellationToken cancellation)
    {
        await Run(connection, transaction, """
            INSERT INTO auth_apple_revocations(id,subject_ref,client_id,credential,retry_at)
            SELECT id,subject_ref,client_id,credential,$2 FROM auth_social_credentials WHERE account_id=$1 ON CONFLICT DO NOTHING
            """, cancellation, account, time.GetUtcNow());
        await Run(connection, transaction, "DELETE FROM auth_social_credentials WHERE account_id=$1", cancellation, account);
    }

    internal async Task PurgeSocialAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid account, CancellationToken cancellation)
    {
        await QueueAppleRevocationsAsync(connection, transaction, account, cancellation);
        await Run(connection, transaction, "DELETE FROM auth_social_challenges WHERE account_id=$1", cancellation, account);
        await Run(connection, transaction, "DELETE FROM auth_social_identities WHERE account_id=$1", cancellation, account);
    }

    internal async Task<int> ExecuteAppleRevocationsAsync(Func<AppleCredential, CancellationToken, Task> revoke, CancellationToken cancellation)
    {
        int completed = 0;
        for (int index = 0; index < 20; index++)
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellation);
            await using var transaction = await connection.BeginTransactionAsync(cancellation);
            AppleCredential credential;
            await using (var query = new NpgsqlCommand("""
                SELECT id,subject_ref,client_id,credential FROM auth_apple_revocations
                WHERE retry_at<=$1 ORDER BY retry_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
                """, connection, transaction))
            {
                query.Parameters.AddWithValue(time.GetUtcNow());
                await using var reader = await query.ExecuteReaderAsync(cancellation);
                if (!await reader.ReadAsync(cancellation)) { break; }
                credential = new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), (byte[])reader[3]);
            }
            bool succeeded = false;
            try
            {
                await revoke(credential, cancellation);
                succeeded = true;
            }
            catch (Exception failure) when (!cancellation.IsCancellationRequested &&
                failure is AuthException or CryptographicException or ArgumentException or HttpRequestException or OperationCanceledException)
            {
                // Retain only encrypted work and retry metadata; provider exception text may contain secrets.
            }
            if (succeeded)
            {
                await Run(connection, transaction, "DELETE FROM auth_apple_revocations WHERE id=$1", cancellation, credential.Id);
            }
            else
            {
                await Run(connection, transaction, "UPDATE auth_apple_revocations SET retry_at=$2,attempts=attempts+1 WHERE id=$1", cancellation,
                    credential.Id, time.GetUtcNow().AddMinutes(5));
            }
            await transaction.CommitAsync(cancellation);
            if (succeeded) { completed++; }
        }
        return completed;
    }
}
