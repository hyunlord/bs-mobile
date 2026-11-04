using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class SocialAuthHttpTests
{
    private sealed class Identity : ISocialIdentityVerifier
    {
        public Task<VerifiedSocialIdentity> VerifyAsync(string provider, string clientId, string token, string nonce, CancellationToken cancellation)
        {
            // Fixture proofs preserve one subject across challenges while still checking the challenge nonce.
            string[] parts = token.Split(':');
            return parts.Length == 3 && parts[0] == "fixture" && Guid.TryParseExact(parts[1], "N", out _) && parts[2] == nonce
                ? Task.FromResult(new VerifiedSocialIdentity(provider, "http-test-" + parts[1], clientId))
                : Task.FromException<VerifiedSocialIdentity>(new AuthException("social-invalid"));
        }
    }

    private sealed class Factory(bool enabled, string connectionString, TimeProvider? clock = null) : WebApplicationFactory<Program>
    {
        public static async Task<Factory> CreateAsync(bool enabled, TimeProvider? clock = null)
        {
            string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES required.");
            string schema = "social_http_test_" + Guid.NewGuid().ToString("N");
            await using var admin = NpgsqlDataSource.Create(connection);
            await using var create = admin.CreateCommand("CREATE SCHEMA " + schema);
            await create.ExecuteNonQueryAsync();
            // Hosted purge and rate counters must not touch other test classes' accounts.
            return new(enabled, new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString, clock);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = connectionString,
                ["ConnectionStrings:Redis"] = "127.0.0.1:1", ["Domains:SeedKey"] = RecruitTests.TestKeyHex,
                ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex,
                ["Auth:Social:Providers:0:Provider"] = enabled ? "google" : null,
                ["Auth:Social:Providers:0:ClientId"] = enabled ? "test-client" : null,
            }));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ISocialIdentityVerifier, Identity>();
                if (clock is not null) { services.AddSingleton<TimeProvider>(clock); }
            });
        }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task SocialRoutesRejectUnconfiguredProvidersAndUnconfirmedDeletion()
    {
        using var factory = await Factory.CreateAsync(false);
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/v1/auth/social/challenge", new { provider = "google", clientId = "test-client", purpose = "delete" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var refused = await client.PostAsJsonAsync("/v1/auth/social/delete", new
        {
            challengeId = Guid.NewGuid(), nonce = "unused", identityToken = "unused", confirm = "no",
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("confirmation-required", await refused.Content.ReadAsStringAsync());
        var malformed = await client.PostAsJsonAsync("/v1/auth/social/login", new
        {
            challengeId = Guid.NewGuid(), nonce = (string?)null, identityToken = "unused",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, malformed.StatusCode);
        var guidance = await client.GetAsync("/v1/account/deletion");
        Assert.Equal(HttpStatusCode.OK, guidance.StatusCode);
        Assert.Contains("/v1/account/deletion/requests", await guidance.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task LinkingRequiresBearerAndConsumesProofThroughHttp()
    {
        using var factory = await Factory.CreateAsync(true);
        using var client = factory.CreateClient();
        var request = new { provider = "google", clientId = "test-client", purpose = "link" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/social/challenge", request)).StatusCode);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var guestResponse = await client.PostAsJsonAsync("/v1/auth/guest", new { publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) });
        guestResponse.EnsureSuccessStatusCode();
        var guest = (await guestResponse.Content.ReadFromJsonAsync<AuthTokens>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", guest.AccessToken);
        var challengeResponse = await client.PostAsJsonAsync("/v1/auth/social/challenge", request);
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<SocialChallenge>())!;
        var proof = new { challenge.ChallengeId, challenge.Nonce, identityToken = FixtureToken(Guid.NewGuid(), challenge) };
        var linked = await client.PostAsJsonAsync("/v1/auth/social/link", proof);
        linked.EnsureSuccessStatusCode();
        Assert.True((await linked.Content.ReadFromJsonAsync<SocialAuthReply>())!.Linked);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/social/link", proof)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/auth/social/challenge", new
        {
            provider = "google", clientId = "test-client", purpose = "login", publicKey = "not-base64",
        })).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task GoogleFixtureLinksLogsInOnNewDeviceAndDeletesThroughWebProof()
    {
        using var factory = await Factory.CreateAsync(true);
        using var client = factory.CreateClient();
        using var originalKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newDeviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Guid subject = Guid.NewGuid();
        var guestResponse = await client.PostAsJsonAsync("/v1/auth/guest", new
        {
            publicKey = Convert.ToBase64String(originalKey.ExportSubjectPublicKeyInfo()),
        });
        guestResponse.EnsureSuccessStatusCode();
        var guest = (await guestResponse.Content.ReadFromJsonAsync<AuthTokens>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", guest.AccessToken);

        var linkChallenge = await Challenge(client, "link");
        var linked = await client.PostAsJsonAsync("/v1/auth/social/link", new
        {
            linkChallenge.ChallengeId, linkChallenge.Nonce, identityToken = FixtureToken(subject, linkChallenge),
        });
        linked.EnsureSuccessStatusCode();
        Assert.True((await linked.Content.ReadFromJsonAsync<SocialAuthReply>())!.Linked);

        client.DefaultRequestHeaders.Authorization = null;
        var loginChallenge = await Challenge(client, "login", newDeviceKey.ExportSubjectPublicKeyInfo());
        byte[] signature = newDeviceKey.SignData(SocialAuthService.SignedBytes(loginChallenge.ChallengeId, loginChallenge.Nonce),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var login = await client.PostAsJsonAsync("/v1/auth/social/login", new
        {
            loginChallenge.ChallengeId, loginChallenge.Nonce, identityToken = FixtureToken(subject, loginChallenge),
            signature = Convert.ToBase64String(signature),
        });
        login.EnsureSuccessStatusCode();
        var recovered = (await login.Content.ReadFromJsonAsync<SocialAuthReply>())!;
        Assert.False(recovered.Linked);
        Assert.NotNull(recovered.Tokens);
        Assert.Equal(guest.AccountId, recovered.Tokens.AccountId);
        Assert.NotEqual(guest.KeyId, recovered.Tokens.KeyId);
        client.DefaultRequestHeaders.Authorization = new("Bearer", recovered.Tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/estate")).StatusCode);

        // The web deletion path authenticates with a fresh provider proof, without an app bearer session.
        client.DefaultRequestHeaders.Authorization = null;
        var deletionChallenge = await Challenge(client, "delete");
        var refused = await client.PostAsJsonAsync("/v1/auth/social/delete", new
        {
            deletionChallenge.ChallengeId, deletionChallenge.Nonce, identityToken = FixtureToken(subject, deletionChallenge), confirm = "no",
        });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("confirmation-required", await refused.Content.ReadAsStringAsync());
        var deleted = await client.PostAsJsonAsync("/v1/auth/social/delete", new
        {
            deletionChallenge.ChallengeId, deletionChallenge.Nonce, identityToken = FixtureToken(subject, deletionChallenge), confirm = "delete",
        });
        deleted.EnsureSuccessStatusCode();
        var deletion = (await deleted.Content.ReadFromJsonAsync<SocialAuthReply>())!;
        Assert.NotNull(deletion.PurgeAt);
        Assert.Null(deletion.Tokens);
        foreach (string access in new[] { guest.AccessToken, recovered.Tokens.AccessToken })
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", access);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
        }
    }

    private static string FixtureToken(Guid subject, SocialChallenge challenge) => "fixture:" + subject.ToString("N") + ":" + challenge.Nonce;

    private static async Task<SocialChallenge> Challenge(HttpClient client, string purpose, byte[]? publicKey = null)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/social/challenge", new
        {
            provider = "google", clientId = "test-client", purpose,
            publicKey = publicKey is null ? null : Convert.ToBase64String(publicKey),
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SocialChallenge>())!;
    }
}
