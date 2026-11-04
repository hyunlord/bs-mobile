using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class SocialAuthHttpTests
{
    private const string WebChallenges = "/v1/account/deletion/challenge";
    private const string WebRequests = "/v1/account/deletion/requests";

    private sealed class WebClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks / 10 * 10;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task WebDeletionSelectsLinkedSubjectRegardlessOfOtherBearerAndNeedsNoAppSession()
    {
        var clock = new WebClock();
        using var factory = await Factory.CreateAsync(true, clock);
        using var client = factory.CreateClient();
        Guid firstSubject = Guid.NewGuid(), secondSubject = Guid.NewGuid();
        var first = await WebGuest(client, firstSubject);
        var second = await WebGuest(client, secondSubject);
        // The bearer belongs to the second account; only the first subject's linked account may be deleted.
        var firstProof = await WebChallenge(client);
        var response = await client.PostAsJsonAsync(WebRequests, WebProof(firstProof, firstSubject));
        await AssertWebDeletionReply(response, clock.GetUtcNow().AddDays(30));
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/estate")).StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var secondProof = await WebChallenge(client);
        await AssertWebDeletionReply(await client.PostAsJsonAsync(WebRequests, WebProof(secondProof, secondSubject)), clock.GetUtcNow().AddDays(30));
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task WebDeletionRequiresConfirmationWithoutConsumingProofAndRejectsReplay()
    {
        var clock = new WebClock();
        using var factory = await Factory.CreateAsync(true, clock);
        using var client = factory.CreateClient();
        Guid subject = Guid.NewGuid();
        var guest = await WebGuest(client, subject);
        var proof = await WebChallenge(client);
        var unconfirmed = WebProof(proof, subject);
        unconfirmed["confirm"] = "no";
        var refused = await client.PostAsJsonAsync(WebRequests, unconfirmed);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("confirmation-required", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/estate")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        await AssertWebDeletionReply(await client.PostAsJsonAsync(WebRequests, WebProof(proof, subject)), clock.GetUtcNow().AddDays(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, WebProof(proof, subject))).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", guest.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task WebDeletionRejectsWrongPurposeExpiredAndNullNonceWithoutDeletingAccount()
    {
        var clock = new WebClock();
        using var factory = await Factory.CreateAsync(true, clock);
        using var client = factory.CreateClient();
        Guid subject = Guid.NewGuid();
        await WebGuest(client, subject);
        var linkProof = await Challenge(client, "link");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, WebProof(linkProof, subject))).StatusCode);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var loginProof = await Challenge(client, "login", device.ExportSubjectPublicKeyInfo());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, WebProof(loginProof, subject))).StatusCode);
        var expired = await WebChallenge(client);
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, WebProof(expired, subject))).StatusCode);
        var current = await WebChallenge(client);
        var nullNonce = WebProof(current, subject);
        nullNonce["nonce"] = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, nullNonce)).StatusCode);
        var wrongNonce = WebProof(current, subject);
        wrongNonce["nonce"] = new string('0', 64);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, wrongNonce)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/estate")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task WebDeletionRejectsAccountSelectionAndPurposeOrDeviceFieldsAtHttpBoundary()
    {
        using var factory = await Factory.CreateAsync(true);
        using var client = factory.CreateClient();
        Guid subject = Guid.NewGuid();
        await WebGuest(client, subject);
        var proof = await WebChallenge(client);
        foreach (string field in new[] { "accountId", "email", "purpose", "publicKey", "signature" })
        {
            var challengeBody = new Dictionary<string, object?> { ["provider"] = "google", ["clientId"] = "test-client", [field] = "forged" };
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(WebChallenges, challengeBody)).StatusCode);
            var requestBody = WebProof(proof, subject);
            requestBody[field] = "forged";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(WebRequests, requestBody)).StatusCode);
        }
        // Rejected JSON never reaches proof consumption or deletion.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/estate")).StatusCode);
        (await client.PostAsJsonAsync(WebRequests, WebProof(proof, subject))).EnsureSuccessStatusCode();
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task WebDeletionRejectsUnlinkedGuestProofAndDisabledProvider()
    {
        using var factory = await Factory.CreateAsync(true);
        using var client = factory.CreateClient();
        await WebGuest(client);
        var proof = await WebChallenge(client);
        // Even a valid fixture identity plus a guest bearer cannot select an unlinked account.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(WebRequests, WebProof(proof, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/estate")).StatusCode);
        using var disabledFactory = await Factory.CreateAsync(false);
        using var disabled = disabledFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await disabled.PostAsJsonAsync(WebChallenges, new { provider = "google", clientId = "test-client" })).StatusCode);
    }

    private static async Task<AuthTokens> WebGuest(HttpClient client, Guid? subject = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var response = await client.PostAsJsonAsync("/v1/auth/guest", new { publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) });
        response.EnsureSuccessStatusCode();
        var guest = (await response.Content.ReadFromJsonAsync<AuthTokens>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", guest.AccessToken);
        if (subject is { } linkedSubject)
        {
            var challenge = await Challenge(client, "link");
            var linked = await client.PostAsJsonAsync("/v1/auth/social/link", new
            {
                challenge.ChallengeId, challenge.Nonce, identityToken = FixtureToken(linkedSubject, challenge),
            });
            linked.EnsureSuccessStatusCode();
            Assert.True((await linked.Content.ReadFromJsonAsync<SocialAuthReply>())!.Linked);
        }
        return guest;
    }

    private static async Task<SocialChallenge> WebChallenge(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(WebChallenges, new { provider = "google", clientId = "test-client" });
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore is true);
        return (await response.Content.ReadFromJsonAsync<SocialChallenge>())!;
    }

    private static Dictionary<string, object?> WebProof(SocialChallenge proof, Guid subject) => new()
    {
        ["challengeId"] = proof.ChallengeId, ["nonce"] = proof.Nonce,
        ["identityToken"] = FixtureToken(subject, proof), ["confirm"] = "delete",
    };

    private static async Task AssertWebDeletionReply(HttpResponseMessage response, DateTimeOffset purgeAt)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore is true);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("purgeAt", Assert.Single(body.EnumerateObject()).Name);
        Assert.Equal(purgeAt, body.GetProperty("purgeAt").GetDateTimeOffset());
    }
}
