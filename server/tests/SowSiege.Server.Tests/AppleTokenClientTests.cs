using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class AppleTokenClientTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 6, 0, 0, TimeSpan.Zero);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static AppleClientOptions Options(ECDsa key) => new("TESTTEAM01", "TESTKEY001", key.ExportPkcs8PrivateKeyPem());

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.invalid/apple/callback")]
    public async Task CodeExchangeUsesPinnedEndpointAndSignedClientSecret(string? redirect)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Options(key);
        var clock = new FixedClock();
        using var handler = new Handler(async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://appleid.apple.com/auth/token", request.RequestUri!.AbsoluteUri);
            Assert.Equal("application/x-www-form-urlencoded", request.Content!.Headers.ContentType!.MediaType);
            var form = Form(await request.Content.ReadAsStringAsync(cancellation));
            Assert.Equal("com.test.game", form["client_id"]);
            Assert.Equal("one+use&code", form["code"]);
            Assert.Equal("authorization_code", form["grant_type"]);
            Assert.Equal(redirect is null ? 4 : 5, form.Count);
            if (redirect is null) { Assert.False(form.ContainsKey("redirect_uri")); }
            else { Assert.Equal(redirect, form["redirect_uri"]); }
            VerifySecret(form["client_secret"], key, clock, options);
            return Response(HttpStatusCode.OK, "{\"id_token\":\"identity\",\"refresh_token\":\"refresh\",\"token_type\":\"Bearer\"}");
        });
        using var http = new HttpClient(handler);
        var result = await new AppleTokenClient(http, clock, options).ExchangeAsync("com.test.game", "one+use&code", redirect, CancellationToken.None);
        Assert.Equal("identity", result.IdentityToken);
        Assert.Equal("refresh", result.RefreshToken);
    }

    [Fact]
    public async Task RevocationUsesRefreshHintAndSignedClientSecret()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var clock = new FixedClock();
        var options = Options(key);
        using var handler = new Handler(async (request, cancellation) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://appleid.apple.com/auth/revoke", request.RequestUri!.AbsoluteUri);
            var form = Form(await request.Content!.ReadAsStringAsync(cancellation));
            Assert.Equal("com.test.game", form["client_id"]);
            Assert.Equal("private+refresh&token", form["token"]);
            Assert.Equal("refresh_token", form["token_type_hint"]);
            Assert.Equal(4, form.Count);
            VerifySecret(form["client_secret"], key, clock, options);
            return Response(HttpStatusCode.OK, "");
        });
        using var http = new HttpClient(handler);
        await new AppleTokenClient(http, clock, options).RevokeAsync("com.test.game", "private+refresh&token", CancellationToken.None);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id_token\":\"identity\"}")]
    [InlineData("{\"id_token\":\"identity\",\"refresh_token\":null}")]
    [InlineData("{\"id_token\":\"identity\",\"refresh_token\":\" \"}")]
    [InlineData("{\"id_token\":42,\"refresh_token\":\"refresh\"}")]
    [InlineData("{\"id_token\":\"first\",\"id_token\":\"second\",\"refresh_token\":\"refresh\"}")]
    [InlineData("{\"id_token\":\"identity\",\"refresh_token\":\"refresh\",\"extra\":1,\"extra\":2}")]
    [InlineData("{\"id_token\":\"identity\",\"refresh_token\":\"refresh\",\"error\":\"invalid_grant\"}")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public async Task InvalidExchangeResponsesNeverBecomeCredentials(string body)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, body)));
        using var http = new HttpClient(handler);
        var failure = await Assert.ThrowsAsync<AuthException>(() => new AppleTokenClient(http, new FixedClock(), Options(key))
            .ExchangeAsync("com.test.game", "private-code", null, CancellationToken.None));
        Assert.Equal("social-unavailable", failure.Message);
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task Non200ResponsesAreFailuresWithoutCredentialDisclosure(HttpStatusCode status)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var handler = new Handler((_, _) => Task.FromResult(Response(status, "private-refresh private-code")));
        using var http = new HttpClient(handler);
        var client = new AppleTokenClient(http, new FixedClock(), Options(key));
        var exchange = await Assert.ThrowsAsync<AuthException>(() => client.ExchangeAsync("com.test.game", "private-code", null, CancellationToken.None));
        var revoke = await Assert.ThrowsAsync<AuthException>(() => client.RevokeAsync("com.test.game", "private-refresh", CancellationToken.None));
        Assert.Equal("social-unavailable", exchange.Message);
        Assert.Equal("social-unavailable", revoke.Message);
        Assert.Null(exchange.InnerException);
        Assert.Null(revoke.InnerException);
    }

    [Fact]
    public async Task OversizedResponsesAreRejectedWithAndWithoutLengthHeaders()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        foreach (bool knownLength in new[] { true, false })
        {
            using var handler = new Handler((_, _) =>
            {
                var response = Response(HttpStatusCode.OK, new string('x', 32 * 1024 + 1));
                if (!knownLength)
                {
                    response.Content = new StreamContent(new NonSeekableStream(new byte[32 * 1024 + 1]));
                }
                return Task.FromResult(response);
            });
            using var http = new HttpClient(handler);
            var client = new AppleTokenClient(http, new FixedClock(), Options(key));
            Assert.Equal("social-unavailable", (await Assert.ThrowsAsync<AuthException>(() => client.ExchangeAsync("com.test.game", "code", null, CancellationToken.None))).Message);
            Assert.Equal("social-unavailable", (await Assert.ThrowsAsync<AuthException>(() => client.RevokeAsync("com.test.game", "refresh", CancellationToken.None))).Message);
        }
    }

    [Fact]
    public async Task TransportErrorsAndTimeoutsNeverExposeCredentials()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        foreach (Exception error in new Exception[] { new HttpRequestException("private-code private-refresh"), new TaskCanceledException("private-code") })
        {
            using var handler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(error));
            using var http = new HttpClient(handler);
            var client = new AppleTokenClient(http, new FixedClock(), Options(key));
            var failure = await Assert.ThrowsAsync<AuthException>(() => client.RevokeAsync("com.test.game", "private-refresh", CancellationToken.None));
            Assert.Equal("social-unavailable", failure.Message);
            Assert.Null(failure.InnerException);
        }
    }

    [Fact]
    public async Task RevokeDoesNotTreatAnErrorBodyAsSuccess()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"error\":\"invalid_client\"}")));
        using var http = new HttpClient(handler);
        Assert.Equal("social-unavailable", (await Assert.ThrowsAsync<AuthException>(() => new AppleTokenClient(http, new FixedClock(), Options(key))
            .RevokeAsync("com.test.game", "refresh", CancellationToken.None))).Message);
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler((_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AppleTokenClient(http, new FixedClock(), Options(key))
            .RevokeAsync("com.test.game", "refresh", cancellation.Token));
    }

    [Fact]
    public async Task InvalidSigningKeyIsSanitisedAndMakesNoRequest()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("must not send"));
        using var http = new HttpClient(handler);
        var options = new AppleClientOptions("team", "key", "private-invalid-pem");
        var failure = await Assert.ThrowsAsync<AuthException>(() => new AppleTokenClient(http, new FixedClock(), options)
            .ExchangeAsync("com.test.game", "private-code", null, CancellationToken.None));
        Assert.Equal("social-unavailable", failure.Message);
        Assert.Null(failure.InnerException);
        Assert.DoesNotContain("private-invalid-pem", options.ToString());
        Assert.DoesNotContain("identity", new AppleTokenExchange("identity", "refresh").ToString());
        Assert.DoesNotContain("refresh", new AppleTokenExchange("identity", "refresh").ToString());
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private static Dictionary<string, string> Form(string text) => text.Split('&').Select(part => part.Split('=', 2))
        .ToDictionary(part => WebUtility.UrlDecode(part[0]), part => WebUtility.UrlDecode(part[1]), StringComparer.Ordinal);

    private static byte[] Decode(string value)
    {
        string normal = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normal.PadRight((normal.Length + 3) / 4 * 4, '='));
    }

    private static void VerifySecret(string token, ECDsa key, TimeProvider clock, AppleClientOptions options)
    {
        string[] parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString());
        Assert.Equal(options.KeyId, header.RootElement.GetProperty("kid").GetString());
        Assert.Equal(options.TeamId, payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal("https://appleid.apple.com", payload.RootElement.GetProperty("aud").GetString());
        Assert.Equal("com.test.game", payload.RootElement.GetProperty("sub").GetString());
        Assert.Equal(clock.GetUtcNow().ToUnixTimeSeconds(), payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds(), payload.RootElement.GetProperty("exp").GetInt64());
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
}
