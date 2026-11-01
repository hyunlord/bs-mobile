using System.Security.Cryptography;
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

    private static async Task<Setup> CreateAsync()
    {
        var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required."));
        var clock = new ManualClock();
        var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
        await accounts.InitializeAsync(CancellationToken.None);
        var auth = new AuthStore(source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromSeconds(60)), clock);
        await auth.InitializeAsync(CancellationToken.None);
        return new(source, accounts, auth, clock);
    }

    private static async Task<AuthTokens> SignInAsync(Setup setup, ECDsa device, Guid keyId)
    {
        var challenge = await setup.Auth.ChallengeAsync(keyId, CancellationToken.None);
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
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), CancellationToken.None);
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
        var challenge = await setup.Auth.ChallengeAsync(guest.KeyId, CancellationToken.None);
        var good = device.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        await setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, good, CancellationToken.None);
        Assert.Equal("challenge-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, challenge.Nonce, good, CancellationToken.None)));
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = await setup.Auth.ChallengeAsync(guest.KeyId, CancellationToken.None);
        var wrong = stranger.SignData(AuthStore.SignedBytes(guest.KeyId, forged.Nonce), HashAlgorithmName.SHA256);
        Assert.Equal("signature-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, forged.Nonce, wrong, CancellationToken.None)));
        var right = device.SignData(AuthStore.SignedBytes(guest.KeyId, forged.Nonce), HashAlgorithmName.SHA256);
        Assert.Equal("challenge-invalid", await Code(() => setup.Auth.SignInAsync(guest.KeyId, forged.Nonce, right, CancellationToken.None)));
        var late = await setup.Auth.ChallengeAsync(guest.KeyId, CancellationToken.None);
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
        var first = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), CancellationToken.None);
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
        var guest = await setup.Auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), CancellationToken.None);
        var other = await SignInAsync(setup, device, guest.KeyId);
        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
        Assert.Null(await setup.Accounts.AuthenticateAsync(guest.AccessToken, CancellationToken.None));
        Assert.Null(await setup.Accounts.AuthenticateAsync(other.AccessToken, CancellationToken.None));
        Assert.Equal("refresh-reused", await Code(() => setup.Auth.RefreshAsync(other.RefreshToken, CancellationToken.None)));
        Assert.Equal("key-invalid", await Code(() => setup.Auth.ChallengeAsync(guest.KeyId, CancellationToken.None)));
        // Deleting twice is harmless.
        await setup.Auth.DeleteAccountAsync(guest.AccountId, CancellationToken.None);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task OnlyP256DeviceKeysAreAccepted()
    {
        await using var setup = await CreateAsync();
        using var rsa = RSA.Create(2048);
        Assert.Equal("key-unsupported", await Code(() => setup.Auth.CreateGuestAsync(rsa.ExportSubjectPublicKeyInfo(), CancellationToken.None)));
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Equal("key-unsupported", await Code(() => setup.Auth.CreateGuestAsync(p384.ExportSubjectPublicKeyInfo(), CancellationToken.None)));
        Assert.Equal("key-unsupported", await Code(() => setup.Auth.CreateGuestAsync([1, 2, 3], CancellationToken.None)));
    }
}
