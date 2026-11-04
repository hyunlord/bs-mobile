using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Host;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldRecoveryHttpTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; } = 1_793_520_000_000;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }

    private sealed class Factory(string postgres, Clock clock) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres, ["ConnectionStrings:Redis"] = "127.0.0.1:1",
                ["World:RecoveryBatchSize"] = "1",
                ["Domains:SeedKey"] = RecruitTests.TestKeyHex, ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.RemoveAll<WorldMapContent>();
                services.AddSingleton(WorldMapContent.Parse(WorldStoreTests.MapBytes()));
                // Disable only the recovery worker so HTTP admission observes a deterministic backlog.
                var worker = services.Single(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType == typeof(WorldRecoveryWorker));
                services.Remove(worker);
            });
        }
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ActualBacklogReturnsServiceUnavailableUntilBoundedRecoveryFinishes()
    {
        await using var database = await WorldTestDatabase.CreateAsync();
        var clock = new Clock();
        using var factory = new Factory(database.ConnectionString, clock);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var createResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);

        int barracks = created.Estate.Buildings.Single(building => building.Kind == "meta:barracks").Id;
        using var buildResponse = await client.PostAsJsonAsync("/v1/estate/commands",
            new EstateCommand(Guid.NewGuid(), created.Estate.Version, "start-construction", barracks));
        buildResponse.EnsureSuccessStatusCode();
        var building = (await buildResponse.Content.ReadFromJsonAsync<CommandReply>())!;
        Assert.True(building.Done);
        clock.Now += building.Estate.Construction!.RemainingSeconds * 1000;
        var built = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        using var trainResponse = await client.PostAsJsonAsync("/v1/account/commands",
            new AccountCommand(Guid.NewGuid(), built.Estate.Version, "training", "start",
                JsonSerializer.SerializeToElement(new TrainingStart("slg:levy", 1), TrainingContent.Json)));
        trainResponse.EnsureSuccessStatusCode();
        Assert.True((await trainResponse.Content.ReadFromJsonAsync<AccountReply>())!.Done);
        clock.Now += 20_000;
        var ready = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        var dispatch = new WorldMarchCommand(Guid.NewGuid(), ready.Estate.Version, "gather", "slg:test_grain", 1,
            "core:frontier_knight", [new("slg:levy", 1)]);
        using var dispatchResponse = await client.PostAsJsonAsync("/v1/world/marches/commands", dispatch);
        dispatchResponse.EnsureSuccessStatusCode();

        // Prototype sessions normally expire after a day. Keep this isolated test session valid
        // so a thirty-day jump exercises recovery admission, rather than authentication expiry.
        await using (var session = factory.Services.GetRequiredService<NpgsqlDataSource>()
            .CreateCommand("UPDATE sessions SET expires_at=$1"))
        {
            session.Parameters.AddWithValue(clock.GetUtcNow().AddDays(31));
            Assert.Equal(1, await session.ExecuteNonQueryAsync());
        }
        clock.Now += 30L * 86_400_000;
        using var unavailable = await client.GetAsync("/v1/world/marches");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("world-recovery-pending", (await unavailable.Content.ReadFromJsonAsync<ApiError>())!.Code);

        var marches = factory.Services.GetRequiredService<WorldMarchStore>();
        int batches = 0;
        WorldRecoveryBatchResult batch;
        do
        {
            batch = await marches.RecoverBatchAsync(CancellationToken.None);
            Assert.InRange(batch.ProcessedEvents, 0, 1);
            Assert.True(++batches < 20, "Bounded recovery must converge.");
        }
        while (batch.HasMore);
        using var available = await client.GetAsync("/v1/world/marches");
        Assert.Equal(HttpStatusCode.OK, available.StatusCode);
        Assert.Equal("returned", Assert.Single((await available.Content.ReadFromJsonAsync<WorldMarchView[]>())!).State);
        var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        Assert.Equal(0, account.Domains["training"].GetProperty("units")[0].GetProperty("deployed").GetInt32());
    }
}
