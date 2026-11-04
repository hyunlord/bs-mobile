using System.Text.Json.Serialization;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public static partial class SocialAuthEndpoints
{
    public sealed record WebDeletionChallengeRequest([property: JsonRequired] string Provider, [property: JsonRequired] string ClientId);
    public sealed record WebDeletionRequest([property: JsonRequired] Guid ChallengeId, [property: JsonRequired] string Nonce,
        [property: JsonRequired] string IdentityToken, [property: JsonRequired] string Confirm, string? AuthorizationCode = null);

    private static void MapWebAccountDeletion(WebApplication app)
    {
        app.MapPost("/v1/account/deletion/challenge",
            (WebDeletionChallengeRequest request, HttpContext context, SocialAuthService social, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Guard(async () => Results.Ok(await social.ChallengeAsync(request.Provider, request.ClientId, "delete", null, null,
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown", ct)));
            }).RequireRateLimiting("prototype");

        app.MapPost("/v1/account/deletion/requests",
            (WebDeletionRequest request, HttpContext context, SocialAuthService social, CancellationToken ct) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Guard(async () =>
                {
                    if (request.Confirm != "delete") { return Results.BadRequest(new ApiError("confirmation-required")); }
                    // Only the fresh provider proof selects the linked account; app sessions are irrelevant to this web request.
                    var reply = await social.CompleteAsync(request.ChallengeId, request.Nonce, "delete", request.IdentityToken,
                        request.AuthorizationCode, null, null, ct);
                    return Results.Ok(new AuthEndpoints.DeleteReply(reply.PurgeAt
                        ?? throw new InvalidOperationException("Deletion completed without a purge deadline.")));
                });
            }).RequireRateLimiting("prototype");
    }
}
