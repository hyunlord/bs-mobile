using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SowSiege.Server.Infrastructure;

public sealed record SocialProviderOptions(string Provider, string ClientId, string[] AllowedPresenters, string? RedirectUri = null);
public sealed record VerifiedSocialIdentity(string Provider, string Subject, string ClientId);

public interface ISocialIdentityVerifier
{
    Task<VerifiedSocialIdentity> VerifyAsync(string provider, string clientId, string token, string nonce, CancellationToken cancellation);
}

/// <summary>Validates provider identities only. The caller binds and atomically consumes the authentication challenge.</summary>
public sealed class SocialIdentityVerifier(HttpClient http, TimeProvider time, IReadOnlyList<SocialProviderOptions> providers) : ISocialIdentityVerifier
{
    private sealed record PublicKey(byte[] Modulus, byte[] Exponent);
    private sealed class KeyCache
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<string, PublicKey> Keys { get; set; } = new(StringComparer.Ordinal);
        public HashSet<string> KnownIds { get; set; } = new(StringComparer.Ordinal);
        public DateTimeOffset Expires { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset NextUnknownRefresh { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset FailureUntil { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset RequestWindow { get; set; } = DateTimeOffset.MinValue;
        public int Requests { get; set; }
    }

    private readonly SocialProviderOptions[] configured = providers.Select(p => p with { AllowedPresenters = [.. p.AllowedPresenters] }).ToArray();
    private readonly KeyCache googleKeys = new();
    private readonly KeyCache appleKeys = new();
    private const int TokenLimit = 16_384;
    private const int KeysLimit = 65_536;

    public async Task<VerifiedSocialIdentity> VerifyAsync(string provider, string clientId, string token, string nonce, CancellationToken cancellation)
    {
        var options = configured.SingleOrDefault(p => p.Provider == provider && p.ClientId == clientId);
        if (options is null || provider is not ("google" or "apple"))
        {
            throw new AuthException("social-disabled");
        }
        try
        {
            if (string.IsNullOrEmpty(token) || token.Length > TokenLimit || string.IsNullOrEmpty(nonce) || nonce.Length > 256)
            {
                throw Invalid();
            }
            string[] parts = token.Split('.');
            if (parts.Length != 3)
            {
                throw Invalid();
            }
            using var header = Parse(Decode(parts[0]));
            using var payload = Parse(Decode(parts[1]));
            var h = header.RootElement;
            var p = payload.RootElement;
            if (RequiredString(h, "alg", 16) != "RS256" || h.TryGetProperty("crit", out _)
                || h.TryGetProperty("b64", out _))
            {
                throw Invalid();
            }
            string kid = RequiredString(h, "kid", 128);
            string issuer = RequiredString(p, "iss", 128);
            if (provider == "google" ? issuer is not ("https://accounts.google.com" or "accounts.google.com") : issuer != "https://appleid.apple.com")
            {
                throw Invalid();
            }
            ValidateAudience(p, options);
            ValidateTime(p);
            if (RequiredString(p, "nonce", 256) != nonce)
            {
                throw Invalid();
            }
            string subject = RequiredString(p, "sub", 255);
            if (subject.Any(c => c < '!' || c > '~'))
            {
                throw Invalid();
            }
            byte[] signature = Decode(parts[2]);
            var key = await GetKeyAsync(provider, kid, cancellation);
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = key.Modulus, Exponent = key.Exponent });
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                throw Invalid();
            }
            // Fetching rotated keys may take long enough for an otherwise valid token to expire.
            ValidateTime(p);
            return new(provider, subject, clientId);
        }
        catch (Exception failure) when (failure is JsonException or FormatException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private void ValidateTime(JsonElement payload)
    {
        long now = time.GetUtcNow().ToUnixTimeSeconds();
        long issued = RequiredInteger(payload, "iat");
        long expires = RequiredInteger(payload, "exp");
        if (issued < 0 || issued > now + 60 || expires <= now || expires <= issued
            || payload.TryGetProperty("nbf", out var notBefore) && (!notBefore.TryGetInt64(out long validFrom) || validFrom > now))
        {
            throw Invalid();
        }
    }

    private void ValidateAudience(JsonElement payload, SocialProviderOptions options)
    {
        if (!payload.TryGetProperty("aud", out var aud))
        {
            throw Invalid();
        }
        string[] audiences = aud.ValueKind switch
        {
            JsonValueKind.String => [aud.GetString()!],
            JsonValueKind.Array when aud.GetArrayLength() is > 0 and <= 16 => aud.EnumerateArray().Select(a => a.GetString() ?? "").ToArray(),
            _ => throw Invalid(),
        };
        if (!audiences.Contains(options.ClientId, StringComparer.Ordinal) || audiences.Distinct(StringComparer.Ordinal).Count() != audiences.Length
            || audiences.Any(a => !configured.Any(p => p.Provider == options.Provider && p.ClientId == a)))
        {
            throw Invalid();
        }
        if (payload.TryGetProperty("azp", out var presenter))
        {
            string? azp = presenter.GetString();
            if (azp != options.ClientId && !options.AllowedPresenters.Contains(azp, StringComparer.Ordinal))
            {
                throw Invalid();
            }
        }
        else if (audiences.Length > 1)
        {
            // This service deliberately requires an explicit authorized presenter for multi-audience tokens.
            throw Invalid();
        }
    }

    private async Task<PublicKey> GetKeyAsync(string provider, string kid, CancellationToken cancellation)
    {
        var cache = provider == "google" ? googleKeys : appleKeys;
        await cache.Gate.WaitAsync(cancellation);
        try
        {
            var now = time.GetUtcNow();
            if (now < cache.Expires && cache.Keys.TryGetValue(kid, out var cached))
            {
                return cached;
            }
            if (now < cache.FailureUntil)
            {
                throw new AuthException("social-unavailable");
            }
            if (!cache.KnownIds.Contains(kid) && now < cache.NextUnknownRefresh)
            {
                throw Invalid();
            }
            if (now >= cache.RequestWindow.AddSeconds(1))
            {
                cache.RequestWindow = now;
                cache.Requests = 0;
            }
            // A provider may require revalidation on every use. Bound that traffic independently of unknown-key refreshes.
            if (cache.Requests >= 8) { throw new AuthException("social-unavailable"); }
            cache.Requests++;
            cache.NextUnknownRefresh = now.AddSeconds(30);
            PublicKey? fetchedKey;
            try
            {
                string uri = provider == "google" ? "https://www.googleapis.com/oauth2/v3/certs" : "https://appleid.apple.com/auth/keys";
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > KeysLimit
                    || response.RequestMessage?.RequestUri is { } actualUri && actualUri.AbsoluteUri != uri)
                {
                    throw new AuthException("social-unavailable");
                }
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                byte[] buffer = new byte[KeysLimit + 1];
                int length = 0;
                while (length < buffer.Length)
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
                    if (read == 0) { break; }
                    length += read;
                }
                if (length > KeysLimit) { throw new AuthException("social-unavailable"); }
                var fetched = ReadKeys(buffer.AsMemory(0, length));
                cache.KnownIds = new(fetched.Keys, StringComparer.Ordinal);
                cache.Expires = time.GetUtcNow().Add(CacheLifetime(response.Headers.CacheControl, response.Headers.Age));
                cache.Keys = response.Headers.CacheControl?.NoStore == true ? new(StringComparer.Ordinal) : fetched;
                cache.FailureUntil = DateTimeOffset.MinValue;
                fetched.TryGetValue(kid, out fetchedKey);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure) when (failure is HttpRequestException or IOException or OperationCanceledException
                or JsonException or FormatException or CryptographicException or InvalidOperationException or ArgumentException or AuthException)
            {
                cache.FailureUntil = time.GetUtcNow().AddSeconds(30);
                throw new AuthException("social-unavailable");
            }
            return fetchedKey ?? throw Invalid();
        }
        finally
        {
            cache.Gate.Release();
        }
    }

    private static TimeSpan CacheLifetime(CacheControlHeaderValue? control, TimeSpan? age)
    {
        if (control?.NoStore == true || control?.NoCache == true) { return TimeSpan.Zero; }
        double seconds = ((control?.MaxAge ?? TimeSpan.FromMinutes(5)) - (age ?? TimeSpan.Zero)).TotalSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600));
    }

    private static Dictionary<string, PublicKey> ReadKeys(ReadOnlyMemory<byte> body)
    {
        using var document = Parse(body);
        if (!document.RootElement.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Array
            || keys.GetArrayLength() is 0 or > 16)
        {
            throw Invalid();
        }
        var result = new Dictionary<string, PublicKey>(StringComparer.Ordinal);
        foreach (var item in keys.EnumerateArray())
        {
            if (RequiredString(item, "kty", 16) != "RSA") { continue; }
            if (item.TryGetProperty("use", out var use) && use.GetString() != "sig") { continue; }
            if (item.TryGetProperty("alg", out var alg) && alg.GetString() != "RS256") { continue; }
            if (item.TryGetProperty("key_ops", out var ops) && (ops.ValueKind != JsonValueKind.Array
                || !ops.EnumerateArray().Any(op => op.ValueKind == JsonValueKind.String && op.GetString() == "verify"))) { continue; }
            string kid = RequiredString(item, "kid", 128);
            byte[] modulus = Decode(RequiredString(item, "n", 1400));
            byte[] exponent = Decode(RequiredString(item, "e", 12));
            if (modulus.Length is < 256 or > 1024 || exponent.Length is 0 or > 8 || !result.TryAdd(kid, new(modulus, exponent)))
            {
                throw Invalid();
            }
            using var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
        }
        if (result.Count == 0) { throw Invalid(); }
        return result;
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        try
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) { throw Invalid(); }
            UniqueProperties(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw Invalid(); }
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray()) { UniqueProperties(value); }
        }
    }

    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
        {
            throw Invalid();
        }
        return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
    }

    private static string RequiredString(JsonElement element, string name, int max)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { Length: > 0 } text || text.Length > max)
        {
            throw Invalid();
        }
        return text;
    }

    private static long RequiredInteger(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : throw Invalid();

    private static AuthException Invalid() => new("social-invalid");
}
