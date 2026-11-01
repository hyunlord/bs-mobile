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
        var auth = new AuthStore(source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromSeconds(60), ShippedLimits(), guestOpen), clock);
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
    public async Task InAppDeletionRevokesEveryCredential()
    {
        await using var setup = await CreateAsync();
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), NextIp(), CancellationToken.None);
        var other = await SignInAsync(setup, device, guest.KeyId);
        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
        Assert.Null(await setup.Accounts.AuthenticateAsync(guest.AccessToken, CancellationToken.None));
        Assert.Null(await setup.Accounts.AuthenticateAsync(other.AccessToken, CancellationToken.None));
        Assert.Equal("refresh-reused", await Code(() => setup.Auth.RefreshAsync(other.RefreshToken, CancellationToken.None)));
        Assert.Equal("key-invalid", await Code(() => setup.Auth.ChallengeAsync(guest.KeyId, NextIp(), CancellationToken.None)));
        // Deleting twice is harmless.
        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
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
