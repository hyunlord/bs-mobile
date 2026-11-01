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

    public static IServiceCollection AddAuth(this IServiceCollection services) =>
        services.AddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            return new AuthOptions(TimeSpan.FromMinutes(configuration.GetValue("Auth:AccessMinutes", 15)),
                TimeSpan.FromDays(configuration.GetValue("Auth:RefreshDays", 30)), TimeSpan.FromSeconds(configuration.GetValue("Auth:ChallengeSeconds", 60)));
        }).AddSingleton<AuthStore>();

    public static async Task InitializeAuthAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<AuthStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapAuth(this WebApplication app)
    {
        app.MapPost("/v1/auth/guest", (GuestRequest request, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.CreateGuestAsync(FromBase64(request.PublicKey), ct)))).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/challenge", (ChallengeRequest request, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.ChallengeAsync(request.KeyId, ct)))).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/device", (DeviceRequest request, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.SignInAsync(request.KeyId, request.Nonce, FromBase64(request.Signature), ct)))).RequireRateLimiting("prototype");
        app.MapPost("/v1/auth/refresh", (RefreshRequest request, AuthStore auth, CancellationToken ct) =>
            Guard(async () => Results.Ok(await auth.RefreshAsync(request.RefreshToken, ct)))).RequireRateLimiting("prototype");
        // In-app deletion needs an explicit confirmation word so a stray call cannot delete an account.
        app.MapPost("/v1/account/delete", async (DeleteRequest request, HttpContext context, AccountStore accounts, AuthStore auth, CancellationToken ct) =>
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
            await auth.DeleteAccountAsync(id.Value, ct);
            return Results.NoContent();
        }).RequireRateLimiting("prototype");
    }

    private static async Task<IResult> Guard(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AuthException failure)
        {
            return failure.Message is "key-unsupported" or "invalid-base64"
                ? Results.BadRequest(new ApiError(failure.Message))
                : Results.Json(new ApiError(failure.Message), statusCode: StatusCodes.Status401Unauthorized);
        }
    }

    private static byte[] FromBase64(string value)
    {
        try { return Convert.FromBase64String(value); }
        catch (FormatException) { throw new AuthException("invalid-base64"); }
    }
}
