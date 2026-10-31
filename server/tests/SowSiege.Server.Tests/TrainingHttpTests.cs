using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingHttpTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task BarracksCommandsCompleteExactlyOnceAcrossConcurrentRequestsAndHostRestart()
    {
        var time = new TrainingClock();
        string token;
        Guid accountId;
        int grainAfterBuilding;
        int timberAfterBuilding;
        using (var factory = new TrainingFactory(time))
        using (var client = factory.CreateClient())
        {
            var createdResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
            createdResponse.EnsureSuccessStatusCode();
            var created = (await createdResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
            token = created.Token;
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            int barracks = created.Estate.Buildings.Single(b => b.Kind == "meta:barracks").Id;
            var construction = await client.PostAsJsonAsync("/v1/estate/commands",
                new EstateCommand(Guid.NewGuid(), created.Estate.Version, "start-construction", barracks));
            construction.EnsureSuccessStatusCode();
            var startedBuilding = (await construction.Content.ReadFromJsonAsync<CommandReply>())!;
            Assert.True(startedBuilding.Done, startedBuilding.Reason);
            time.Now = time.Now.AddSeconds(startedBuilding.Estate.Construction!.RemainingSeconds);
            var built = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            Assert.Equal(1, built.Domains["training"].GetProperty("barracksLevel").GetInt32());
            grainAfterBuilding = built.Estate.Wallet["meta:grain"];
            timberAfterBuilding = built.Estate.Wallet["meta:timber"];

            var first = new AccountCommand(Guid.NewGuid(), built.Estate.Version, "training", "start",
                JsonSerializer.SerializeToElement(new TrainingStart("slg:levy", 2), TrainingContent.Json));
            var firstResponse = await client.PostAsJsonAsync("/v1/account/commands", first);
            firstResponse.EnsureSuccessStatusCode();
            string firstReceipt = await firstResponse.Content.ReadAsStringAsync();
            var firstReply = JsonSerializer.Deserialize<AccountReply>(firstReceipt, AccountStore.Json)!;
            Assert.True(firstReply.Done, firstReply.Reason);
            Assert.Equal(2, firstReply.Domains["training"].GetProperty("occupied").GetInt32());
            var duplicate = await client.PostAsJsonAsync("/v1/account/commands", first);
            duplicate.EnsureSuccessStatusCode();
            Assert.Equal(firstReceipt, await duplicate.Content.ReadAsStringAsync());

            var queue = first with
            {
                CommandId = Guid.NewGuid(), ExpectedVersion = firstReply.Estate.Version,
                Payload = JsonSerializer.SerializeToElement(new TrainingStart("slg:levy", 1), TrainingContent.Json)
            };
            var competitors = await Task.WhenAll(client.PostAsJsonAsync("/v1/account/commands", queue),
                client.PostAsJsonAsync("/v1/account/commands", queue with { CommandId = Guid.NewGuid() }));
            Assert.Single(competitors, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(competitors, r => r.StatusCode == HttpStatusCode.Conflict);
            var winner = (await competitors.Single(r => r.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<AccountReply>())!;
            Assert.True(winner.Done, winner.Reason);
            Assert.Equal(3, winner.Domains["training"].GetProperty("occupied").GetInt32());
            Assert.Equal(JsonValueKind.Null, winner.Domains["training"].GetProperty("waiting").GetProperty("startedAtMilliseconds").ValueKind);
            foreach (var response in competitors) { response.Dispose(); }
            var store = factory.Services.GetRequiredService<AccountStore>();
            accountId = (await store.AuthenticateAsync(token, CancellationToken.None))!.Value;
        }

        time.Now = time.Now.AddHours(2);
        using var restarted = new TrainingFactory(time);
        using var reconnected = restarted.CreateClient();
        reconnected.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var completed = (await reconnected.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        var view = completed.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.Equal(3, view.Units.Single(u => u.UnitId == "slg:levy").Ready);
        Assert.All(view.Units, u => Assert.Equal(0, u.Reserved));
        Assert.Null(view.Active);
        Assert.Null(view.Waiting);
        Assert.Equal(grainAfterBuilding - 24, completed.Estate.Wallet["meta:grain"]);
        Assert.Equal(timberAfterBuilding - 9, completed.Estate.Wallet["meta:timber"]);
        var repeated = (await reconnected.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        Assert.Equal(3, repeated.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!.Units.Sum(u => u.Ready));
        var source = restarted.Services.GetRequiredService<NpgsqlDataSource>();
        await using var records = source.CreateCommand("SELECT (SELECT count(*) FROM commands WHERE account_id=$1),(SELECT count(*) FROM domain_events WHERE account_id=$1 AND domain='training' AND kind='training-completed')");
        records.Parameters.AddWithValue(accountId);
        await using var reader = await records.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3, reader.GetInt64(0));
        Assert.Equal(2, reader.GetInt64(1));
    }

    private sealed class TrainingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TrainingFactory(TrainingClock time) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            string postgres = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres, ["ConnectionStrings:Redis"] = "127.0.0.1:1",
                ["Domains:SeedKey"] = RecruitTests.TestKeyHex
            }));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(time));
        }
    }
}
