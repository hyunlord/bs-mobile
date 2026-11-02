using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldMarchHttpTests
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
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres, ["ConnectionStrings:Redis"] = "127.0.0.1:1",
                ["World:RecoveryPollMilliseconds"] = "100",
                ["Domains:SeedKey"] = RecruitTests.TestKeyHex, ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex
            }));
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(clock);
                s.RemoveAll<WorldMapContent>(); s.AddSingleton(WorldMapContent.Parse(WorldStoreTests.MapBytes()));
            });
        }
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ActualHttpTrainingGatheringAndRestartReturnCreditOnceWithNoClientRewardInput()
    {
        await using var db = await WorldTestDatabase.CreateAsync();
        var clock = new Clock();
        string token;
        int walletBeforeReturn;
        WorldMarchCommand dispatch;
        string receipt;
        using (var host = new Factory(db.ConnectionString, clock))
        using (var client = host.CreateClient())
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/world/marches")).StatusCode);
            var created = (await (await client.PostAsJsonAsync("/prototype/accounts", new { })).Content.ReadFromJsonAsync<AccountCreated>())!;
            token = created.Token;
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            int barracks = created.Estate.Buildings.Single(b => b.Kind == "meta:barracks").Id;
            var building = (await (await client.PostAsJsonAsync("/v1/estate/commands",
                new EstateCommand(Guid.NewGuid(), created.Estate.Version, "start-construction", barracks))).Content.ReadFromJsonAsync<CommandReply>())!;
            Assert.True(building.Done);
            clock.Now += building.Estate.Construction!.RemainingSeconds * 1000;
            var built = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            var training = new AccountCommand(Guid.NewGuid(), built.Estate.Version, "training", "start",
                System.Text.Json.JsonSerializer.SerializeToElement(new TrainingStart("slg:levy", 1), TrainingContent.Json));
            var trained = (await (await client.PostAsJsonAsync("/v1/account/commands", training)).Content.ReadFromJsonAsync<AccountReply>())!;
            Assert.True(trained.Done);
            clock.Now += 20000;
            var ready = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            dispatch = new(Guid.NewGuid(), ready.Estate.Version, "gather", "slg:test_grain", 1, "core:frontier_knight", [new("slg:levy", 1)]);
            var dispatched = await client.PostAsJsonAsync("/v1/world/marches/commands", dispatch);
            dispatched.EnsureSuccessStatusCode();
            receipt = await dispatched.Content.ReadAsStringAsync();
            Assert.DoesNotContain("snapshot", receipt, StringComparison.OrdinalIgnoreCase);
            var march = Assert.Single((await client.GetFromJsonAsync<WorldMarchView[]>("/v1/world/marches"))!);
            clock.Now = march.DueAtMs;
            // Read the ledger directly: an HTTP query would itself perform recovery and mask a missing worker.
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await using var probe = host.Services.GetRequiredService<NpgsqlDataSource>().CreateCommand("SELECT state FROM world_marches WHERE march_id=$1");
                probe.Parameters.AddWithValue(dispatch.CommandId);
                while ((string?)await probe.ExecuteScalarAsync(timeout.Token) == "outbound")
                {
                    await Task.Delay(25, timeout.Token);
                }
            }
            Assert.Equal("gathering", Assert.Single((await client.GetFromJsonAsync<WorldMarchView[]>("/v1/world/marches"))!).State);
            var visit = (await host.Services.GetRequiredService<GatheringStore>().FindAsync(dispatch.CommandId, CancellationToken.None))!;
            clock.Now = visit.DueAtMs!.Value;
            Assert.Equal("returning", Assert.Single((await client.GetFromJsonAsync<WorldMarchView[]>("/v1/world/marches"))!).State);
            var before = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            walletBeforeReturn = before.Estate.Wallet["meta:grain"];
            Assert.Equal(1, before.Domains["training"].GetProperty("units")[0].GetProperty("deployed").GetInt32());
        }
        clock.Now += 3_600_000;
        using var restarted = new Factory(db.ConnectionString, clock);
        using var reconnect = restarted.CreateClient();
        reconnect.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var after = (await reconnect.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        Assert.Equal(walletBeforeReturn + 10, after.Estate.Wallet["meta:grain"]);
        Assert.Equal(0, after.Domains["training"].GetProperty("units")[0].GetProperty("deployed").GetInt32());
        Assert.Equal("returned", Assert.Single((await reconnect.GetFromJsonAsync<WorldMarchView[]>("/v1/world/marches"))!).State);
        var duplicate = await reconnect.PostAsJsonAsync("/v1/world/marches/commands", dispatch);
        duplicate.EnsureSuccessStatusCode();
        Assert.Equal(receipt, await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(walletBeforeReturn + 10, (await reconnect.GetFromJsonAsync<AccountReply>("/v1/account"))!.Estate.Wallet["meta:grain"]);
    }
}
