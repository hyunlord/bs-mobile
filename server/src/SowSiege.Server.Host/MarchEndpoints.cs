using Microsoft.Extensions.DependencyInjection.Extensions;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public sealed record MarchView(Guid MarchId, string TargetId, long Generation, string Operation, string HeroId,
    MarchTroops[] Troops, WorldCoordinate[] Path, long StartedAtMs, long DueAtMs, string State, string Reason);

public static class MarchEndpoints
{
    public static IServiceCollection AddMarches(this IServiceCollection services)
    {
        services.TryAddSingleton<WorldRecoveryGate>();
        services.AddSingleton(p => WorldMarchContent.Load(p.GetRequiredService<IConfiguration>()["GameContentRoot"]!));
        services.AddSingleton(p => ResearchContent.Load(p.GetRequiredService<IConfiguration>()["GameContentRoot"]!));
        services.AddSingleton<TrainingHandler>();
        services.AddSingleton<IWorldMarchActivity, GatheringMarchActivity>();
        services.AddSingleton<WorldMarchStore>();
        return services;
    }

    public static async Task InitializeMarchesAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<WorldMarchStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapMarches(this WebApplication app)
    {
        app.MapGet("/v1/world/marches", async (HttpContext context, AccountStore accounts, WorldMarchStore marches, CancellationToken ct) =>
        {
            var id = await Authenticate(context, accounts, ct);
            if (id is null) { return Results.Unauthorized(); }
            return Results.Ok((await marches.GetAsync(id.Value, ct)).Select(View).ToArray());
        }).RequireRateLimiting("prototype");
        app.MapPost("/v1/world/marches/commands", async (WorldMarchCommand command, HttpContext context, AccountStore accounts, WorldMarchStore marches, CancellationToken ct) =>
        {
            var id = await Authenticate(context, accounts, ct);
            if (id is null) { return Results.Unauthorized(); }
            try { return Results.Content(await marches.ExecuteAsync(id.Value, command, ct), "application/json"); }
            catch (CommandConflictException conflict) { return Results.Conflict(new ApiError(conflict.Message)); }
        }).RequireRateLimiting("prototype");
    }

    private static MarchView View(WorldMarch m) => new(m.MarchId, m.TargetId, m.Generation, m.Operation, m.HeroId,
        m.Troops, m.Path, m.StartedAtMs, m.DueAtMs, m.State, m.Reason);
    private static Task<Guid?> Authenticate(HttpContext context, AccountStore accounts, CancellationToken ct)
    {
        string header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? accounts.AuthenticateAsync(header[7..], ct) : Task.FromResult<Guid?>(null);
    }
}
