using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldAoiConnectionTests
{
    [Theory, Trait("Category", "Postgres")]
    [InlineData(1, 1)]
    [InlineData(4, 4)]
    public async Task AuthenticatedChunkRequestsDoNotBorrowASecondBarrierConnection(int pool, int concurrency)
    {
        await using var database = await WorldTestDatabase.CreateAsync(pool);
        using var factory = new WorldHttpTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        var created = await factory.Services.GetRequiredService<AccountStore>().CreateAsync(CancellationToken.None);
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
        {
            using var response = await client.GetAsync("/v1/world/chunks/0/0");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        })).WaitAsync(TimeSpan.FromSeconds(15));
        using var next = await client.GetAsync("/v1/world/chunks/0/0");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CallerTransactionRemainsUsableAndCancellationReturnsItsConnection()
    {
        await using var database = await WorldTestDatabase.CreateAsync(1);
        using var factory = new WorldHttpTests.Factory(database.ConnectionString);
        using var client = factory.CreateClient();
        var world = factory.Services.GetRequiredService<WorldStore>();
        var source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        var standalone = await world.ChunkAsync(0, 0, CancellationToken.None);
        await using (var connection = await source.OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            var shared = await world.ChunkAsync(connection, transaction, 0, 0, CancellationToken.None);
            Assert.Equal(JsonSerializer.Serialize(standalone), JsonSerializer.Serialize(shared));
            await using var alive = new NpgsqlCommand("SELECT 1", connection, transaction);
            Assert.Equal(1, await alive.ExecuteScalarAsync());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.ChunkAsync(connection, transaction, 0, 0, canceled.Token));
            await transaction.RollbackAsync();
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var returned = await source.OpenConnectionAsync(deadline.Token);
        await using var probe = new NpgsqlCommand("SELECT 1", returned);
        Assert.Equal(1, await probe.ExecuteScalarAsync(deadline.Token));
    }
}
