using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Host;

public sealed record WorldCombatChoiceRequest(string Choice);
public sealed record WorldCombatInputRequest(short X, short Y);
public sealed record WorldCombatCommandRequest(long Sequence, int Tick, ReplayCommandKind Kind,
    WorldCombatInputRequest? Input = null, string? CardId = null, int Value = 0)
{
    public ReplayCommand ToCommand() => new(Sequence, Tick, Kind,
        Input is null ? default : new PlayerInput(Input.X, Input.Y), CardId, Value);
}
public sealed record WorldCombatPrefixRequest(WorldCombatCommandRequest[] Commands);
public sealed record WorldCombatView(Guid RunId, Guid MarchId, string TargetId, long Generation,
    string EncounterId, string Status, long Revision, long DueAtMs, WorldCombatIssuedContract? Contract,
    ReplayCommand[] Commands, WorldInputFrontier Frontier, WorldCombatResult? Result, DomainCredit[] Credits)
{
    public static WorldCombatView From(WorldCombatRun run) => new(run.RunId, run.MarchId, run.TargetId, run.Generation,
        run.EncounterId, run.Status, run.Revision, run.DueAtMs, run.Contract, run.Commands, run.Frontier, run.Result, run.Credits);
}

public static class WorldCombatEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter<ReplayCommandKind>(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static IServiceCollection AddWorldCombat(this IServiceCollection services)
    {
        services.AddSingleton(p => WorldCombatContent.Load(p.GetRequiredService<IConfiguration>()["GameContentRoot"]!));
        services.AddSingleton(p =>
        {
            var configuration = p.GetRequiredService<IConfiguration>();
            var root = configuration["GameContentRoot"]!;
            return new WorldCombatExecutor(new(Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root,
                "wave-1a", ReplayLimit(configuration), TimeSpan.FromSeconds(configuration.GetValue("WorldCombat:VerifySeconds", 30)),
                configuration.GetValue("WorldCombat:MaxConcurrentVerifications", 1), PlayTimeToleranceMilliseconds: 0));
        });
        services.AddSingleton(p => new WorldCombatContractFactory(p.GetRequiredService<WorldCombatContent>(),
            p.GetRequiredService<WorldCombatExecutor>().Catalog, p.GetRequiredService<WorldCombatExecutor>().DataHash, "wave-1a"));
        services.AddSingleton<WorldCombatStore>();
        services.AddSingleton<IWorldMarchActivity, WorldCombatActivity>();
        return services;
    }

    public static async Task InitializeWorldCombatAsync(this WebApplication app) =>
        await app.Services.GetRequiredService<WorldCombatStore>().InitializeAsync(app.Lifetime.ApplicationStopping);

    public static void MapWorldCombat(this WebApplication app)
    {
        app.MapGet("/v1/world/marches/{marchId:guid}/combat", (Guid marchId, HttpContext context, CancellationToken ct) =>
            Execute(context, async (account, store, marches) =>
            {
                await marches.RecoverAsync(ct);
                var run = await store.GetAsync(account, marchId, ct);
                return run is null ? Results.NotFound(new ApiError("combat-not-found")) : Results.Ok(WorldCombatView.From(run));
            }, ct)).RequireRateLimiting("prototype");
        app.MapPost("/v1/world/marches/{marchId:guid}/combat/choice", (Guid marchId, HttpContext context, CancellationToken ct) =>
            Execute(context, async (account, store, marches) =>
            {
                var request = Deserialize<WorldCombatChoiceRequest>(await ReadBody(context, 8192, ct));
                if (request.Choice is not ("direct" or "auto" or "return"))
                {
                    return Results.BadRequest(new ApiError("invalid-combat-choice"));
                }

                return Results.Ok(WorldCombatView.From(await store.ChooseAsync(marches, account, marchId, request.Choice, ct)));
            }, ct)).RequireRateLimiting("prototype");
        app.MapPost("/v1/world/marches/{marchId:guid}/combat/prefix", (Guid marchId, HttpContext context, CancellationToken ct) =>
            Execute(context, async (account, store, marches) =>
            {
                var request = Deserialize<WorldCombatPrefixRequest>(await ReadBody(context, ReplayLimit(context.RequestServices.GetRequiredService<IConfiguration>()), ct));
                if (request.Commands is null || request.Commands.Any(x => x is null))
                {
                    return Results.BadRequest(new ApiError("invalid-combat-prefix"));
                }

                return Results.Ok(WorldCombatView.From(await store.PrefixAsync(marches, account, marchId, request.Commands.Select(x => x.ToCommand()).ToArray(), ct)));
            }, ct)).RequireRateLimiting("prototype");
        app.MapPost("/v1/world/marches/{marchId:guid}/combat/replay", (Guid marchId, HttpContext context, CancellationToken ct) =>
            Execute(context, async (account, store, marches) => Results.Ok(WorldCombatView.From(await store.SubmitAsync(marches, account, marchId,
                await ReadBody(context, ReplayLimit(context.RequestServices.GetRequiredService<IConfiguration>()), ct), ct))), ct))
            .RequireRateLimiting("prototype");
    }

    private static int ReplayLimit(IConfiguration configuration)
    {
        var limit = configuration.GetValue("WorldCombat:MaxReplayBytes", 4 * 1024 * 1024);
        return limit > 0 ? limit : throw new InvalidOperationException("WorldCombat:MaxReplayBytes must be positive.");
    }

    private static T Deserialize<T>(byte[] bytes) where T : class =>
        JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new JsonException("Missing request.");

    private static async Task<byte[]> ReadBody(HttpContext context, int maximum, CancellationToken ct)
    {
        if (context.Request.ContentLength > maximum)
        {
            throw new BadHttpRequestException("Request too large.", StatusCodes.Status413PayloadTooLarge);
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = maximum;
        }

        using var output = new MemoryStream();
        var buffer = new byte[Math.Min(8192, maximum)];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximum)
            {
                throw new BadHttpRequestException("Request too large.", StatusCodes.Status413PayloadTooLarge);
            }

            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static async Task<IResult> Execute(HttpContext context,
        Func<Guid, WorldCombatStore, WorldMarchStore, Task<IResult>> action, CancellationToken ct)
    {
        var accounts = context.RequestServices.GetRequiredService<AccountStore>();
        var header = context.Request.Headers.Authorization.ToString();
        Guid? account = header.StartsWith("Bearer ", StringComparison.Ordinal) ? await accounts.AuthenticateAsync(header[7..], ct) : null;
        if (account is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return await action(account.Value, context.RequestServices.GetRequiredService<WorldCombatStore>(), context.RequestServices.GetRequiredService<WorldMarchStore>());
        }
        catch (KeyNotFoundException) { return Results.NotFound(new ApiError("combat-not-found")); }
        catch (CommandConflictException) { return Results.Conflict(new ApiError("combat-conflict")); }
        catch (WorldCombatRecoveryRequiredException) { return Results.Conflict(new ApiError("combat-recovery-required")); }
        catch (TimeoutException) { return Results.Conflict(new ApiError("combat-verification-pending")); }
        catch (BadHttpRequestException error) { return Results.Json(new ApiError("invalid-combat-body"), statusCode: error.StatusCode); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or OverflowException or EndOfStreamException or InvalidOperationException)
        { return Results.BadRequest(new ApiError("invalid-combat-request")); }
    }
}
