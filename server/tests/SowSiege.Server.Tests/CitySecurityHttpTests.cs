using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

[Trait("Category", "Postgres")]
public sealed class CitySecurityHttpTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 5, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Factory(Clock clock, string postgres) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres,
                ["ConnectionStrings:Redis"] = "127.0.0.1:1", ["Domains:SeedKey"] = RecruitTests.TestKeyHex,
                ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex,
            }));
        }
    }

    [Fact]
    public async Task ShieldApiUsesServerCreationClockAndRejectsClientDurations()
    {
        var clock = new Clock();
        await using var isolated = await WorldTestDatabase.CreateAsync();
        using var factory = new Factory(clock, isolated.ConnectionString);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/world/shield")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/world/shield", new { commandId = Guid.NewGuid() })).StatusCode);
        var created = (await (await client.PostAsJsonAsync("/prototype/accounts", new { })).Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var policy = CitySecurityPolicy.Load(ContentEstateTests.Root());
        var shield = (await client.GetFromJsonAsync<CityShieldState>("/v1/world/shield"))!;
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds() + policy.NewShieldMilliseconds, shield.ShieldUntilMs);
        var rejected = (await (await client.PostAsJsonAsync("/v1/world/shield", new { commandId = Guid.NewGuid() })).Content.ReadFromJsonAsync<CityShieldReply>())!;
        Assert.False(rejected.Done);
        Assert.Equal("shield-active", rejected.Reason);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/world/shield",
            new { commandId = Guid.NewGuid(), durationMilliseconds = 1, now = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/world/shield", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/world/scout/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(shield.ShieldUntilMs, (await client.GetFromJsonAsync<CityShieldState>("/v1/world/shield"))!.ShieldUntilMs);
    }
}
