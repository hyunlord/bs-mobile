using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SowSiege.Server.Infrastructure;

public sealed record AppleClientOptions(string TeamId, string KeyId, string PrivateKeyPem)
{
    public override string ToString() => "AppleClientOptions { credentials redacted }";
}

public sealed record AppleTokenExchange(string IdentityToken, string RefreshToken)
{
    public override string ToString() => "AppleTokenExchange { credentials redacted }";
}

public interface IAppleTokenClient
{
    Task<AppleTokenExchange> ExchangeAsync(string clientId, string code, string? redirectUri, CancellationToken cancellation);
    Task RevokeAsync(string clientId, string refreshToken, CancellationToken cancellation);
}

/// <summary>
/// Apple REST token exchange and revocation. The injected HTTP transport must disable automatic redirects.
/// The caller must verify the returned identity token's signature, client audience and nonce before linking it.
/// https://developer.apple.com/documentation/signinwithapplerestapi/generate-and-validate-tokens
/// https://developer.apple.com/documentation/signinwithapplerestapi/revoke-tokens
/// </summary>
public sealed class AppleTokenClient(HttpClient http, TimeProvider time, AppleClientOptions options) : IAppleTokenClient
{
    private const int MaximumResponseBytes = 32 * 1024;

    public async Task<AppleTokenExchange> ExchangeAsync(string clientId, string code, string? redirectUri, CancellationToken cancellation)
    {
        try
        {
            Require(code);
            var form = ClientForm(clientId);
            form.Add("code", code);
            form.Add("grant_type", "authorization_code");
            if (redirectUri is not null)
            {
                Require(redirectUri);
                form.Add("redirect_uri", redirectUri);
            }
            byte[] bytes = await SendAsync("https://appleid.apple.com/auth/token", form, cancellation);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Unavailable();
            }
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name) || property.NameEquals("error"))
                {
                    throw Unavailable();
                }
            }
            return new(Token(root, "id_token"), Token(root, "refresh_token"));
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw Unavailable(); }
        catch (Exception failure) when (IsProviderFailure(failure)) { throw Unavailable(); }
    }

    public async Task RevokeAsync(string clientId, string refreshToken, CancellationToken cancellation)
    {
        try
        {
            Require(refreshToken);
            var form = ClientForm(clientId);
            form.Add("token", refreshToken);
            form.Add("token_type_hint", "refresh_token");
            byte[] response = await SendAsync("https://appleid.apple.com/auth/revoke", form, cancellation);
            if (response.Length != 0) { throw Unavailable(); }
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw Unavailable(); }
        catch (Exception failure) when (IsProviderFailure(failure)) { throw Unavailable(); }
    }

    private Dictionary<string, string> ClientForm(string clientId)
    {
        Require(clientId);
        Require(options.TeamId);
        Require(options.KeyId);
        Require(options.PrivateKeyPem);
        using var key = ECDsa.Create();
        key.ImportFromPem(options.PrivateKeyPem);
        if (key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
        {
            throw Unavailable();
        }
        long now = time.GetUtcNow().ToUnixTimeSeconds();
        string header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = options.KeyId }));
        string payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = options.TeamId,
            iat = now,
            exp = checked(now + 300),
            aud = "https://appleid.apple.com",
            sub = clientId,
        }));
        string input = header + "." + payload;
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new(StringComparer.Ordinal) { ["client_id"] = clientId, ["client_secret"] = input + "." + Encode(signature) };
    }

    private async Task<byte[]> SendAsync(string endpoint, Dictionary<string, string> form, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(form) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength > MaximumResponseBytes)
        {
            throw Unavailable();
        }
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[MaximumResponseBytes + 1];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), deadline.Token);
            if (read == 0)
            {
                return buffer.AsSpan(0, total).ToArray();
            }
            total += read;
        }
        throw Unavailable();
    }

    private static string Token(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var token) || token.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(token.GetString()))
        {
            throw Unavailable();
        }
        return token.GetString()!;
    }

    private static void Require(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) { throw Unavailable(); }
    }

    private static bool IsProviderFailure(Exception failure) => failure is HttpRequestException or IOException or JsonException or CryptographicException or ArgumentException;
    private static AuthException Unavailable() => new("social-unavailable");
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
