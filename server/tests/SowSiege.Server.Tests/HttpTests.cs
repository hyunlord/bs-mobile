using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Server.Infrastructure;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class HttpTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AuthenticationAndStrictPayloadProtectServerState()
    {
        using var factory = new PrototypeFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
        var createdResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var store = factory.Services.GetRequiredService<AccountStore>();
        var source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        Guid account = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        async Task<string> StoredClockAndReceipts()
        {
            await using var query = source.CreateCommand("SELECT version,utc_highwater,logical_seconds,fraction_ms,(SELECT count(*) FROM commands WHERE account_id=$1) FROM accounts WHERE id=$1");
            query.Parameters.AddWithValue(account);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return string.Join(":", Enumerable.Range(0, 5).Select(reader.GetValue));
        }
        string before = await StoredClockAndReceipts();
        var missingVersion = new { commandId = Guid.NewGuid(), type = "claim-production" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/estate/commands", missingVersion)).StatusCode);
        Assert.Equal(before, await StoredClockAndReceipts());
        var forged = new { commandId = Guid.NewGuid(), expectedVersion = 0, type = "claim-production", wallet = 999999, now = 999999 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/estate/commands", forged)).StatusCode);
        var missingDomainVersion = new { commandId = Guid.NewGuid(), domain = "test", operation = "start", payload = new { } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", missingDomainVersion)).StatusCode);
        var scalar = new { commandId = Guid.NewGuid(), expectedVersion = 0, domain = "test", operation = "start", payload = 42 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", scalar)).StatusCode);
        var unknown = new { commandId = Guid.NewGuid(), expectedVersion = 0, domain = "test", operation = "start", payload = new { }, wallet = 999 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", unknown)).StatusCode);
        Assert.Equal(before, await StoredClockAndReceipts());
        var refreshed = await client.GetFromJsonAsync<CommandReply>("/v1/estate");
        Assert.Equal(1, refreshed!.Estate.Version);
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('0', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public void ProductionCannotStartPrototypeEndpoints()
    {
        using var factory = new PrototypeFactory("Production");
        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("requires Development", failure.Message);
    }

    private sealed class PrototypeFactory(string environment = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            string postgres = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres,
                ["ConnectionStrings:Redis"] = "127.0.0.1:1"
            }));
        }
    }
}
