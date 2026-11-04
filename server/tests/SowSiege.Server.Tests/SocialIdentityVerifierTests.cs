using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class SocialIdentityVerifierTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class KeysHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int Requests { get; private set; }
        public Uri? LastUri { get; private set; }
        public string CacheControl { get; set; } = "max-age=300";
        public Action? OnRequest { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            LastUri = request.RequestUri;
            OnRequest?.Invoke();
            var response = new HttpResponseMessage(Status) { Content = new StringContent(Body) };
            response.Headers.CacheControl = CacheControlHeaderValue.Parse(CacheControl);
            return Task.FromResult(response);
        }
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string Keys(RSA key, string kid = "key-1")
    {
        var p = key.ExportParameters(false);
        return JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid, n = B64(p.Modulus!), e = B64(p.Exponent!) } } });
    }

    private static Dictionary<string, object> Claims(Clock clock, string provider = "google") => new()
    {
        ["iss"] = provider == "google" ? "https://accounts.google.com" : "https://appleid.apple.com",
        ["aud"] = "server-client", ["sub"] = "provider-user", ["nonce"] = "server-nonce",
        ["iat"] = clock.Now.ToUnixTimeSeconds(), ["exp"] = clock.Now.AddMinutes(5).ToUnixTimeSeconds(),
    };

    private static string Token(RSA key, Dictionary<string, object> claims, string kid = "key-1", string alg = "RS256") =>
        Token(key, JsonSerializer.Serialize(claims), JsonSerializer.Serialize(new { alg, kid }));

    private static string Token(RSA key, string claims, string header)
    {
        string signed = B64(Encoding.UTF8.GetBytes(header)) + "." + B64(Encoding.UTF8.GetBytes(claims));
        return signed + "." + B64(key.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    private static SocialIdentityVerifier Verifier(HttpClient http, Clock clock) => new(http, clock,
        [new("google", "server-client", ["android-client"]), new("google", "other-client", []), new("apple", "server-client", [])]);

    private static async Task<string> Code(Func<Task> action) => (await Assert.ThrowsAsync<AuthException>(action)).Message;

    [Theory]
    [InlineData("google", "https://www.googleapis.com/oauth2/v3/certs")]
    [InlineData("apple", "https://appleid.apple.com/auth/keys")]
    public async Task SignedIdentityUsesOnlyConfiguredProviderKeysAndCachesThem(string provider, string endpoint)
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        var handler = new KeysHandler { Body = Keys(key) };
        using var http = new HttpClient(handler);
        var verifier = Verifier(http, clock);
        string token = Token(key, Claims(clock, provider));
        var identity = await verifier.VerifyAsync(provider, "server-client", token, "server-nonce", CancellationToken.None);
        Assert.Equal(new VerifiedSocialIdentity(provider, "provider-user", "server-client"), identity);
        await verifier.VerifyAsync(provider, "server-client", token, "server-nonce", CancellationToken.None);
        string untrustedLocation = Token(key, JsonSerializer.Serialize(Claims(clock, provider)),
            "{\"alg\":\"RS256\",\"kid\":\"key-1\",\"jku\":\"https://attacker.invalid/keys\"}");
        await verifier.VerifyAsync(provider, "server-client", untrustedLocation, "server-nonce", CancellationToken.None);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(endpoint, handler.LastUri!.AbsoluteUri);
        Assert.Equal("social-disabled", await Code(() => verifier.VerifyAsync(provider, "unconfigured", token, "server-nonce", CancellationToken.None)));
    }

    [Theory]
    [InlineData("iss", "https://attacker.invalid")]
    [InlineData("aud", "attacker-client")]
    [InlineData("sub", "")]
    [InlineData("nonce", "wrong-nonce")]
    [InlineData("exp", "not-an-integer")]
    [InlineData("iat", "not-an-integer")]
    [InlineData("azp", "attacker-client")]
    public async Task RejectsInvalidSignedClaims(string name, string value)
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        using var http = new HttpClient(new KeysHandler { Body = Keys(key) });
        var claims = Claims(clock);
        claims[name] = value;
        Assert.Equal("social-invalid", await Code(() => Verifier(http, clock).VerifyAsync("google", "server-client", Token(key, claims), "server-nonce", CancellationToken.None)));
    }

    [Fact]
    public async Task RejectsExpiredFutureAndMissingClaimsAndUntrustedAudienceArrays()
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        using var http = new HttpClient(new KeysHandler { Body = Keys(key) });
        var verifier = Verifier(http, clock);
        var cases = new List<Dictionary<string, object>>();
        var expired = Claims(clock); expired["exp"] = clock.Now.ToUnixTimeSeconds(); cases.Add(expired);
        var future = Claims(clock); future["iat"] = clock.Now.AddMinutes(2).ToUnixTimeSeconds(); cases.Add(future);
        var missing = Claims(clock); missing.Remove("nonce"); cases.Add(missing);
        var wrongAudience = Claims(clock); wrongAudience["aud"] = new[] { "server-client", "attacker" }; cases.Add(wrongAudience);
        var missingPresenter = Claims(clock); missingPresenter["aud"] = new[] { "server-client", "other-client" }; cases.Add(missingPresenter);
        foreach (var claims in cases)
        {
            Assert.Equal("social-invalid", await Code(() => verifier.VerifyAsync("google", "server-client", Token(key, claims), "server-nonce", CancellationToken.None)));
        }
        var hybrid = Claims(clock); hybrid["azp"] = "android-client";
        await verifier.VerifyAsync("google", "server-client", Token(key, hybrid), "server-nonce", CancellationToken.None);
        hybrid["aud"] = new[] { "server-client", "other-client" };
        await verifier.VerifyAsync("google", "server-client", Token(key, hybrid), "server-nonce", CancellationToken.None);
    }

    [Fact]
    public async Task RejectsDuplicatePropertiesAlgorithmsMalformedTokensAndForgedSignatures()
    {
        using var key = RSA.Create(2048);
        using var attacker = RSA.Create(2048);
        var clock = new Clock();
        using var http = new HttpClient(new KeysHandler { Body = Keys(key) });
        var verifier = Verifier(http, clock);
        string claims = JsonSerializer.Serialize(Claims(clock));
        string[] tokens =
        [
            Token(attacker, Claims(clock)), Token(key, Claims(clock), alg: "HS256"),
            Token(key, claims, "{\"alg\":\"RS256\",\"alg\":\"RS256\",\"kid\":\"key-1\"}"),
            Token(key, claims[..^1] + ",\"sub\":\"other\"}", "{\"alg\":\"RS256\",\"kid\":\"key-1\"}"),
            "not.a.jwt", "a.b.c.d", new string('a', 20_000),
        ];
        foreach (string token in tokens)
        {
            Assert.Equal("social-invalid", await Code(() => verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None)));
        }
    }

    [Fact]
    public async Task RotationRefreshesUnknownKeysOnlyAfterCooldownAndOutagesFailClosed()
    {
        using var first = RSA.Create(2048);
        using var next = RSA.Create(2048);
        var clock = new Clock();
        var handler = new KeysHandler { Body = Keys(first) };
        using var http = new HttpClient(handler);
        var verifier = Verifier(http, clock);
        await verifier.VerifyAsync("google", "server-client", Token(first, Claims(clock)), "server-nonce", CancellationToken.None);
        handler.Body = Keys(next, "key-2");
        string rotated = Token(next, Claims(clock), "key-2");
        Assert.Equal("social-invalid", await Code(() => verifier.VerifyAsync("google", "server-client", rotated, "server-nonce", CancellationToken.None)));
        Assert.Equal(1, handler.Requests);
        clock.Now = clock.Now.AddSeconds(31);
        await verifier.VerifyAsync("google", "server-client", rotated, "server-nonce", CancellationToken.None);
        Assert.Equal(2, handler.Requests);
        clock.Now = clock.Now.AddMinutes(6);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        string fresh = Token(next, Claims(clock), "key-2");
        Assert.Equal("social-unavailable", await Code(() => verifier.VerifyAsync("google", "server-client", fresh, "server-nonce", CancellationToken.None)));
        Assert.Equal("social-unavailable", await Code(() => verifier.VerifyAsync("google", "server-client", fresh, "server-nonce", CancellationToken.None)));
        Assert.Equal(3, handler.Requests);
    }

    [Theory]
    [InlineData("{\"keys\":[],\"keys\":[]}")]
    [InlineData("{\"keys\":[]}")]
    [InlineData("not-json")]
    public async Task MalformedProviderResponsesAreUnavailable(string body)
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        using var http = new HttpClient(new KeysHandler { Body = body });
        Assert.Equal("social-unavailable", await Code(() => Verifier(http, clock).VerifyAsync("google", "server-client", Token(key, Claims(clock)), "server-nonce", CancellationToken.None)));
    }

    [Fact]
    public async Task TokenExpiringDuringKeyFetchIsRejectedAfterSignatureVerification()
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        var claims = Claims(clock);
        claims["exp"] = clock.Now.AddSeconds(1).ToUnixTimeSeconds();
        var handler = new KeysHandler { Body = Keys(key), OnRequest = () => clock.Now = clock.Now.AddSeconds(2) };
        using var http = new HttpClient(handler);
        Assert.Equal("social-invalid", await Code(() => Verifier(http, clock).VerifyAsync("google", "server-client", Token(key, claims), "server-nonce", CancellationToken.None)));
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("no-store", 0)]
    [InlineData("no-cache", 0)]
    [InlineData("max-age=0", 0)]
    [InlineData("max-age=1", 2)]
    public async Task KnownKeysRevalidateWhenCacheExpiresDespiteUnknownKeyCooldown(string cacheControl, int elapsedSeconds)
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        var handler = new KeysHandler { Body = Keys(key), CacheControl = cacheControl };
        using var http = new HttpClient(handler);
        var verifier = Verifier(http, clock);
        string token = Token(key, Claims(clock));
        await verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(elapsedSeconds);
        await verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None);
        Assert.Equal(2, handler.Requests);
        string unknown = Token(key, Claims(clock), "unknown");
        Assert.Equal("social-invalid", await Code(() => verifier.VerifyAsync("google", "server-client", unknown, "server-nonce", CancellationToken.None)));
        Assert.Equal(2, handler.Requests);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        clock.Now = clock.Now.AddSeconds(elapsedSeconds);
        Assert.Equal("social-unavailable", await Code(() => verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None)));
        Assert.Equal("social-unavailable", await Code(() => verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None)));
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task UncacheableKeysStillBoundProviderTrafficWithoutLabellingValidTokensInvalid()
    {
        using var key = RSA.Create(2048);
        var clock = new Clock();
        var handler = new KeysHandler { Body = Keys(key), CacheControl = "no-store" };
        using var http = new HttpClient(handler);
        var verifier = Verifier(http, clock);
        string token = Token(key, Claims(clock));
        for (int i = 0; i < 8; i++)
        {
            await verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None);
        }
        Assert.Equal("social-unavailable", await Code(() => verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None)));
        Assert.Equal(8, handler.Requests);
        clock.Now = clock.Now.AddSeconds(1);
        await verifier.VerifyAsync("google", "server-client", token, "server-nonce", CancellationToken.None);
        Assert.Equal(9, handler.Requests);
    }
}
