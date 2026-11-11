using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class PurgeAdminHttpTests
{
    private sealed class Factory(string connection, bool enabled) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Prototype:Enabled"]="true", ["GameContentRoot"]=ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"]=connection,["ConnectionStrings:Redis"]="127.0.0.1:1",
                ["Domains:SeedKey"]=RecruitTests.TestKeyHex,["Auth:PseudonymKey"]=RecruitTests.TestKeyHex,
                ["Auth:PurgeAdmin:Enabled"]=enabled.ToString(),["Auth:PurgeAdmin:Secret"]=new string('a',64),
            }));
        }
    }
    [Theory, Trait("Category","Postgres")]
    [InlineData(false)] [InlineData(true)]
    public async Task AdminRouteIsOptInAndNeverUsesUserBearer(bool enabled)
    {
        string cs=Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("Postgres required");
        string schema="purge_admin_"+Guid.NewGuid().ToString("N");
        await using(var db=NpgsqlDataSource.Create(cs))
        await using(var create=db.CreateCommand("CREATE SCHEMA "+schema)) { await create.ExecuteNonQueryAsync(); }
        using var factory=new Factory(new NpgsqlConnectionStringBuilder(cs){SearchPath=schema}.ConnectionString,enabled);
        using var client=factory.CreateClient();
        client.DefaultRequestHeaders.Authorization=new("Bearer","not-admin");
        Assert.Equal(enabled?HttpStatusCode.Unauthorized:HttpStatusCode.NotFound,(await client.GetAsync("/v1/admin/account-purge")).StatusCode);
        if(!enabled) { return; }
        client.DefaultRequestHeaders.Add("X-Purge-Admin-Key",new string('b',64));
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/v1/admin/account-purge")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Purge-Admin-Key");
        client.DefaultRequestHeaders.Add("X-Purge-Admin-Key",new string('a',64));
        var reply=await client.GetAsync("/v1/admin/account-purge");
        Assert.Equal(HttpStatusCode.OK,reply.StatusCode);
        Assert.True(reply.Headers.CacheControl?.NoStore);
        using var json=JsonDocument.Parse(await reply.Content.ReadAsStringAsync());
        Assert.Equal(new[]{"duePending","failed","gracePending","lastSucceededAt","recovered","retryFinished","retryReady","retryWaiting","succeeded"},
            json.RootElement.EnumerateObject().Select(p=>p.Name).Order().ToArray());
        client.DefaultRequestHeaders.Add("X-Purge-Admin-Key",new string('a',64));
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/v1/admin/account-purge")).StatusCode);
    }
}
