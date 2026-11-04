using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class AuthTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 5, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Setup(NpgsqlDataSource Source, AccountStore Accounts, AuthStore Auth, ManualClock Clock) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();
    }

    /// <summary>Limits as shipped in the Host appsettings.json (non-secret configuration).</summary>
    internal static AuthLimits ShippedLimits()
    {
        string path = Path.Combine(ContentEstateTests.Root(), "server", "src", "SowSiege.Server.Host", "appsettings.json");
        return new ConfigurationBuilder().AddJsonFile(path).Build().GetSection("Auth:Limits").Get<AuthLimits>()!;
    }

    private static int ip;
    private static string NextIp() => "10.0." + Interlocked.Increment(ref ip) / 250 + "." + ip % 250;

    private static async Task<Setup> CreateAsync(bool guestOpen = true, bool isolated = false)
    {
        string connectionString = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
        if (isolated)
        {
            string schema = "auth_purge_test_" + Guid.NewGuid().ToString("N");
            await using var admin = NpgsqlDataSource.Create(connectionString);
            await using var create = admin.CreateCommand("CREATE SCHEMA " + schema);
            await create.ExecuteNonQueryAsync();
            connectionString = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        }
        var source = NpgsqlDataSource.Create(connectionString);
        var clock = new ManualClock();
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
        await accounts.InitializeAsync(CancellationToken.None);
        var auth = new AuthStore(source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromSeconds(60), ShippedLimits(), guestOpen,
            Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        await auth.InitializeAsync(CancellationToken.None);
        return new(source, accounts, auth, clock);
    }

    private static async Task<AuthTokens> SignInAsync(Setup setup, ECDsa device, Guid keyId)
    {
        var challenge = await setup.Auth.ChallengeAsync(keyId, NextIp(), CancellationToken.None);
        var signature = device.SignData(AuthStore.SignedBytes(keyId, challenge.Nonce), HashAlgorithmName.SHA256);
        return await setup.Auth.SignInAsync(keyId, challenge.Nonce, signature, CancellationToken.None);
    }

    private static async Task<string> Code(Func<Task> action) => (await Assert.ThrowsAsync<AuthException>(action)).Message;

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task GuestDeviceKeySignsInAndNoncesAreSingleUse()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        Assert.Equal(guest.AccountId, await setup.Accounts.AuthenticateAsync(guest.AccessToken, CancellationToken.None));
        Assert.Equal(setup.Clock.Now.AddMinutes(15), guest.AccessExpiresAt);
        await using (var sessions = setup.Source.CreateCommand("SELECT count(*) FROM sessions WHERE account_id=$1"))
        {
            sessions.Parameters.AddWithValue(guest.AccountId);
            Assert.Equal(1L, await sessions.ExecuteScalarAsync()); // the prototype creation session was replaced
        }
        var signedIn = await SignInAsync(setup, device, guest.KeyId);
        Assert.Equal(guest.AccountId, signedIn.AccountId);

        // A replayed nonce, a wrong signature and an expired challenge all fail; a failed signature still consumes the nonce.
        var challenge = await setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None);
        var good = device.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        await setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, good, CancellationToken.None);
        Assert.Equal("challenge-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, good, CancellationToken.None)));
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = await setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None);
        var wrong = stranger.SignData(AuthStore.SignedBytes(guest.KeyId, forged.Nonce), HashAlgorithmName.SHA256);
        Assert.Equal("signature-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, forged.Nonce, wrong, CancellationToken.None)));
        var right = device.SignData(AuthStore.SignedBytes(guest.KeyId, forged.Nonce), HashAlgorithmName.SHA256);
        Assert.Equal("challenge-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, forged.Nonce, right, CancellationToken.None)));
        var late = await setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddSeconds(61);
        var lateSignature = device.SignData(AuthStore.SignedBytes(guest.KeyId, late.Nonce), HashAlgorithmName.SHA256);
        Assert.Equal("challenge-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, late.Nonce, lateSignature, CancellationToken.None)));
        // Access tokens are short-lived.
        setup.Clock.Now = setup.Clock.Now.AddMinutes(15);
        Assert.Null(await setup.Accounts.AuthenticateAsync(guest.AccessToken, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RefreshTokensRotateAndReuseRevokesTheFamily()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var first = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        var second = await setup.Auth.RefreshAsync(first.RefreshToken, CancellationToken.None);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(first.AccountId, await setup.Accounts.AuthenticateAsync(second.AccessToken, CancellationToken.None));
        // Presenting the already used first token is a theft signal: the whole family, including the newest token, is revoked.
        Assert.Equal("refresh-reused", await Code(() => setup.Auth.RefreshAsync(first.RefreshToken, CancellationToken.None)));
        Assert.Equal("refresh-reused", await Code(() => setup.Auth.RefreshAsync(second.RefreshToken, CancellationToken.None)));
        // A device sign-in starts a new family; it expires after the refresh lifetime.
        var fresh = await SignInAsync(setup, device, first.KeyId);
        setup.Clock.Now = setup.Clock.Now.AddDays(30);
        Assert.Equal("refresh-expired", await Code(() => setup.Auth.RefreshAsync(fresh.RefreshToken, CancellationToken.None)));
        Assert.Equal("refresh-invalid", await Code(() => setup.Auth.RefreshAsync(new string('0', 64), CancellationToken.None)));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task InAppDeletionRevokesSessionsAndRefreshTokens()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        var other = await SignInAsync(setup, device, guest.KeyId);
        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
        Assert.Null(await setup.Accounts.AuthenticateAsync(guest.AccessToken, CancellationToken.None));
        Assert.Null(await setup.Accounts.AuthenticateAsync(other.AccessToken, CancellationToken.None));
        Assert.Equal("refresh-reused", await Code(() => setup.Auth.RefreshAsync(other.RefreshToken, CancellationToken.None)));
        // Deleting twice is harmless and keeps the first purge time.
        var purgeAt = await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
        Assert.Equal(setup.Clock.Now.AddDays(30), purgeAt);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task SameDeviceRestoresWithinGraceButNotAfter()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddDays(29);
        var restored = await SignInAsync(setup, device, guest.KeyId);
        Assert.True(restored.Restored);
        Assert.Equal(guest.AccountId, await setup.Accounts.AuthenticateAsync(restored.AccessToken, CancellationToken.None));
        Assert.False((await setup.Auth.RefreshAsync(restored.RefreshToken, CancellationToken.None)).Restored);
        Assert.False((await SignInAsync(setup, device, guest.KeyId)).Restored);
        // Restored accounts are not purged later.
        setup.Clock.Now = setup.Clock.Now.AddDays(60);
        await setup.Auth.PurgeExpiredAsync(1000, CancellationToken.None);
        Assert.False((await SignInAsync(setup, device, guest.KeyId)).Restored);

        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddDays(30);
        Assert.Equal("key-invalid", await Code(() => setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None)));
    }

    /// <summary>
    /// ADR0080 §5 table, fixed: after the purge no table keeps the account ID in any uuid or text column. Retained records (alliance
    /// chat bodies, report snapshots) remain with the ID replaced by the keyed pseudonym. A table added later that stores the ID fails here.
    /// </summary>
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task PurgeAfterGraceRemovesTheAccountAndPseudonymisesRetainedRecords()
    {
        await using var setup = await CreateAsync();
        var alliances = new AllianceStore(setup.Source, AllianceContent.Load(ContentEstateTests.Root()), setup.Clock);
        await alliances.InitializeAsync(CancellationToken.None);
        using var goneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stayKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var moderatorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var gone = (await setup.Auth.CreateGuestAsync(goneKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        var stay = (await setup.Auth.CreateGuestAsync(stayKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        var moderator = (await setup.Auth.CreateGuestAsync(moderatorKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        async Task<string> Act(Guid id, string op, object payload)
        {
            await using var q = setup.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
            q.Parameters.AddWithValue(id);
            var command = new SowSiege.Server.Contracts.AccountCommand(Guid.NewGuid(), (long)(await q.ExecuteScalarAsync())!, AllianceStore.Domain, op,
                System.Text.Json.JsonSerializer.SerializeToElement(payload));
            setup.Clock.Now = setup.Clock.Now.AddSeconds(10);
            return await alliances.ExecuteAsync(id, command, CancellationToken.None);
        }
        var created = System.Text.Json.JsonSerializer.Deserialize<AllianceReply>(await Act(gone, "create", new { name = "Purge", mode = "open" }), AccountStore.Json)!;
        await Act(stay, "apply", new { allianceId = created.CreatedId });
        await Act(moderator, "apply", new { allianceId = created.CreatedId });
        await Act(gone, "role-change", new { accountId = moderator, role = "officer" });
        var goneMessage = Guid.NewGuid();
        await Act(gone, "chat", new { messageId = goneMessage, text = "hello" });
        await Act(stay, "chat", new { messageId = Guid.NewGuid(), text = "hi" });
        await Act(stay, "report", new { messageId = goneMessage, reason = "spam-ad" });
        Guid deletedMessage = Guid.NewGuid(), hiddenMessage = Guid.NewGuid();
        await Act(gone, "chat", new { messageId = deletedMessage, text = "deleted evidence" });
        await Act(moderator, "report", new { messageId = deletedMessage, reason = "spam-ad" });
        var deleted = System.Text.Json.JsonSerializer.Deserialize<AllianceReply>(await Act(gone, "message-delete", new { messageId = deletedMessage }), AccountStore.Json)!;
        Assert.True(deleted.Done);
        await Act(stay, "chat", new { messageId = hiddenMessage, text = "hidden evidence" });
        await Act(gone, "report", new { messageId = hiddenMessage, reason = "spam-ad" });
        var hidden = System.Text.Json.JsonSerializer.Deserialize<AllianceReply>(await Act(moderator, "message-hide", new { messageId = hiddenMessage }), AccountStore.Json)!;
        Assert.True(hidden.Done);
        await Act(gone, "block", new { accountId = stay });
        await Act(stay, "request-help", new { nodeId = "x" }); // may be refused without a research job; the purge must not depend on it

        await setup.Auth.DeleteAccountAsync(gone, CancellationToken.None);
        await setup.Auth.DeleteAccountAsync(moderator, CancellationToken.None);
        var view = await alliances.ViewAsync(stay, CancellationToken.None);
        Assert.Equal(stay, view.Alliance!.Leader);
        Assert.Null(view.Chat.Single(m => m.MessageId == goneMessage).Author); // shown as "삭제된 사용자"
        setup.Clock.Now = setup.Clock.Now.AddDays(30).AddSeconds(-1);
        await setup.Auth.PurgeExpiredAsync(1000, CancellationToken.None);
        Assert.Equal(1, await Count("SELECT count(*) FROM accounts WHERE id=$1", gone));
        Assert.Equal(1, await Count("SELECT count(*) FROM accounts WHERE id=$1", moderator));

        setup.Clock.Now = setup.Clock.Now.AddSeconds(1);
        Assert.True(await setup.Auth.PurgeExpiredAsync(1000, CancellationToken.None) >= 1);
        // Deleted: every row keyed by the account, in every table.
        await using (var columns = setup.Source.CreateCommand("""
            SELECT table_name, column_name, data_type FROM information_schema.columns
            WHERE table_schema=current_schema() AND data_type IN ('uuid','text','jsonb')
            """))
        {
            var found = new List<string>();
            var list = new List<(string Table, string Column, string Type)>();
            await using (var reader = await columns.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) { list.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2))); }
            }
            foreach (var (table, column, type) in list)
            {
                string sql = type == "uuid" ? $"SELECT count(*) FROM \"{table}\" WHERE \"{column}\"=$1"
                    : $"SELECT count(*) FROM \"{table}\" WHERE strpos(\"{column}\"::text, $1::text) > 0";
                foreach (Guid removed in new[] { gone, moderator })
                {
                    if (await Count(sql, removed) > 0) { found.Add(removed + ":" + table + "." + column); }
                }
            }
            Assert.Empty(found);
        }
        // Retained: chat body (200-message rule) and the report snapshot (180 days), under the pseudonym.
        string pseudonym = setup.Auth.Pseudonym(gone);
        string moderatorPseudonym = setup.Auth.Pseudonym(moderator);
        Assert.Equal(1, await Count("SELECT count(*) FROM alliance_messages WHERE message_id=$1 AND deleted_by IS NULL AND deleted_by_ref=$2 AND deleted_ms IS NOT NULL", deletedMessage, pseudonym));
        Assert.Equal(1, await Count("SELECT count(*) FROM alliance_messages WHERE message_id=$1 AND hidden_by IS NULL AND hidden_by_ref=$2 AND hidden_ms IS NOT NULL", hiddenMessage, moderatorPseudonym));
        Assert.Equal(1, await Count("SELECT count(*) FROM alliance_reports WHERE message_id=$1 AND author IS NULL AND author_ref=$2 AND reporter IS NULL AND reporter_ref=$3 AND body_snapshot='deleted evidence'", deletedMessage, pseudonym, moderatorPseudonym));
        Assert.Equal(1, await Count("SELECT count(*) FROM alliance_reports WHERE message_id=$1 AND author=$2 AND reporter IS NULL AND reporter_ref=$3 AND body_snapshot='hidden evidence'", hiddenMessage, stay, pseudonym));
        Assert.Equal(1, await Count("SELECT count(*) FROM alliance_messages WHERE author IS NULL AND author_ref=$2 AND message_id=$1", goneMessage, pseudonym));
        Assert.Equal(1, await Count("SELECT count(*) FROM alliance_reports WHERE author IS NULL AND author_ref=$2 AND message_id=$1 AND reporter IS NOT NULL", goneMessage, pseudonym));
        Assert.Null((await alliances.ViewAsync(stay, CancellationToken.None)).Chat.Single(m => m.MessageId == goneMessage).Author);
        // A report filed after the purge keeps the same pseudonym, so operations can link the evidence.
        await Act(stay, "report", new { messageId = goneMessage, reason = "harassment" });
        Assert.Equal(2, await Count("SELECT count(*) FROM alliance_reports WHERE author_ref=$2 AND message_id=$1", goneMessage, pseudonym));
        Assert.Equal("key-invalid", await Code(() => setup.Auth.ChallengeAsync(Guid.Empty, NextIp(), CancellationToken.None)));

        async Task<long> Count(string sql, params object[] values)
        {
            await using var q = setup.Source.CreateCommand(sql);
            foreach (var value in values) { q.Parameters.AddWithValue(value); }
            return Convert.ToInt64(await q.ExecuteScalarAsync());
        }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentCrossAuditPurgesLockAllianceBeforeAccounts()
    {
        await using var setup = await CreateAsync(isolated: true);
        var alliances = new AllianceStore(setup.Source, AllianceContent.Load(ContentEstateTests.Root()), setup.Clock);
        await alliances.InitializeAsync(CancellationToken.None);
        using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Guid first = (await setup.Auth.CreateGuestAsync(firstKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        Guid second = (await setup.Auth.CreateGuestAsync(secondKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        async Task<AllianceReply> Act(Guid actor, string operation, object payload)
        {
            await using var version = setup.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
            version.Parameters.AddWithValue(actor);
            var command = new SowSiege.Server.Contracts.AccountCommand(Guid.NewGuid(), (long)(await version.ExecuteScalarAsync())!, AllianceStore.Domain,
                operation, System.Text.Json.JsonSerializer.SerializeToElement(payload));
            setup.Clock.Now = setup.Clock.Now.AddSeconds(10);
            var reply = System.Text.Json.JsonSerializer.Deserialize<AllianceReply>(await alliances.ExecuteAsync(actor, command, CancellationToken.None), AccountStore.Json)!;
            Assert.True(reply.Done);
            return reply;
        }
        Guid alliance = (await Act(first, "create", new { name = "Cross Audit", mode = "open" })).CreatedId!.Value;
        await Act(second, "apply", new { allianceId = alliance });
        Guid firstMessage = Guid.NewGuid(), secondMessage = Guid.NewGuid();
        await Act(first, "chat", new { messageId = firstMessage, text = "first evidence" });
        await Act(second, "chat", new { messageId = secondMessage, text = "second evidence" });
        await Act(first, "report", new { messageId = secondMessage, reason = "spam-ad" });
        await Act(second, "report", new { messageId = firstMessage, reason = "spam-ad" });
        await Act(first, "message-hide", new { messageId = secondMessage });
        await setup.Auth.DeleteAccountAsync(first, CancellationToken.None);
        await Act(second, "message-hide", new { messageId = firstMessage });
        await setup.Auth.DeleteAccountAsync(second, CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddDays(31);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var gate = await setup.Source.OpenConnectionAsync(timeout.Token);
        await using var gateTransaction = await gate.BeginTransactionAsync(timeout.Token);
        await using (var locked = new NpgsqlCommand("SELECT id FROM alliances WHERE id=$1 FOR UPDATE", gate, gateTransaction))
        {
            locked.Parameters.AddWithValue(alliance);
            Assert.Equal(alliance, await locked.ExecuteScalarAsync(timeout.Token));
        }
        await using (var messages = new NpgsqlCommand("SELECT message_id FROM alliance_messages WHERE alliance_id=$1 ORDER BY message_id FOR UPDATE", gate, gateTransaction))
        {
            messages.Parameters.AddWithValue(alliance);
            await messages.ExecuteNonQueryAsync(timeout.Token);
        }
        Task<int> firstPurge = setup.Auth.PurgeExpiredAsync(2, timeout.Token);
        Task<int> secondPurge = setup.Auth.PurgeExpiredAsync(2, timeout.Token);
        await using (var observer = await setup.Source.OpenConnectionAsync(timeout.Token))
        await using (var waiting = new NpgsqlCommand("""
            WITH RECURSIVE blocked(pid) AS (
              SELECT pid FROM pg_stat_activity WHERE $1=ANY(pg_blocking_pids(pid))
              UNION
              SELECT a.pid FROM pg_stat_activity a JOIN blocked b ON b.pid=ANY(pg_blocking_pids(a.pid)))
            SELECT count(*) FROM blocked
            """, observer))
        {
            waiting.Parameters.AddWithValue(gate.ProcessID);
            while ((long)(await waiting.ExecuteScalarAsync(timeout.Token))! < 2)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(10, timeout.Token);
            }
            // Neither purge may own an account while waiting for its alliance row.
            await using var probe = await observer.BeginTransactionAsync(timeout.Token);
            await using var accounts = new NpgsqlCommand("SELECT id FROM accounts WHERE id=ANY($1) ORDER BY id FOR UPDATE NOWAIT", observer, probe);
            accounts.Parameters.AddWithValue(new[] { first, second });
            await accounts.ExecuteNonQueryAsync(timeout.Token);
            await probe.RollbackAsync(timeout.Token);
        }
        await gateTransaction.CommitAsync(timeout.Token);
        int[] purged = await Task.WhenAll(firstPurge, secondPurge).WaitAsync(timeout.Token);
        Assert.Equal(2, purged.Sum());
        await using var verify = setup.Source.CreateCommand("""
            SELECT (SELECT count(*) FROM accounts),
              (SELECT count(*) FROM alliance_messages WHERE author IS NULL AND hidden_by IS NULL AND author_ref IS NOT NULL AND hidden_by_ref IS NOT NULL),
              (SELECT count(*) FROM alliance_reports WHERE author IS NULL AND reporter IS NULL AND author_ref IS NOT NULL AND reporter_ref IS NOT NULL)
            """);
        await using var reader = await verify.ExecuteReaderAsync(timeout.Token);
        Assert.True(await reader.ReadAsync(timeout.Token));
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal(2, reader.GetInt64(1));
        Assert.Equal(2, reader.GetInt64(2));
    }

    [Fact]
    public void PseudonymIsKeyedAndStable()
    {
        var options = new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromSeconds(60), ShippedLimits(), false, new byte[32]);
        var other = options with { PseudonymKey = Enumerable.Repeat((byte)1, 32).ToArray() };
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        string a = new AuthStore(null!, null!, options, TimeProvider.System).Pseudonym(id);
        Assert.Equal(a, new AuthStore(null!, null!, options, TimeProvider.System).Pseudonym(id));
        Assert.NotEqual(a, new AuthStore(null!, null!, other, TimeProvider.System).Pseudonym(id));
        Assert.DoesNotContain(id.ToString("D"), a);
        Assert.StartsWith("p1:", a);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeletionLeavesTheAllianceAndDissolvesASoloOne()
    {
        await using var setup = await CreateAsync();
        var alliances = new AllianceStore(setup.Source, AllianceContent.Load(ContentEstateTests.Root()), setup.Clock);
        await alliances.InitializeAsync(CancellationToken.None);
        using var leaderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var memberKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var leader = await setup.Auth.CreateGuestAsync(leaderKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        var member = await setup.Auth.CreateGuestAsync(memberKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        async Task<long> Version(Guid id)
        {
            await using var q = setup.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
            q.Parameters.AddWithValue(id);
            return (long)(await q.ExecuteScalarAsync())!;
        }
        async Task<SowSiege.Server.Contracts.AccountCommand> Command(Guid id, string op, object payload) =>
            new(Guid.NewGuid(), await Version(id), AllianceStore.Domain, op, System.Text.Json.JsonSerializer.SerializeToElement(payload));
        var created = System.Text.Json.JsonSerializer.Deserialize<AllianceReply>(
            await alliances.ExecuteAsync(leader.AccountId, await Command(leader.AccountId, "create", new { name = "Guard", mode = "open" }), CancellationToken.None), AccountStore.Json)!;
        await alliances.ExecuteAsync(member.AccountId, await Command(member.AccountId, "apply", new { allianceId = created.CreatedId }), CancellationToken.None);
        await setup.Auth.DeleteAccountAsync(member.AccountId, CancellationToken.None);
        var view = await alliances.ViewAsync(leader.AccountId, CancellationToken.None);
        Assert.Equal((1, 1), (view.Alliance!.MemberCount, view.Members.Length));
        await setup.Auth.DeleteAccountAsync(leader.AccountId, CancellationToken.None);
        await using var status = setup.Source.CreateCommand("SELECT status,member_count FROM alliances WHERE id=$1");
        status.Parameters.AddWithValue(created.CreatedId!.Value);
        await using var reader = await status.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(("dissolved", 0), (reader.GetString(0), reader.GetInt32(1)));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [Trait("Category", "Postgres")]
    public async Task DeletedLeaderPrefersOfficersThenOldestJoinAndUuid(bool officers, bool tied, bool allOfficers)
    {
        await using var setup = await CreateAsync();
        var alliances = new AllianceStore(setup.Source, AllianceContent.Load(ContentEstateTests.Root()), setup.Clock);
        await alliances.InitializeAsync(CancellationToken.None);
        var keys = Enumerable.Range(0, 3).Select(_ => ECDsa.Create(ECCurve.NamedCurves.nistP256)).ToArray();
        var ids = new Guid[3];
        for (int i = 0; i < 3; i++)
        {
            ids[i] = (await setup.Auth.CreateGuestAsync(keys[i].ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        }
        async Task<SowSiege.Server.Contracts.AccountCommand> Command(Guid id, string op, object payload)
        {
            await using var q = setup.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
            q.Parameters.AddWithValue(id);
            return new(Guid.NewGuid(), (long)(await q.ExecuteScalarAsync())!, AllianceStore.Domain, op, System.Text.Json.JsonSerializer.SerializeToElement(payload));
        }
        var created = System.Text.Json.JsonSerializer.Deserialize<AllianceReply>(
            await alliances.ExecuteAsync(ids[0], await Command(ids[0], "create", new { name = "Heirs", mode = "open" }), CancellationToken.None), AccountStore.Json)!;
        // ids[2] joins first, so it is the heir even though its account is newer.
        await alliances.ExecuteAsync(ids[2], await Command(ids[2], "apply", new { allianceId = created.CreatedId }), CancellationToken.None);
        setup.Clock.Now = setup.Clock.Now.AddSeconds(5);
        await alliances.ExecuteAsync(ids[1], await Command(ids[1], "apply", new { allianceId = created.CreatedId }), CancellationToken.None);
        if (officers)
        {
            await using var promote = setup.Source.CreateCommand("UPDATE alliance_members SET role='officer' WHERE account_id=ANY($1)");
            promote.Parameters.AddWithValue(allOfficers ? new[] { ids[1], ids[2] } : new[] { ids[1] });
            await promote.ExecuteNonQueryAsync();
        }
        if (tied)
        {
            await using var tie = setup.Source.CreateCommand("UPDATE alliance_members SET joined_ms=$2 WHERE alliance_id=$1");
            tie.Parameters.AddWithValue(created.CreatedId!.Value); tie.Parameters.AddWithValue(setup.Clock.Now.ToUnixTimeMilliseconds());
            await tie.ExecuteNonQueryAsync();
        }
        Guid expected = tied ? ids.Skip(1).OrderBy(id => id.ToString("N"), StringComparer.Ordinal).First() : officers && !allOfficers ? ids[1] : ids[2];
        await setup.Auth.DeleteAccountAsync(ids[0], CancellationToken.None);
        var view = await alliances.ViewAsync(ids[1], CancellationToken.None);
        Assert.Equal(("active", 2), (view.Alliance!.Status, view.Alliance.MemberCount));
        Assert.Single(view.Members, m => m.Role == "leader");
        Assert.Equal("leader", view.Members.Single(m => m.AccountId == expected).Role);
        Assert.Equal(expected, view.Alliance.Leader);
        foreach (var key in keys) { key.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task OnlyP256DeviceKeysAreAccepted()
    {
        await using var setup = await CreateAsync();
        using var rsa = RSA.Create(2048);
        Assert.Equal("key-unsupported", await Code(() => setup.Auth.CreateGuestAsync(rsa.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)));
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Equal("key-unsupported", await Code(() => setup.Auth.CreateGuestAsync(p384.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)));
        Assert.Equal("key-unsupported", await Code(() => setup.Auth.CreateGuestAsync([1, 2, 3], NextIp(), CancellationToken.None)));
    }

    [Fact]
    public void ShippedLimitsAndGuestGateAreFixed()
    {
        Assert.Equal(new AuthLimits(5, 1, 30, 10), ShippedLimits());
        Assert.True(AuthOptions.GuestCreationOpen("Development", "true"));
        Assert.False(AuthOptions.GuestCreationOpen("Production", "true"));
        Assert.False(AuthOptions.GuestCreationOpen("Staging", "true"));
        Assert.False(AuthOptions.GuestCreationOpen("Development", null));
        Assert.False(AuthOptions.GuestCreationOpen("Development", "false"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ReleaseSettingsKeepGuestCreationClosed()
    {
        await using var setup = await CreateAsync(guestOpen: false);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("guest-disabled", await Code(() => setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task GuestAndNonceIssuanceAreRateLimited()
    {
        await using var setup = await CreateAsync();
        var limits = ShippedLimits();
        // Per IP: the (limit + 1)th guest from one address in an hour is refused; another address still works.
        string address = NextIp();
        var keys = new List<ECDsa>();
        for (int i = 0; i < limits.GuestPerIpPerHour; i++)
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); keys.Add(key);
            await setup.Auth.CreateGuestAsync(key.ExportSubjectPublicKeyInfo(), address, CancellationToken.None);
        }
        using var extra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal("rate-limited", await Code(() => setup.Auth.CreateGuestAsync(extra.ExportSubjectPublicKeyInfo(), address, CancellationToken.None)));
        var guest = await setup.Auth.CreateGuestAsync(extra.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        // Per public key: the same key cannot mint a second guest within a day, from any address.
        Assert.Equal("rate-limited", await Code(() => setup.Auth.CreateGuestAsync(extra.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)));
        // Per account: the (limit + 1)th nonce in a minute is refused even from fresh addresses.
        for (int i = 0; i < limits.ChallengePerAccountPerMinute; i++)
        {
            await setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None);
        }
        Assert.Equal("rate-limited", await Code(() => setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None)));
        // Per IP: one address asking for nonces across several accounts is refused at its own limit.
        string shared = NextIp();
        var accounts = new List<Guid>();
        foreach (var key in keys.Take(4))
        {
            accounts.Add((await setup.Auth.CreateGuestAsync(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).KeyId);
        }
        int issued = 0;
        string? refused = null;
        for (int i = 0; i < limits.ChallengePerIpPerMinute + 1 && refused is null; i++)
        {
            try { await setup.Auth.ChallengeAsync(accounts[i % accounts.Count], shared, CancellationToken.None); issued++; }
            catch (AuthException failure) { refused = failure.Message; }
        }
        Assert.Equal((limits.ChallengePerIpPerMinute, "rate-limited"), (issued, refused));
        // The next window opens again.
        setup.Clock.Now = setup.Clock.Now.AddMinutes(1);
        await setup.Auth.ChallengeAsync(accounts[0], shared, CancellationToken.None);
        foreach (var key in keys) { key.Dispose(); }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ExpiredNoncesAndTokensArePrunedOnIssue()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        var stale = await setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None);
        async Task<long> Count(string sql, object value)
        {
            await using var query = setup.Source.CreateCommand(sql);
            query.Parameters.AddWithValue(value);
            return Convert.ToInt64(await query.ExecuteScalarAsync());
        }
        string NonceHash(string nonce) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nonce)));
        string TokenHash(string token) => NonceHash(token);
        Assert.Equal(1L, await Count("SELECT count(*) FROM auth_challenges WHERE nonce_hash=$1", NonceHash(stale.Nonce)));
        // After expiry, the next issue removes the stale nonce and the refresh token past its lifetime.
        setup.Clock.Now = setup.Clock.Now.AddDays(31);
        var other = await setup.Auth.CreateGuestAsync(ECDsa.Create(ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        Assert.Equal(0L, await Count("SELECT count(*) FROM auth_challenges WHERE nonce_hash=$1", NonceHash(stale.Nonce)));
        Assert.Equal(0L, await Count("SELECT count(*) FROM auth_refresh_tokens WHERE token_hash=$1", TokenHash(guest.RefreshToken)));
        Assert.Equal(1L, await Count("SELECT count(*) FROM auth_refresh_tokens WHERE token_hash=$1", TokenHash(other.RefreshToken)));
        // A used nonce is removed on the next issue as well.
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var fresh = await setup.Auth.CreateGuestAsync(signer.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        var used = await setup.Auth.ChallengeAsync(fresh.KeyId, NextIp(), CancellationToken.None);
        await setup.Auth.SignInAsync(fresh.KeyId, used.Nonce, signer.SignData(AuthStore.SignedBytes(fresh.KeyId, used.Nonce), HashAlgorithmName.SHA256), CancellationToken.None);
        await setup.Auth.ChallengeAsync(fresh.KeyId, NextIp(), CancellationToken.None);
        Assert.Equal(0L, await Count("SELECT count(*) FROM auth_challenges WHERE nonce_hash=$1", NonceHash(used.Nonce)));
    }
}
