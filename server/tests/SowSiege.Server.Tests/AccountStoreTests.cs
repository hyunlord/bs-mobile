using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class AccountStoreTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task CommandsAreAtomicIdempotentAndSurviveStoreReplacement()
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required for integration tests.");
        await using var source = NpgsqlDataSource.Create(connection);
        var time = new ManualClock();
        var app = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
        var store = new AccountStore(source, app, time);
        await store.InitializeAsync(CancellationToken.None);
        var created = await store.CreateAsync(CancellationToken.None);
        Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        int granary = created.Estate.Buildings.Single(b => b.Kind == "meta:granary").Id;
        var command = new EstateCommand(Guid.NewGuid(), 0, "start-construction", granary);
        string first = await store.ExecuteAsync(id, command, CancellationToken.None);
        Assert.True(JsonSerializer.Deserialize<CommandReply>(first, AccountStore.Json)!.Done);
        time.Now = time.Now.AddSeconds(80);
        Assert.Equal(first, await store.ExecuteAsync(id, command, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => store.ExecuteAsync(id, command with { BuildingId = int.MaxValue }, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => store.ExecuteAsync(id, command with { CommandId = Guid.NewGuid() }, CancellationToken.None));
        var replaced = new AccountStore(source, app, time);
        Assert.Equal(id, await replaced.AuthenticateAsync(created.Token, CancellationToken.None));
        string next = await replaced.ExecuteAsync(id, null, CancellationToken.None);
        var snapshot = JsonSerializer.Deserialize<CommandReply>(next, AccountStore.Json)!.Estate;
        Assert.Equal(1, snapshot.Buildings.Single(b => b.Id == granary).Level);
        Assert.Equal(2, snapshot.Version);
        await using var counts = source.CreateCommand("SELECT count(*) FROM commands WHERE account_id=$1");
        counts.Parameters.AddWithValue(id);
        Assert.Equal(1L, await counts.ExecuteScalarAsync());
        time.Now = time.Now.AddDays(2);
        Assert.Null(await replaced.AuthenticateAsync(created.Token, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentCommandsHaveOneWinnerAndLateWriteFailureRollsBack()
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
        await using var source = NpgsqlDataSource.Create(connection);
        var app = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
        var store = new AccountStore(source, app, new ManualClock());
        await store.InitializeAsync(CancellationToken.None);
        var created = await store.CreateAsync(CancellationToken.None);
        Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        int granary = created.Estate.Buildings.Single(b => b.Kind == "meta:granary").Id;
        var command = new EstateCommand(Guid.NewGuid(), 0, "start-construction", granary);
        // Force a late transaction failure without adding any production fault endpoint.
        await using var collision = source.CreateCommand("INSERT INTO outbox VALUES($1,1,'test-collision')");
        collision.Parameters.AddWithValue(id);
        await collision.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<PostgresException>(() => store.ExecuteAsync(id, command, CancellationToken.None));
        await using var state = source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
        state.Parameters.AddWithValue(id);
        Assert.Equal(0L, await state.ExecuteScalarAsync());
        await using var clear = source.CreateCommand("DELETE FROM outbox WHERE account_id=$1");
        clear.Parameters.AddWithValue(id);
        await clear.ExecuteNonQueryAsync();
        async Task<bool> Attempt(EstateCommand input)
        {
            try
            {
                await store.ExecuteAsync(id, input, CancellationToken.None);
                return true;
            }
            catch (CommandConflictException)
            {
                return false;
            }
        }
        var results = await Task.WhenAll(Attempt(command), Attempt(command with { CommandId = Guid.NewGuid() }));
        Assert.Single(results, won => won);
        Assert.Equal(1L, await state.ExecuteScalarAsync());
    }

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
