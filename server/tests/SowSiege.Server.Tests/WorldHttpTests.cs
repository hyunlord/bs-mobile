using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldHttpTests
{
    internal sealed class Factory(string postgres, WorldMapContent? content = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres, ["ConnectionStrings:Redis"] = "127.0.0.1:1",
                ["Domains:SeedKey"] = RecruitTests.TestKeyHex, ["Prototype:RecruitGems"] = "3000",
                ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex
            }));
            if (content is not null)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<WorldMapContent>();
                    services.AddSingleton(content);
                });
            }
        }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AuthenticationBoundsAndRepeatCityRequestsProtectPublicAoi()
    {
        await using var database = await WorldTestDatabase.CreateAsync();
        using var factory = new Factory(database.ConnectionString);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/world/city")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/world/city", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/world/chunks/0/0")).StatusCode);

        var createdResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var city = (await client.GetFromJsonAsync<WorldTargetView>("/v1/world/city"))!;
        Assert.Equal("city", city.Kind);
        Assert.NotNull(city.AccountId);
        var repeated = await client.PostAsJsonAsync("/v1/world/city", new { });
        repeated.EnsureSuccessStatusCode();
        Assert.Equal(city, await repeated.Content.ReadFromJsonAsync<WorldTargetView>());

        var map = factory.Services.GetRequiredService<WorldMapContent>();
        var local = (await client.GetFromJsonAsync<WorldChunkView>($"/v1/world/chunks/{city.X / map.ChunkSize}/{city.Y / map.ChunkSize}"))!;
        Assert.Equal(map.WorldId, local.WorldId);
        Assert.Equal(map.Hash, local.ContentHash);
        Assert.Equal(map.ChunkSize * map.ChunkSize, local.Tiles.Length);
        Assert.Contains(local.Targets, t => t.TargetId == city.TargetId && t.Generation == city.Generation);
        string json = JsonSerializer.Serialize(local, AccountStore.Json);
        Assert.DoesNotContain("wallet", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("publicKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);

        int lastX = map.Width / map.ChunkSize - 1;
        int lastY = map.Height / map.ChunkSize - 1;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/v1/world/chunks/{lastX}/{lastY}")).StatusCode);
        foreach (string path in new[] { "-1/0", "0/-1", $"{lastX + 1}/0", $"0/{lastY + 1}", $"{int.MaxValue}/{int.MaxValue}" })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/v1/world/chunks/" + path)).StatusCode);
        }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task GuestKeyAccountGetsACityBeforeItsFirstWorldRequest()
    {
        await using var database = await WorldTestDatabase.CreateAsync();
        using var factory = new Factory(database.ConnectionString, WorldMapContent.Parse(WorldStoreTests.MapBytes()));
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var response = await client.PostAsJsonAsync("/v1/auth/guest", new { publicKey = Convert.ToBase64String(device.ExportSubjectPublicKeyInfo()) });
        response.EnsureSuccessStatusCode();
        var guest = (await response.Content.ReadFromJsonAsync<AuthTokens>())!;
        var world = factory.Services.GetRequiredService<WorldStore>();
        Assert.NotNull(await world.FindCityAsync(guest.AccountId, CancellationToken.None));
        client.DefaultRequestHeaders.Authorization = new("Bearer", guest.AccessToken);
        var city = (await client.GetFromJsonAsync<WorldTargetView>("/v1/world/city"))!;
        Assert.Equal(guest.AccountId, city.AccountId);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task FullWorldReturnsConflictForPrototypeAndGuestWithoutOrphanAccounts()
    {
        await using var database = await WorldTestDatabase.CreateAsync();
        using var factory = new Factory(database.ConnectionString, WorldMapContent.Parse(WorldStoreTests.MapBytes(1, 1)));
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        (await client.PostAsJsonAsync("/prototype/accounts", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/prototype/accounts", new { })).StatusCode);
        using var device = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/v1/auth/guest", new { publicKey = Convert.ToBase64String(device.ExportSubjectPublicKeyInfo()) })).StatusCode);
        await using var count = database.Source.CreateCommand("SELECT (SELECT count(*) FROM accounts),(SELECT count(*) FROM sessions),(SELECT count(*) FROM auth_device_keys)");
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(0L, reader.GetInt64(2));
    }
}
