using System.Security.Cryptography;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class SocialAuthStoreTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Verifier : ISocialIdentityVerifier
    {
        public Task<VerifiedSocialIdentity> VerifyAsync(string provider, string clientId, string token, string nonce, CancellationToken cancellation) =>
            token.StartsWith("subject-", StringComparison.Ordinal)
                ? Task.FromResult(new VerifiedSocialIdentity(provider, token, clientId)) : throw new AuthException("social-invalid");
    }

    private sealed class Apple : IAppleTokenClient
    {
        public bool Fail { get; set; }
        public List<string> Revoked { get; } = [];
        public Func<CancellationToken, Task>? BeforeRevoke { get; set; }
        public int RevokeCalls;
        public Task<AppleTokenExchange> ExchangeAsync(string clientId, string code, string? redirectUri, CancellationToken cancellation) =>
            Task.FromResult(new AppleTokenExchange(code, "private-refresh-" + code));
        public async Task RevokeAsync(string clientId, string refreshToken, CancellationToken cancellation)
        {
            Interlocked.Increment(ref RevokeCalls);
            if (BeforeRevoke is not null) { await BeforeRevoke(cancellation); }
            if (Fail) { throw new AuthException("social-unavailable"); }
            Revoked.Add(refreshToken);
        }
    }

    private sealed record Setup(NpgsqlDataSource Source, AccountStore Accounts, AuthStore Auth, SocialAuthService Social, Clock Clock, Apple Apple) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    // Frozen stage-1 DDL: migration tests must not initialize through the current schema first.
    private const string LegacyAuthSql = """
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

    private static int nextIp;
    private static string Ip() => "social-test-" + Interlocked.Increment(ref nextIp);

    private static async Task<Setup> CreateAsync(bool legacySchema = false)
    {
        string connectionString = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES required.");
        string schema = "social_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(connectionString))
        {
            await using var create = admin.CreateCommand("CREATE SCHEMA " + schema);
            await create.ExecuteNonQueryAsync();
        }
        // Purge scans all due accounts. A private test schema keeps its shifted clock away from other test classes.
        var source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString);
        var clock = new Clock();
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
        await accounts.InitializeAsync(CancellationToken.None);
        var auth = new AuthStore(source, accounts, new(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true,
            Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        if (legacySchema)
        {
            await using var initialize = source.CreateCommand(LegacyAuthSql);
            await initialize.ExecuteNonQueryAsync();
        }
        else { await auth.InitializeAsync(CancellationToken.None); }
        var apple = new Apple();
        var social = new SocialAuthService(auth, new Verifier(), apple,
            new([new("google", "test-client", []), new("apple", "test-client", [])], Enumerable.Repeat((byte)91, 32).ToArray()));
        return new(source, accounts, auth, social, clock, apple);
    }

    private static async Task Link(Setup setup, AuthTokens owner, string subject, string provider = "google")
    {
        var challenge = await setup.Social.ChallengeAsync(provider, "test-client", "link", owner.AccessToken, null, Ip(), CancellationToken.None);
        var result = await setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "link", subject, provider == "apple" ? subject : null,
            null, owner.AccessToken, CancellationToken.None);
        Assert.True(result.Linked);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task LinkedIdentityRecoversOnlyWithNewDeviceProofAndCannotReplayNonce()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        await Link(setup, owner, subject);
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var challenge = await setup.Social.ChallengeAsync("google", "test-client", "login", null, next.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        byte[] proof = next.SignData(SocialAuthService.SignedBytes(challenge.ChallengeId, challenge.Nonce), HashAlgorithmName.SHA256);
        var reply = await setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "login", subject, null, proof, null, CancellationToken.None);
        Assert.Equal(owner.AccountId, reply.Tokens!.AccountId);
        Assert.NotEqual(owner.KeyId, reply.Tokens.KeyId);
        Assert.Equal(owner.AccountId, await setup.Accounts.AuthenticateAsync(reply.Tokens.AccessToken, CancellationToken.None));
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "login", subject, null, proof, null, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task IdentityCannotMoveBetweenAccountsAndPendingDeletionCannotRestoreThroughSocialLogin()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var other = await setup.Auth.CreateGuestAsync(otherKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        await Link(setup, owner, subject);
        var conflict = await Assert.ThrowsAsync<AuthException>(() => Link(setup, other, subject));
        Assert.Equal("social-conflict", conflict.Message);
        await setup.Auth.DeleteAccountAsync(owner.AccountId, CancellationToken.None);
        var challenge = await setup.Social.ChallengeAsync("google", "test-client", "login", null, otherKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        byte[] proof = otherKey.SignData(SocialAuthService.SignedBytes(challenge.ChallengeId, challenge.Nonce), HashAlgorithmName.SHA256);
        Assert.Equal("social-invalid", (await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "login", subject,
            null, proof, null, CancellationToken.None))).Message);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task InvalidProofConsumesChallengeAndPurposeSubstitutionIsRejected()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var challenge = await setup.Social.ChallengeAsync("google", "test-client", "login", null, device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        byte[] proof = device.SignData(SocialAuthService.SignedBytes(challenge.ChallengeId, challenge.Nonce), HashAlgorithmName.SHA256);
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "delete", "subject-x", null, proof, null, CancellationToken.None));
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "login", "subject-x", null, [1, 2, 3], null, CancellationToken.None));
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "login", "subject-x", null, proof, null, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AppleCredentialIsEncryptedAndDeletionRetriesSurviveAccountPurge()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        await Link(setup, owner, subject, "apple");
        await using (var check = setup.Source.CreateCommand("SELECT credential FROM auth_social_credentials WHERE account_id=$1"))
        {
            check.Parameters.AddWithValue(owner.AccountId);
            byte[] encrypted = (byte[])(await check.ExecuteScalarAsync())!;
            Assert.DoesNotContain("private-refresh", System.Text.Encoding.UTF8.GetString(encrypted));
        }
        await setup.Social.ChallengeAsync("apple", "test-client", "link", owner.AccessToken, null, Ip(), CancellationToken.None);
        setup.Apple.Fail = true;
        await setup.Auth.DeleteAccountAsync(owner.AccountId, CancellationToken.None);
        Assert.Equal(0, await setup.Social.RetryRevocationsAsync(CancellationToken.None));
        setup.Clock.Now = setup.Clock.Now.AddDays(31);
        await setup.Auth.PurgeExpiredAsync(1000, CancellationToken.None);
        setup.Apple.Fail = false;
        Assert.True(await setup.Social.RetryRevocationsAsync(CancellationToken.None) >= 1);
        Assert.Contains("private-refresh-" + subject, setup.Apple.Revoked);
        await using var identities = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_identities WHERE account_id=$1");
        identities.Parameters.AddWithValue(owner.AccountId);
        Assert.Equal(0L, await identities.ExecuteScalarAsync());
        foreach (string table in new[] { "auth_social_challenges", "auth_social_credentials" })
        {
            await using var references = setup.Source.CreateCommand($"SELECT count(*) FROM {table} WHERE account_id=$1");
            references.Parameters.AddWithValue(owner.AccountId);
            Assert.Equal(0L, await references.ExecuteScalarAsync());
        }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task WebDeletionUsesLinkedProviderIdentityAndKeepsSameGraceDeadline()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        await Link(setup, owner, subject);
        var challenge = await setup.Social.ChallengeAsync("google", "test-client", "delete", null, null, Ip(), CancellationToken.None);
        var reply = await setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "delete", subject, null, null, null, CancellationToken.None);
        Assert.Equal(setup.Clock.Now.AddDays(30), reply.PurgeAt);
        Assert.Null(await setup.Accounts.AuthenticateAsync(owner.AccessToken, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task TwoAccountsRacingToLinkGetExactlyOneOwnerWithoutDeadlock()
    {
        await using var setup = await CreateAsync();
        using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var first = await setup.Auth.CreateGuestAsync(firstKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var second = await setup.Auth.CreateGuestAsync(secondKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        var a = await setup.Social.ChallengeAsync("google", "test-client", "link", first.AccessToken, null, Ip(), CancellationToken.None);
        var b = await setup.Social.ChallengeAsync("google", "test-client", "link", second.AccessToken, null, Ip(), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task<bool> Complete(SocialChallenge challenge, string token)
        {
            try
            {
                await setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "link", subject, null, null, token, timeout.Token);
                return true;
            }
            catch (AuthException error) when (error.Message == "social-conflict") { return false; }
        }
        var results = await Task.WhenAll(Complete(a, first.AccessToken), Complete(b, second.AccessToken));
        Assert.Single(results, success => success);
        await using var read = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_identities WHERE provider='google' AND subject=$1");
        read.Parameters.AddWithValue(subject);
        Assert.Equal(1L, await read.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ExpiredChallengeAndRevokedLinkSessionCannotAddAnIdentity()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var challenge = await setup.Social.ChallengeAsync("google", "test-client", "link", owner.AccessToken, null, Ip(), CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddMinutes(2);
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "link", "subject-expired", null,
            null, owner.AccessToken, CancellationToken.None));
        var next = await setup.Social.ChallengeAsync("google", "test-client", "link", owner.AccessToken, null, Ip(), CancellationToken.None);
        await setup.Auth.DeleteAccountAsync(owner.AccountId, CancellationToken.None);
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(next.ChallengeId, next.Nonce, "link", "subject-deleted", null,
            null, owner.AccessToken, CancellationToken.None));
        await using var read = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_identities WHERE account_id=$1");
        read.Parameters.AddWithValue(owner.AccountId);
        Assert.Equal(0L, await read.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AppleExchangeSubjectMismatchDoesNotLinkOrQueueRevocation()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var challenge = await setup.Social.ChallengeAsync("apple", "test-client", "link", owner.AccessToken, null, Ip(), CancellationToken.None);
        string original = "subject-" + Guid.NewGuid();
        string exchanged = "subject-" + Guid.NewGuid();
        await Assert.ThrowsAsync<AuthException>(() => setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "link", original, exchanged, null,
            owner.AccessToken, CancellationToken.None));
        await using var read = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_identities WHERE account_id=$1");
        read.Parameters.AddWithValue(owner.AccountId);
        Assert.Equal(0L, await read.ExecuteScalarAsync());
        await AssertNoRevocation(setup);
    }
    private static async Task AssertNoRevocation(Setup setup)
    {
        await using var queued = setup.Source.CreateCommand("SELECT count(*) FROM auth_apple_revocations");
        Assert.Equal(0L, await queued.ExecuteScalarAsync());
        Assert.Equal(0, await setup.Social.RetryRevocationsAsync(CancellationToken.None));
        Assert.Equal(0, setup.Apple.RevokeCalls);
        Assert.Empty(setup.Apple.Revoked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task AppleLinkCollisionNeverRevokesWinningIdentity(bool concurrent)
    {
        await using var setup = await CreateAsync();
        using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var first = await setup.Auth.CreateGuestAsync(firstKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var second = await setup.Auth.CreateGuestAsync(secondKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        if (concurrent)
        {
            async Task<bool> Attempt(AuthTokens owner)
            {
                try { await Link(setup, owner, subject, "apple"); return true; }
                catch (AuthException error) when (error.Message == "social-conflict") { return false; }
            }
            var results = await Task.WhenAll(Attempt(first), Attempt(second)).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Single(results, success => success);
        }
        else
        {
            await Link(setup, first, subject, "apple");
            Assert.Equal("social-conflict", (await Assert.ThrowsAsync<AuthException>(() => Link(setup, second, subject, "apple"))).Message);
        }
        await AssertNoRevocation(setup);
        await using var credentials = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_credentials");
        Assert.Equal(1L, await credentials.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RevocationWorkersSerializeOneJobAndBlockRelinkAcrossClientIds()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var owner = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        await Link(setup, owner, subject, "apple");
        await setup.Auth.DeleteAccountAsync(owner.AccountId, CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddDays(31);
        await setup.Auth.PurgeExpiredAsync(1000, CancellationToken.None);
        var replacement = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        setup.Apple.BeforeRevoke = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var worker = setup.Social.RetryRevocationsAsync(timeout.Token);
        try
        {
            await entered.Task.WaitAsync(timeout.Token);
            Assert.Equal(0, await setup.Social.RetryRevocationsAsync(timeout.Token));
            var otherClient = new SocialAuthService(setup.Auth, new Verifier(), setup.Apple,
                new([new("apple", "other-client", [])], Enumerable.Repeat((byte)91, 32).ToArray()));
            var challenge = await otherClient.ChallengeAsync("apple", "other-client", "link", replacement.AccessToken, null, Ip(), timeout.Token);
            var error = await Assert.ThrowsAsync<AuthException>(() => otherClient.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "link", subject,
                subject, null, replacement.AccessToken, timeout.Token));
            Assert.Equal("social-revocation-pending", error.Message);
            Assert.Equal(1, setup.Apple.RevokeCalls);
        }
        finally { release.TrySetResult(); await worker; }
        Assert.Equal(1, setup.Apple.RevokeCalls);
        Assert.Single(setup.Apple.Revoked);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task SchemaOneMigrationPreservesGuestProofAndRefreshAndIsIdempotent()
    {
        await using var setup = await CreateAsync(legacySchema: true);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var challenge = await setup.Auth.ChallengeAsync(guest.KeyId, Ip(), CancellationToken.None);
        await setup.Auth.InitializeAsync(CancellationToken.None);
        await setup.Auth.InitializeAsync(CancellationToken.None);
        var signature = device.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        var signedIn = await setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, signature, CancellationToken.None);
        var refreshed = await setup.Auth.RefreshAsync(guest.RefreshToken, CancellationToken.None);
        Assert.Equal(guest.AccountId, signedIn.AccountId);
        Assert.Equal(guest.KeyId, signedIn.KeyId);
        Assert.Equal(guest.AccountId, refreshed.AccountId);
        Assert.Equal(guest.AccountId, await setup.Accounts.AuthenticateAsync(refreshed.AccessToken, CancellationToken.None));
        await using var version = setup.Source.CreateCommand("SELECT array_agg(version ORDER BY version) FROM auth_schema");
        Assert.Equal(new[] { 1, 2, 3 }, (int[])(await version.ExecuteScalarAsync())!);
        await Link(setup, refreshed, "subject-migrated");
        await setup.Auth.InitializeAsync(CancellationToken.None);
        await using var identity = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_identities");
        Assert.Equal(1L, await identity.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task FutureAuthSchemaIsRejectedWithoutCreatingSocialTablesOrChangingGuest()
    {
        await using var setup = await CreateAsync(legacySchema: true);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        await using (var future = setup.Source.CreateCommand("INSERT INTO auth_schema VALUES(99)")) { await future.ExecuteNonQueryAsync(); }
        await using var catalog = setup.Source.CreateCommand("SELECT array_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname=current_schema()");
        var before = (string[])(await catalog.ExecuteScalarAsync())!;
        await Assert.ThrowsAsync<InvalidDataException>(() => setup.Auth.InitializeAsync(CancellationToken.None));
        Assert.Equal(before, (string[])(await catalog.ExecuteScalarAsync())!);
        await using var version = setup.Source.CreateCommand("SELECT array_agg(version ORDER BY version) FROM auth_schema");
        Assert.Equal(new[] { 1, 99 }, (int[])(await version.ExecuteScalarAsync())!);
        Assert.DoesNotContain("auth_social_identities", before);
        await using (var absent = setup.Source.CreateCommand("SELECT to_regprocedure('auth_command_response_ids_v1(text)')::text"))
        {
            Assert.Equal(DBNull.Value, await absent.ExecuteScalarAsync());
        }
        await using (var absent = setup.Source.CreateCommand("SELECT to_regclass('commands_response_account_ids')::text"))
        {
            Assert.Equal(DBNull.Value, await absent.ExecuteScalarAsync());
        }
        Assert.Equal(guest.AccountId, await setup.Accounts.AuthenticateAsync(guest.AccessToken, CancellationToken.None));
        Assert.Equal(guest.AccountId, (await setup.Auth.RefreshAsync(guest.RefreshToken, CancellationToken.None)).AccountId);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task LinkRechecksRevocationAfterIdentityUniquenessWaitAsTransactionOrderDefense()
    {
        await using var setup = await CreateAsync();
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var oldOwner = await setup.Auth.CreateGuestAsync(oldKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        var newOwner = await setup.Auth.CreateGuestAsync(newKey.ExportSubjectPublicKeyInfo(), Ip(), CancellationToken.None);
        string subject = "subject-" + Guid.NewGuid();
        await Link(setup, oldOwner, subject, "apple");
        var challenge = await setup.Social.ChallengeAsync("apple", "test-client", "link", newOwner.AccessToken, null, Ip(), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await setup.Source.OpenConnectionAsync(timeout.Token);
        await using var transaction = await connection.BeginTransactionAsync(timeout.Token);
        // Synthetic transaction-order defense: normal deletion already queues work before eventual purge.
        await using (var retiring = new NpgsqlCommand("""
            INSERT INTO auth_apple_revocations(id,subject_ref,client_id,credential,retry_at)
              SELECT id,subject_ref,client_id,credential,$2 FROM auth_social_credentials WHERE account_id=$1
            """, connection, transaction))
        {
            retiring.Parameters.AddWithValue(oldOwner.AccountId);
            retiring.Parameters.AddWithValue(setup.Clock.Now);
            await retiring.ExecuteNonQueryAsync(timeout.Token);
        }
        foreach (string table in new[] { "auth_social_credentials", "auth_social_identities" })
        {
            await using var retiring = new NpgsqlCommand($"DELETE FROM {table} WHERE account_id=$1", connection, transaction);
            retiring.Parameters.AddWithValue(oldOwner.AccountId);
            await retiring.ExecuteNonQueryAsync(timeout.Token);
        }
        var linking = setup.Social.CompleteAsync(challenge.ChallengeId, challenge.Nonce, "link", subject, subject, null, newOwner.AccessToken, timeout.Token);
        await using (var blocked = setup.Source.CreateCommand("""
            SELECT EXISTS(SELECT 1 FROM pg_stat_activity
              WHERE $1=ANY(pg_blocking_pids(pid)) AND query LIKE 'INSERT INTO auth_social_identities%')
            """))
        {
            blocked.Parameters.AddWithValue(connection.ProcessID);
            while (await blocked.ExecuteScalarAsync(timeout.Token) is not true)
            {
                Assert.False(linking.IsCompleted, "Link must reach the identity uniqueness wait before the retiring transaction commits.");
                await Task.Delay(10, timeout.Token);
            }
        }
        await transaction.CommitAsync(timeout.Token);
        var error = await Assert.ThrowsAsync<AuthException>(async () => await linking);
        Assert.Equal("social-revocation-pending", error.Message);
        await using var identities = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_identities WHERE account_id=$1");
        identities.Parameters.AddWithValue(newOwner.AccountId);
        Assert.Equal(0L, await identities.ExecuteScalarAsync(timeout.Token));
        await using var credentials = setup.Source.CreateCommand("SELECT count(*) FROM auth_social_credentials WHERE account_id=$1");
        credentials.Parameters.AddWithValue(newOwner.AccountId);
        Assert.Equal(0L, await credentials.ExecuteScalarAsync(timeout.Token));
    }

}
