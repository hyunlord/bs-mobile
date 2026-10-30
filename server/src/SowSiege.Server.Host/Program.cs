using System.Text.Json.Serialization;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 8192);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => EstateContent.Load(services.GetRequiredService<IConfiguration>()["GameContentRoot"] ?? throw new InvalidOperationException("GameContentRoot required.")));
builder.Services.AddSingleton<EstateApplication>();
builder.Services.AddSingleton(services => NpgsqlDataSource.Create(services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? throw new InvalidOperationException("Postgres required.")));
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton(services => RecruitPool.Load(services.GetRequiredService<IConfiguration>()["GameContentRoot"]!));
builder.Services.AddSingleton(services => new RecruitOptions(Convert.FromHexString(services.GetRequiredService<IConfiguration>()["Recruit:SeedKey"] is { Length: >= 64 } key
    ? key : throw new InvalidOperationException("Recruit:SeedKey (>=32 bytes hex) required."))));
builder.Services.AddSingleton<RecruitStore>();
builder.Services.AddSingleton(services => ResearchContent.Load(services.GetRequiredService<IConfiguration>()["GameContentRoot"]!));
builder.Services.AddSingleton<ResearchStore>();
builder.Services.AddSingleton(services => new ConnectionCache(services.GetRequiredService<IConfiguration>().GetConnectionString("Redis") ?? "localhost:6379"));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("prototype", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});
var app = builder.Build();
if (!app.Environment.IsDevelopment() || app.Configuration["Prototype:Enabled"] != "true")
{
    throw new InvalidOperationException("This prototype requires Development and explicit Prototype:Enabled=true.");
}
await app.Services.GetRequiredService<AccountStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<RecruitStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
await app.Services.GetRequiredService<ResearchStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
app.UseRateLimiter();
app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
app.MapPost("/prototype/accounts", async (AccountStore store, CancellationToken cancellation) =>
    Results.Ok(await store.CreateAsync(cancellation))).RequireRateLimiting("prototype");
app.MapGet("/v1/estate", async (HttpContext context, AccountStore store, ConnectionCache cache, CancellationToken cancellation) =>
    await Execute(context, store, cache, null, cancellation)).RequireRateLimiting("prototype");
app.MapPost("/v1/estate/commands", async (EstateCommand command, HttpContext context, AccountStore store, ConnectionCache cache, CancellationToken cancellation) =>
{
    if (!EstateApplication.IsValid(command))
    {
        return Results.BadRequest(new ApiError("invalid-command"));
    }
    return await Execute(context, store, cache, command, cancellation);
}).RequireRateLimiting("prototype");
app.MapGet("/v1/heroes/recruit/odds", async (HttpContext context, AccountStore store, RecruitStore recruit, CancellationToken cancellation) =>
    await Authenticated(context, store, async id => Results.Ok(await recruit.OddsAsync(id, cancellation)), cancellation)).RequireRateLimiting("prototype");
app.MapPost("/v1/heroes/recruit", async (RecruitCommand command, HttpContext context, AccountStore store, RecruitStore recruit, ConnectionCache cache, CancellationToken cancellation) =>
{
    if (!RecruitDraw.IsValid(command))
    {
        return Results.BadRequest(new ApiError("invalid-command"));
    }
    return await Authenticated(context, store, async id =>
    {
        string response = await recruit.RecruitAsync(id, command, cancellation);
        await cache.TouchAsync(id);
        return Results.Content(response, "application/json");
    }, cancellation);
}).RequireRateLimiting("prototype");
app.MapGet("/v1/research", async (HttpContext context, AccountStore store, ResearchStore research, CancellationToken cancellation) =>
    await Authenticated(context, store, async id => Results.Ok(await research.ViewAsync(id, cancellation)), cancellation)).RequireRateLimiting("prototype");
app.MapPost("/v1/research/commands", async (ResearchCommand command, HttpContext context, AccountStore store, ResearchStore research, ConnectionCache cache, CancellationToken cancellation) =>
{
    if (!ResearchRules.IsValid(command))
    {
        return Results.BadRequest(new ApiError("invalid-command"));
    }
    return await Authenticated(context, store, async id =>
    {
        string response = await research.ExecuteAsync(id, command, cancellation);
        await cache.TouchAsync(id);
        return Results.Content(response, "application/json");
    }, cancellation);
}).RequireRateLimiting("prototype");
app.Run();

static async Task<IResult> Authenticated(HttpContext context, AccountStore store, Func<Guid, Task<IResult>> action, CancellationToken cancellation)
{
    string header = context.Request.Headers.Authorization.ToString();
    Guid? id = header.StartsWith("Bearer ", StringComparison.Ordinal) ? await store.AuthenticateAsync(header[7..], cancellation) : null;
    if (id is null)
    {
        return Results.Unauthorized();
    }
    try
    {
        return await action(id.Value);
    }
    catch (CommandConflictException conflict)
    {
        return Results.Conflict(new ApiError(conflict.Message));
    }
}

static async Task<IResult> Execute(HttpContext context, AccountStore store, ConnectionCache cache, EstateCommand? command, CancellationToken cancellation)
{
    string header = context.Request.Headers.Authorization.ToString();
    Guid? id = header.StartsWith("Bearer ", StringComparison.Ordinal) ? await store.AuthenticateAsync(header[7..], cancellation) : null;
    if (id is null)
    {
        return Results.Unauthorized();
    }
    try
    {
        string response = await store.ExecuteAsync(id.Value, command, cancellation);
        await cache.TouchAsync(id.Value);
        return Results.Content(response, "application/json");
    }
    catch (CommandConflictException conflict)
    {
        return Results.Conflict(new ApiError(conflict.Message));
    }
}

public partial class Program { }
