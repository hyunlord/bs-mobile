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

    private static async Task<Setup> CreateAsync(bool guestOpen = true)
    {
        var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required."));
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
        var alliances = new AllianceStore(setup.Source, AllianceContent.Load(ContentEstateTests.Root()), setup.Clock, setup.Accounts);
        await alliances.InitializeAsync(CancellationToken.None);
        using var goneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stayKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var gone = (await setup.Auth.CreateGuestAsync(goneKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
        var stay = (await setup.Auth.CreateGuestAsync(stayKey.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None)).AccountId;
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
        var goneMessage = Guid.NewGuid();
        await Act(gone, "chat", new { messageId = goneMessage, text = "hello" });
        await Act(stay, "chat", new { messageId = Guid.NewGuid(), text = "hi" });
        await Act(stay, "report", new { messageId = goneMessage, reason = "spam-ad" });
        await Act(gone, "block", new { accountId = stay });
        await Act(stay, "request-help", new { nodeId = "x" }); // may be refused without a research job; the purge must not depend on it

        await setup.Auth.DeleteAccountAsync(gone, CancellationToken.None);
        var view = await alliances.ViewAsync(stay, CancellationToken.None);
        Assert.Equal(stay, view.Alliance!.Leader);
        Assert.Null(view.Chat.Single(m => m.MessageId == goneMessage).Author); // shown as "삭제된 사용자"
        setup.Clock.Now = setup.Clock.Now.AddDays(30).AddSeconds(-1);
        await setup.Auth.PurgeExpiredAsync(1000, CancellationToken.None);
        Assert.Equal(1, await Count("SELECT count(*) FROM accounts WHERE id=$1", gone));

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
                if (await Count(sql, gone) > 0) { found.Add(table + "." + column); }
            }
            Assert.Empty(found);
        }
        // Retained: chat body (200-message rule) and the report snapshot (180 days), under the pseudonym.
        string pseudonym = setup.Auth.Pseudonym(gone);
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

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeletedLeaderPassesTheAllianceToTheLongestStandingMember()
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
        await setup.Auth.DeleteAccountAsync(ids[0], CancellationToken.None);
        var view = await alliances.ViewAsync(ids[1], CancellationToken.None);
        Assert.Equal(("active", 2), (view.Alliance!.Status, view.Alliance.MemberCount));
        Assert.Equal("leader", view.Members.Single(m => m.AccountId == ids[2]).Role);
        Assert.Equal("member", view.Members.Single(m => m.AccountId == ids[1]).Role);
        Assert.Equal(ids[2], view.Alliance.Leader);
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
