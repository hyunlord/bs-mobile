using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldAuthRecoveryConnectionTests
{
    [Fact, Trait("Category", "Postgres")]
    public async Task SingleConnectionSignInCommitsRejectedSignatureNonceBeforeThrowing()
    {
        await using var database = await WorldTestDatabase.CreateAsync(1);
        using var factory = new WorldHttpTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient();
        var auth = factory.Services.GetRequiredService<AuthStore>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guest = await auth.CreateGuestAsync(device.ExportSubjectPublicKeyInfo(), "192.0.2.71", ct);
        var challenge = await auth.ChallengeAsync(guest.KeyId, "192.0.2.71", ct);
        var wrong = stranger.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        var rejected = await Assert.ThrowsAsync<AuthException>(() => auth.SignInAsync(guest.KeyId, challenge.Nonce, wrong, ct));
        Assert.Equal("signature-invalid", rejected.Message);
        var correct = device.SignData(AuthStore.SignedBytes(guest.KeyId, challenge.Nonce), HashAlgorithmName.SHA256);
        var replayed = await Assert.ThrowsAsync<AuthException>(() => auth.SignInAsync(guest.KeyId, challenge.Nonce, correct, ct));
        Assert.Equal("challenge-invalid", replayed.Message);
        var fresh = await auth.ChallengeAsync(guest.KeyId, "192.0.2.71", ct);
        var signedIn = await auth.SignInAsync(guest.KeyId, fresh.Nonce,
            device.SignData(AuthStore.SignedBytes(guest.KeyId, fresh.Nonce), HashAlgorithmName.SHA256), ct);
        Assert.Equal(guest.AccountId, signedIn.AccountId);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task SingleConnectionDeletionAndPurgeReleaseEveryAdmissionTransaction()
    {
        await using var database = await WorldTestDatabase.CreateAsync(1);
        using var factory = new WorldHttpTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient();
        var auth = factory.Services.GetRequiredService<AuthStore>();
        var accounts = factory.Services.GetRequiredService<AccountStore>();
        var source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        var options = factory.Services.GetRequiredService<AuthOptions>();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var created = await accounts.CreateAsync(ct);
        var id = (await accounts.AuthenticateAsync(created.Token, ct))!.Value;
        await auth.DeleteAccountAsync(id, ct);
        Assert.Null(await accounts.AuthenticateAsync(created.Token, ct));
        Assert.Equal(0, await auth.PurgeExpiredAsync(1, ct));
        await using (var expire = source.CreateCommand("UPDATE auth_deleted_accounts SET deleted_at=$2 WHERE account_id=$1"))
        {
            expire.Parameters.AddWithValue(id);
            expire.Parameters.AddWithValue(DateTimeOffset.UtcNow.Subtract(options.DeletionGrace).AddMinutes(-1));
            Assert.Equal(1, await expire.ExecuteNonQueryAsync(ct));
        }
        await auth.PurgeExpiredAsync(1, ct);
        await using var count = source.CreateCommand("SELECT count(*) FROM accounts WHERE id=$1");
        count.Parameters.AddWithValue(id);
        Assert.Equal(0L, await count.ExecuteScalarAsync(ct));
        Assert.Equal(0, await auth.PurgeExpiredAsync(1, ct));
    }
}
