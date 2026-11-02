using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

/// <summary>Alliance registration kept out of Program.cs so the shared file only gains the three calls below.</summary>
public static class AllianceEndpoints
{
    public static IServiceCollection AddAlliance(this IServiceCollection services) =>
        services.AddSingleton(provider => AllianceContent.Load(provider.GetRequiredService<IConfiguration>()["GameContentRoot"]!))
            .AddSingleton<AllianceStore>();

    public static async Task InitializeAllianceAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<AllianceStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapAlliance(this WebApplication app) =>
        app.MapGet("/v1/alliance", async (HttpContext context, AccountStore accounts, AllianceStore alliance, CancellationToken cancellation) =>
        {
            var id = await AuthenticateAsync(context, accounts, cancellation);
            return id is null ? Results.Unauthorized() : Results.Ok(await alliance.ViewAsync(id.Value, cancellation));
        }).RequireRateLimiting("prototype");

    /// <summary>Called from /v1/account/commands when domain is "alliance".</summary>
    public static async Task<IResult> ExecuteAsync(AccountCommand command, HttpContext context, CancellationToken cancellation)
    {
        var accounts = context.RequestServices.GetRequiredService<AccountStore>();
        var id = await AuthenticateAsync(context, accounts, cancellation);
        if (id is null)
        {
            return Results.Unauthorized();
        }
        try
        {
            return Results.Content(await context.RequestServices.GetRequiredService<AllianceStore>().ExecuteAsync(id.Value, command, cancellation), "application/json");
        }
        catch (CommandConflictException conflict)
        {
            return Results.Conflict(new ApiError(conflict.Message));
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest(new ApiError("invalid-domain-payload"));
        }
    }

    private static async Task<Guid?> AuthenticateAsync(HttpContext context, AccountStore accounts, CancellationToken cancellation)
    {
        string header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? await accounts.AuthenticateAsync(header[7..], cancellation) : null;
    }
}
