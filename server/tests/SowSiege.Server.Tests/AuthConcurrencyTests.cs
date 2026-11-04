using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class AuthConcurrencyTests
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);
    }

    private sealed record Setup(NpgsqlDataSource Source, AccountStore Accounts, AuthStore Auth, AllianceStore Alliances, string ApplicationName) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    private static async Task<Setup> CreateAsync()
    {
        string applicationName = "auth-race-" + Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.")) { ApplicationName = applicationName };
        string schema = "auth_concurrency_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(settings.ConnectionString))
        {
            await using var create = admin.CreateCommand("CREATE SCHEMA " + schema);
            await create.ExecuteNonQueryAsync();
        }
        // Other fixtures advance their clocks and prune expired proofs; their cleanup must not alter this lock race.
        settings.SearchPath = schema;
        var source = NpgsqlDataSource.Create(settings.ConnectionString);
        var clock = new Clock();
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
        var auth = new AuthStore(source, accounts, new(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromSeconds(60), AuthTests.ShippedLimits(), true,
            Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        var alliances = new AllianceStore(source, AllianceContent.Load(ContentEstateTests.Root()), clock);
        await accounts.InitializeAsync(CancellationToken.None);
        await auth.InitializeAsync(CancellationToken.None);
        await alliances.InitializeAsync(CancellationToken.None);
        return new(source, accounts, auth, alliances, applicationName);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeviceLoginQueuedAfterDeletionRestoresUnderTheAccountLock()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string ip = Guid.NewGuid().ToString("N");
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), ip, CancellationToken.None);
        var challenge = await setup.Auth.ChallengeAsync(guest.KeyId, ip, CancellationToken.None);
        byte[] signature = device.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var gate = await setup.Source.OpenConnectionAsync(timeout.Token);
        await using var transaction = await gate.BeginTransactionAsync(timeout.Token);
        await Run(gate, transaction, "SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", timeout.Token, guest.AccountId);

        Task<DateTimeOffset> deletion = setup.Auth.DeleteAccountAsync(guest.AccountId, timeout.Token);
        await WaitForBlockedAsync(setup, 1, timeout.Token);
        Task<AuthTokens> login = setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, signature, timeout.Token);
        await WaitForBlockedAsync(setup, 2, timeout.Token);
        await transaction.CommitAsync(timeout.Token);

        await deletion;
        var restored = await login;
        Assert.True(restored.Restored);
        Assert.Equal(guest.AccountId, await setup.Accounts.AuthenticateAsync(restored.AccessToken, timeout.Token));
        Assert.Null(await setup.Accounts.AuthenticateAsync(guest.AccessToken, timeout.Token));
        Assert.Equal(0L, await Count(setup, "SELECT count(*) FROM auth_deleted_accounts WHERE account_id=$1", guest.AccountId));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RefreshQueuedAfterDeletionCannotMintASessionOrDeadlock()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), Guid.NewGuid().ToString("N"), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var gate = await setup.Source.OpenConnectionAsync(timeout.Token);
        await using var transaction = await gate.BeginTransactionAsync(timeout.Token);
        await Run(gate, transaction, "SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", timeout.Token, guest.AccountId);

        Task<DateTimeOffset> deletion = setup.Auth.DeleteAccountAsync(guest.AccountId, timeout.Token);
        await WaitForBlockedAsync(setup, 1, timeout.Token);
        Task<AuthTokens> refresh = setup.Auth.RefreshAsync(guest.RefreshToken, timeout.Token);
        await WaitForBlockedAsync(setup, 2, timeout.Token);
        await transaction.CommitAsync(timeout.Token);

        await deletion;
        Assert.Equal("refresh-reused", (await Assert.ThrowsAsync<AuthException>(() => refresh)).Message);
        Assert.Equal(0L, await Count(setup, "SELECT count(*) FROM sessions WHERE account_id=$1", guest.AccountId));
        Assert.Equal(1L, await Count(setup, "SELECT count(*) FROM auth_deleted_accounts WHERE account_id=$1", guest.AccountId));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task MembershipChangeRestartsDeletionBeforeLockingTheNewAlliance()
    {
        await using var setup = await CreateAsync();
        using var leaderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leader = await setup.Auth.CreateGuestAsync(leaderKey.ExportSubjectPublicKeyInfo(), Guid.NewGuid().ToString("N"), CancellationToken.None);
        var member = await setup.Auth.CreateGuestAsync(memberKey.ExportSubjectPublicKeyInfo(), Guid.NewGuid().ToString("N"), CancellationToken.None);
        var create = new AccountCommand(Guid.NewGuid(), 0, AllianceStore.Domain, "create",
            JsonSerializer.SerializeToElement(new { name = "Race" + Guid.NewGuid().ToString("N")[..6], mode = "open" }));
        var reply = JsonSerializer.Deserialize<AllianceReply>(await setup.Alliances.ExecuteAsync(leader.AccountId, create, CancellationToken.None), AccountStore.Json)!;
        Assert.True(reply.Done);
        Guid alliance = reply.CreatedId!.Value;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // A join owns alliance then account. Deletion discovers no membership while this join is uncommitted.
        await using var joining = await setup.Source.OpenConnectionAsync(timeout.Token);
        await using var joinTransaction = await joining.BeginTransactionAsync(timeout.Token);
        await Run(joining, joinTransaction, "SELECT 1 FROM alliances WHERE id=$1 FOR UPDATE", timeout.Token, alliance);
        await Run(joining, joinTransaction, "SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", timeout.Token, member.AccountId);
        Task<DateTimeOffset> deletion = setup.Auth.DeleteAccountAsync(member.AccountId, timeout.Token);
        await WaitForBlockedAsync(setup, 1, timeout.Token);

        // This second alliance-first operation queues before the deletion can discover its new membership.
        await using var contender = await setup.Source.OpenConnectionAsync(timeout.Token);
        await using var contenderTransaction = await contender.BeginTransactionAsync(timeout.Token);
        Task<int> nextAlliance = Run(contender, contenderTransaction, "SELECT 1 FROM alliances WHERE id=$1 FOR UPDATE", timeout.Token, alliance);
        await WaitForBlockedAsync(setup, 2, timeout.Token);
        await Run(joining, joinTransaction, "INSERT INTO alliance_members VALUES($1,$2,'member',$3)", timeout.Token,
            member.AccountId, alliance, new Clock().GetUtcNow().ToUnixTimeMilliseconds());
        await Run(joining, joinTransaction, "UPDATE alliances SET member_count=member_count+1 WHERE id=$1", timeout.Token, alliance);
        await joinTransaction.CommitAsync(timeout.Token);
        await nextAlliance;
        await WaitForBlockedAsync(setup, 1, timeout.Token, contender.ProcessID);

        // A stale deletion must release its account lock before asking for the alliance lock; otherwise this forms a cycle.
        await Run(contender, contenderTransaction, "SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", timeout.Token, member.AccountId);
        await contenderTransaction.CommitAsync(timeout.Token);
        await deletion;
        var view = await setup.Alliances.ViewAsync(leader.AccountId, timeout.Token);
        Assert.Equal(leader.AccountId, view.Alliance!.Leader);
        Assert.Equal(1, view.Alliance.MemberCount);
        Assert.Single(view.Members);
        Assert.Equal("leader", view.Members.Single().Role);
        Assert.Equal(0L, await Count(setup, "SELECT count(*) FROM alliance_members WHERE account_id=$1", member.AccountId));
        Assert.Equal(1L, await Count(setup, "SELECT count(*) FROM auth_deleted_accounts WHERE account_id=$1", member.AccountId));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeletingAMissingAccountIsAnAuthenticationFailure()
    {
        await using var setup = await CreateAsync();
        Guid missing = Guid.NewGuid();
        Assert.Equal("key-invalid", (await Assert.ThrowsAsync<AuthException>(() => setup.Auth.DeleteAccountAsync(missing, CancellationToken.None))).Message);
        Assert.Equal(0L, await Count(setup, "SELECT count(*) FROM auth_deleted_accounts WHERE account_id=$1", missing));
    }

    private static async Task WaitForBlockedAsync(Setup setup, int minimum, CancellationToken cancellation, int? blocker = null)
    {
        // Observe the database lock barrier, not elapsed wall time; every loop remains bounded by the test cancellation token.
        while (true)
        {
            await using var query = setup.Source.CreateCommand("""
                SELECT count(*) FROM pg_stat_activity
                WHERE application_name=$1 AND wait_event_type='Lock'
                  AND ($2::integer IS NULL OR $2=ANY(pg_blocking_pids(pid)))
                """);
            query.Parameters.AddWithValue(setup.ApplicationName);
            query.Parameters.AddWithValue(blocker is { } pid ? pid : DBNull.Value);
            if (Convert.ToInt64(await query.ExecuteScalarAsync(cancellation)) >= minimum) { return; }
            await Task.Yield();
            cancellation.ThrowIfCancellationRequested();
        }
    }

    private static async Task<long> Count(Setup setup, string sql, Guid account)
    {
        await using var query = setup.Source.CreateCommand(sql);
        query.Parameters.AddWithValue(account);
        return Convert.ToInt64(await query.ExecuteScalarAsync());
    }

    private static async Task<int> Run(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellation, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (object value in values) { command.Parameters.AddWithValue(value); }
        return await command.ExecuteNonQueryAsync(cancellation);
    }
}
