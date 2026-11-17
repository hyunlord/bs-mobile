using System.Text.Json.Serialization;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

/// <summary>ADR0080 stage 1 endpoints, kept out of Program.cs (which only gains the three registration calls).</summary>
public static class AuthEndpoints
{
    public sealed record GuestRequest([property: JsonRequired] string PublicKey);
    public sealed record ChallengeRequest([property: JsonRequired] Guid KeyId);
    public sealed record DeviceRequest([property: JsonRequired] Guid KeyId, [property: JsonRequired] string Nonce, [property: JsonRequired] string Signature);
    public sealed record RefreshRequest([property: JsonRequired] string RefreshToken);
    public sealed record DeleteRequest([property: JsonRequired] string Confirm);
    /// <summary>The account is permanently deleted after PurgeAt unless the same device signs in before then (ADR0080 §5).</summary>
    public sealed record DeleteReply(DateTimeOffset PurgeAt);

    public static IServiceCollection AddAuth(this IServiceCollection services) =>
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var limits = configuration.GetSection("Auth:Limits").Get<AuthLimits>() ?? throw new InvalidOperationException("Auth:Limits required.");
            return new AuthOptions(TimeSpan.FromMinutes(configuration.GetValue("Auth:AccessMinutes", 15)),
                TimeSpan.FromDays(configuration.GetValue("Auth:RefreshDays", 30)), TimeSpan.FromSeconds(configuration.GetValue("Auth:ChallengeSeconds", 60)), limits,
                AuthOptions.GuestCreationOpen(provider.GetRequiredService<IHostEnvironment>().EnvironmentName, configuration["Prototype:Enabled"]),
                configuration["Auth:PseudonymKey"] is { Length: >= 64 } key ? Convert.FromHexString(key)
                    : throw new InvalidOperationException("Auth:PseudonymKey (>=32 bytes hex) required."),
                configuration.GetValue("Auth:DeletionGraceDays", 30));
        }).AddSingleton<AuthStore>().AddHostedService<AccountPurgeService>().AddSocialAuth();

    public static async Task InitializeAuthAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<AuthStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapAuth(this WebApplication app)
    {
        app.MapSocialAuth();
        app.MapPurgeAdmin();
        app.MapPost("/v1/auth/guest", (GuestRequest request, HttpContext context, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.CreateGuestAsync(FromBase64(request.PublicKey), ClientIp(context), ct)))).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/challenge", (ChallengeRequest request, HttpContext context, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.ChallengeAsync(request.KeyId, ClientIp(context), ct)))).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/device", (DeviceRequest request, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.SignInAsync(request.KeyId, request.Nonce, FromBase64(request.Signature), ct)))).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/refresh", (RefreshRequest request, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.RefreshAsync(request.RefreshToken, ct)))).RequireRateLimiting("prototype");
        // In-app deletion needs an explicit confirmation word so a stray call cannot delete an account.
        app.MapPost("/v1/account/delete", (DeleteRequest request, HttpContext context, AccountStore accounts, AuthStore auth, CancellationToken ct) => Guard(async () =>
        {
            string header = context.Request.Headers.Authorization.ToString();
            Guid? id = header.StartsWith("Bearer ", StringComparison.Ordinal) ? await accounts.AuthenticateAsync(header[7..], ct) : null;
            if (id is null)
            {
                return Results.Unauthorized();
            }
            if (request.Confirm != "delete")
            {
                return Results.BadRequest(new ApiError("confirmation-required"));
            }
            var purgeAt = await auth.DeleteAccountAsync(id.Value, ct);
            return Results.Ok(new DeleteReply(purgeAt));
        })).RequireRateLimiting("prototype");
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AuthException failure)
        {
            return failure.Message switch
            {
                "key-unsupported" or "invalid-base64" => Results.BadRequest(new ApiError(failure.Message)),
                "rate-limited" => Results.Json(new ApiError(failure.Message), statusCode: StatusCodes.Status429TooManyRequests),
                "guest-disabled" => Results.Json(new ApiError(failure.Message), statusCode: StatusCodes.Status403Forbidden),
                "state-changed" => Results.Json(new ApiError(failure.Message), statusCode: StatusCodes.Status409Conflict),
                _ => Results.Json(new ApiError(failure.Message), statusCode: StatusCodes.Status401Unauthorized),
            };
        }
    }

    // Direct peer address; a reverse proxy deployment must configure forwarded headers before this is meaningful.
    private static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static byte[] FromBase64(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new AuthException("invalid-base64"); }
    }
}

/// <summary>ADR0080 §5 periodic job: purges accounts past the deletion grace period and expired report snapshots.</summary>
public sealed class AccountPurgeService(IServiceProvider services, IConfiguration configuration, ILogger<AccountPurgeService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(configuration.GetValue("Auth:PurgeIntervalMinutes", 60));
        var retry = TimeSpan.FromMinutes(configuration.GetValue("Auth:PurgeRetryMinutes", 60));
        int budget = configuration.GetValue("Auth:PurgeCycleCandidates", 1000);
        if (interval <= TimeSpan.Zero || retry <= TimeSpan.Zero || retry > TimeSpan.FromDays(1) || budget < 1)
        { throw new InvalidOperationException("Purge scheduling configuration is invalid."); }
        using var timer = new PeriodicTimer(interval);
        AuthStore.PurgeCandidate? cursor = null;
        do
        {
            try
            {
                var auth = services.GetRequiredService<AuthStore>();
                cursor = await PurgeCycle.RunAsync(cursor, budget,
                    (batch, after, ct) => auth.PurgePageAsync(batch, after, retry, ct), stoppingToken);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                stoppingToken.ThrowIfCancellationRequested();
                log.LogError("Account purge cycle failed; pending work remains for the next interval.");
            }
            try
            {
                if (services.GetService<AllianceStore>() is { } alliances) { await alliances.PruneReportsAsync(stoppingToken); }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                stoppingToken.ThrowIfCancellationRequested();
                log.LogError("Expired report pruning failed; retrying next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
