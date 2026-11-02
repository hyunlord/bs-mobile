using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public static class WorldEndpoints
{
    public static IServiceCollection AddWorld(this IServiceCollection services) =>
        services.AddSingleton(provider => WorldMapContent.Load(provider.GetRequiredService<IConfiguration>()["GameContentRoot"]!))
            .AddSingleton<WorldStore>();

    public static async Task InitializeWorldAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<WorldStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapWorld(this WebApplication app)
    {
        // Account creation (both prototype and guest) shares the allocation transaction and capacity error.
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (WorldCapacityException)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new ApiError("world-capacity-exhausted"), context.RequestAborted);
            }
        });
        app.MapGet("/v1/world/chunks/{chunkX:int}/{chunkY:int}", async (int chunkX, int chunkY, HttpContext context,
            AccountStore accounts, WorldStore world, WorldMarchStore marches, CancellationToken cancellation) =>
        {
            if (await Authenticate(context, accounts, cancellation) is null) { return Results.Unauthorized(); }
            try { return Results.Ok(await marches.ExecuteAsync((_, _, _, ct) => world.ChunkAsync(chunkX, chunkY, ct), cancellation)); }
            catch (ArgumentOutOfRangeException) { return Results.BadRequest(new ApiError("invalid-chunk")); }
        }).RequireRateLimiting("prototype");
        app.MapGet("/v1/world/city", async (HttpContext context, AccountStore accounts, WorldStore world, CancellationToken cancellation) =>
        {
            var id = await Authenticate(context, accounts, cancellation);
            if (id is null) { return Results.Unauthorized(); }
            var city = await world.FindCityAsync(id.Value, cancellation);
            return city is null ? Results.NotFound(new ApiError("city-not-assigned")) : Results.Ok(city);
        }).RequireRateLimiting("prototype");
        app.MapPost("/v1/world/city", async (HttpContext context, AccountStore accounts, WorldStore world, TimeProvider time, CancellationToken cancellation) =>
        {
            var id = await Authenticate(context, accounts, cancellation);
            if (id is null) { return Results.Unauthorized(); }
            try { return Results.Ok(await world.EnsureCityAsync(id.Value, time.GetUtcNow().ToUnixTimeMilliseconds(), cancellation)); }
            catch (CommandConflictException conflict) { return Results.Conflict(new ApiError(conflict.Message)); }
        }).RequireRateLimiting("prototype");
    }

    private static Task<Guid?> Authenticate(HttpContext context, AccountStore accounts, CancellationToken cancellation)
    {
        string header = context.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal)
            ? accounts.AuthenticateAsync(header[7..], cancellation) : Task.FromResult<Guid?>(null);
    }
}
