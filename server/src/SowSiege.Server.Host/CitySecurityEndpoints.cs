using Microsoft.Extensions.DependencyInjection.Extensions;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public sealed record ShieldCommand(Guid CommandId);

public static class CitySecurityEndpoints
{
    public static IServiceCollection AddCitySecurity(this IServiceCollection services)
    {
        services.TryAddSingleton<WorldRecoveryGate>();
        return services.AddSingleton(provider => CitySecurityPolicy.Load(provider.GetRequiredService<IConfiguration>()["GameContentRoot"]!))
            .AddSingleton<CitySecurityStore>();
    }

    public static async Task InitializeCitySecurityAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<CitySecurityStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapCitySecurity(this WebApplication app)
    {
        app.MapGet("/v1/world/shield", async (HttpContext context, AccountStore accounts, CitySecurityStore security, CancellationToken ct) =>
        {
            Guid? id = await Authenticate(context, accounts, ct);
            if (id is null) { return Results.Unauthorized(); }
            try { return Results.Ok(await security.ViewAsync(id.Value, ct)); }
            catch (CommandConflictException error) { return Results.Conflict(new ApiError(error.Message)); }
        }).RequireRateLimiting("prototype");
        app.MapPost("/v1/world/shield", async (ShieldCommand command, HttpContext context, AccountStore accounts, CitySecurityStore security, CancellationToken ct) =>
        {
            Guid? id = await Authenticate(context, accounts, ct);
            if (id is null) { return Results.Unauthorized(); }
            if (command.CommandId == Guid.Empty) { return Results.BadRequest(new ApiError("invalid-command")); }
            try { return Results.Ok(await security.ActivateAsync(id.Value, command.CommandId, ct)); }
            catch (CommandConflictException error) { return Results.Conflict(new ApiError(error.Message)); }
        }).RequireRateLimiting("prototype");
    }

    private static async Task<Guid?> Authenticate(HttpContext context, AccountStore accounts, CancellationToken ct)
    {
        string header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? await accounts.AuthenticateAsync(header[7..], ct) : null;
    }
}
