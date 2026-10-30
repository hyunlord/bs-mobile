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
builder.Services.AddSingleton(services => TrainingContent.Load(services.GetRequiredService<IConfiguration>()["GameContentRoot"]
    ?? throw new InvalidOperationException("GameContentRoot required."), services.GetRequiredService<EstateContent>()));
builder.Services.AddSingleton<IDomainHandler, TrainingHandler>();
builder.Services.AddSingleton(services => NpgsqlDataSource.Create(services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres") ?? throw new InvalidOperationException("Postgres required.")));
builder.Services.AddSingleton<DomainRegistry>();
builder.Services.AddSingleton<AccountStore>();
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
app.MapGet("/v1/account", async (HttpContext context, AccountStore store, CancellationToken cancellation) =>
    await ExecuteAccount(context, store, null, cancellation)).RequireRateLimiting("prototype");
app.MapPost("/v1/account/commands", async (AccountCommand command, HttpContext context, AccountStore store, CancellationToken cancellation) =>
{
    if (command.CommandId == Guid.Empty || command.ExpectedVersion < 0 || string.IsNullOrWhiteSpace(command.Domain) ||
        string.IsNullOrWhiteSpace(command.Operation) || command.Payload.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        return Results.BadRequest(new ApiError("invalid-command"));
    }
    return await ExecuteAccount(context, store, command, cancellation);
}).RequireRateLimiting("prototype");
app.Run();

static async Task<IResult> ExecuteAccount(HttpContext context, AccountStore store, AccountCommand? command, CancellationToken cancellation)
{
    string header = context.Request.Headers.Authorization.ToString();
    Guid? id = header.StartsWith("Bearer ", StringComparison.Ordinal) ? await store.AuthenticateAsync(header[7..], cancellation) : null;
    if (id is null)
    {
        return Results.Unauthorized();
    }
    try
    {
        return Results.Content(await store.ExecuteAccountAsync(id.Value, command, cancellation), "application/json");
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
