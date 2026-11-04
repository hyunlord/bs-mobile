using System.Text.Json.Serialization;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public static partial class SocialAuthEndpoints
{
    public sealed record ChallengeRequest([property: JsonRequired] string Provider, [property: JsonRequired] string ClientId,
        [property: JsonRequired] string Purpose, string? PublicKey = null);
    public sealed record ProofRequest([property: JsonRequired] Guid ChallengeId, [property: JsonRequired] string Nonce,
        [property: JsonRequired] string IdentityToken, string? AuthorizationCode = null, string? Signature = null, string? Confirm = null);

    public static IServiceCollection AddSocialAuth(this IServiceCollection services)
    {
        // Credentials must never be forwarded to a redirect target; provider URLs are fixed in the clients.
        services.AddHttpClient("social-auth").ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
        });
        services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>().GetSection("Auth:Social");
            var clients = config.GetSection("Providers").GetChildren().Where(c => !string.IsNullOrWhiteSpace(c["Provider"]))
                .Select(c => new SocialProviderOptions(c["Provider"]!, c["ClientId"] ?? "", c.GetSection("AllowedPresenters").Get<string[]>() ?? [], c["RedirectUri"]))
                .ToArray();
            if (clients.Any(c => c.Provider is not ("apple" or "google") || string.IsNullOrWhiteSpace(c.ClientId))
                || clients.Select(c => (c.Provider, c.ClientId)).Distinct().Count() != clients.Length)
            {
                throw new InvalidOperationException("Auth:Social provider/client configuration is invalid.");
            }
            byte[]? key = null;
            if (!string.IsNullOrEmpty(config["CredentialKey"]))
            {
                try { key = Convert.FromHexString(config["CredentialKey"]!); }
                catch (FormatException) { throw new InvalidOperationException("Auth:Social:CredentialKey must be 32 bytes hex."); }
                if (key.Length != 32) { throw new InvalidOperationException("Auth:Social:CredentialKey must be 32 bytes hex."); }
            }
            if (clients.Any(c => c.Provider == "apple") && key is null)
            {
                throw new InvalidOperationException("Apple linking requires Auth:Social:CredentialKey.");
            }
            return new SocialAuthOptions(clients, key);
        });
        services.AddSingleton<ISocialIdentityVerifier>(provider => new SocialIdentityVerifier(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient("social-auth"), provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<SocialAuthOptions>().Providers));
        services.AddSingleton<IAppleTokenClient>(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>().GetSection("Auth:Social:Apple");
            return new AppleTokenClient(provider.GetRequiredService<IHttpClientFactory>().CreateClient("social-auth"),
                provider.GetRequiredService<TimeProvider>(), new(config["TeamId"] ?? "", config["KeyId"] ?? "", config["PrivateKeyPem"] ?? ""));
        });
        return services.AddSingleton<SocialAuthService>().AddHostedService<AppleRevocationService>();
    }

    public static void MapSocialAuth(this WebApplication app)
    {
        MapWebAccountDeletion(app);
        app.MapGet("/v1/account/deletion", (AuthOptions options) => Results.Ok(new
        {
            linkedAccounts = "/v1/account/deletion/requests",
            challenge = "/v1/account/deletion/challenge",
            guestAccounts = "/v1/account/delete",
            graceDays = options.DeletionGraceDays,
            confirmation = "delete",
            notice = "연결 계정은 Apple 또는 Google 본인 확인 뒤 웹에서 삭제를 요청할 수 있습니다. 미연결 게스트는 앱 안에서 삭제하세요. 유예 중 기존 기기 키로 로그인하면 삭제가 취소됩니다.",
        })).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/social/challenge", (ChallengeRequest request, HttpContext context, SocialAuthService social, CancellationToken ct) =>
            Guard(async () => Results.Ok(await social.ChallengeAsync(request.Provider, request.ClientId, request.Purpose,
                Bearer(context), Decode(request.PublicKey), context.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct))))
            .RequireRateLimiting("prototype");
        foreach (string purpose in new[] { "link", "login", "delete" })
        {
            app.MapPost("/v1/auth/social/" + purpose, (ProofRequest request, HttpContext context, SocialAuthService social, CancellationToken ct) =>
                Guard(async () =>
                {
                    if (purpose == "delete" && request.Confirm != "delete")
                    {
                        return Results.BadRequest(new ApiError("confirmation-required"));
                    }
                    return Results.Ok(await social.CompleteAsync(request.ChallengeId, request.Nonce, purpose, request.IdentityToken,
                        request.AuthorizationCode, Decode(request.Signature), Bearer(context), ct));
                })).RequireRateLimiting("prototype");
        }
    }

    private static string? Bearer(HttpContext context)
    {
        string value = context.Request.Headers.Authorization.ToString();
        return value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : null;
    }

    private static byte[]? Decode(string? value)
    {
        if (value is null) { return null; }
        if (value.Length > 4096) { throw new AuthException("invalid-base64"); }
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new AuthException("invalid-base64"); }
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (AuthException failure)
        {
            int status = failure.Message switch
            {
                "invalid-base64" or "key-unsupported" or "invalid-purpose" or "invalid-request" => StatusCodes.Status400BadRequest,
                "social-disabled" => StatusCodes.Status403Forbidden,
                "identity-linked" or "social-conflict" or "state-changed" or "social-revocation-pending" => StatusCodes.Status409Conflict,
                "rate-limited" => StatusCodes.Status429TooManyRequests,
                "social-unavailable" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status401Unauthorized,
            };
            return Results.Json(new ApiError(failure.Message), statusCode: status);
        }
    }
}

/// <summary>Durable provider revocation retries do not delay an account deletion HTTP response.</summary>
public sealed class AppleRevocationService(SocialAuthService social, IConfiguration configuration, ILogger<AppleRevocationService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(configuration.GetValue("Auth:Social:RevocationPollSeconds", 60)));
        do
        {
            try { await social.RetryRevocationsAsync(stoppingToken); }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // Provider credentials and exception payloads must never enter logs.
                log.LogError("Apple revocation processing failed; durable jobs remain pending.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
