using Npgsql;
using SowSiege.Server.Application;
using Xunit;
using Xunit.Abstractions;

namespace SowSiege.Server.Tests;

public sealed partial class GatheringStoreTests
{
    private readonly ITestOutputHelper output;
    public GatheringStoreTests(ITestOutputHelper output) => this.output = output;
    private static string ConnectionTag() => "gather_lifetime_" + Guid.NewGuid().ToString("N");

    private async Task<NpgsqlConnection> ObserveAsync()
    {
        var settings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.")) { Pooling = false };
        var observer = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await observer.OpenAsync();
            await using var limit = new NpgsqlCommand("SHOW max_connections", observer);
            output.WriteLine("PostgreSQL max_connections={0}", await limit.ExecuteScalarAsync());
            return observer;
        }
        catch { await observer.DisposeAsync(); throw; }
    }

    private static async Task<long> ConnectionCountAsync(NpgsqlConnection observer, string tag, string condition = "true")
    {
        await using var query = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name IN ($1,$2) AND " + condition, observer);
        query.Parameters.AddWithValue(tag);
        query.Parameters.AddWithValue(tag + "-admin");
        return (long)(await query.ExecuteScalarAsync())!;
    }

    private async Task AssertReleasedAsync(Setup s, NpgsqlConnection observer, string tag)
    {
        // A leaked checked-out connection exhausts this fixture's one-slot pool while the process is still alive.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using (var lease = await s.Db.Source.OpenConnectionAsync(deadline.Token))
        {
            await using var query = new NpgsqlCommand("SELECT 1", lease);
            Assert.Equal(1, await query.ExecuteScalarAsync(deadline.Token));
        }
        long transactions = await ConnectionCountAsync(observer, tag, "xact_start IS NOT NULL");
        output.WriteLine("{0}: pool slot reusable; open transactions={1}", tag, transactions);
        Assert.Equal(0L, transactions);
    }

    private async Task WaitForConnectionCountAsync(NpgsqlConnection observer, string tag, long expected, string condition = "true")
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await ConnectionCountAsync(observer, tag, condition) != expected)
        {
            await Task.Delay(10, deadline.Token);
        }
        output.WriteLine("{0}: connection count={1}; condition={2}; test process still alive", tag, expected, condition);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task GatheringNormalConflictAndRollbackReturnTheOnlyPoolSlot()
    {
        string tag = ConnectionTag();
        await using var observer = await ObserveAsync();
        await using var s = await CreateAsync(1, tag);
        await s.Store.InitializeAsync(None);
        var arrival = Arrival(s);
        await s.Store.ArriveAsync(arrival, None);
        await AssertReleasedAsync(s, observer, tag);
        await Assert.ThrowsAsync<CommandConflictException>(() => s.Store.ArriveAsync(arrival with { Soldiers = 2 }, None));
        await AssertReleasedAsync(s, observer, tag);
        s.Time.Now += 90_000;
        await s.Store.CancelAsync(arrival.MarchId, s.Account, None);
        await AssertReleasedAsync(s, observer, tag);
        Assert.NotNull(await s.Store.FindAsync(arrival.MarchId, None));
        Assert.Equal(599, (await s.Store.NodeAsync(arrival.TargetId, None)).RemainingStock);
        s.Time.Now += 1000;
        await using (var inject = s.Db.Source.CreateCommand("CREATE FUNCTION lifetime_fail() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'lifetime-rollback'; END $$; CREATE TRIGGER lifetime_fail BEFORE INSERT ON outbox FOR EACH ROW EXECUTE FUNCTION lifetime_fail();"))
        { await inject.ExecuteNonQueryAsync(); }
        await Assert.ThrowsAsync<PostgresException>(() => s.Store.RecoverAsync(None));
        await AssertReleasedAsync(s, observer, tag);
        Assert.Equal("returning", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
        await using (var repair = s.Db.Source.CreateCommand("DROP TRIGGER lifetime_fail ON outbox"))
        { await repair.ExecuteNonQueryAsync(); }
        await s.Store.RecoverAsync(None);
        await s.Accounts.ExecuteAccountAsync(s.Account, null, None);
        await AssertReleasedAsync(s, observer, tag);
        Assert.Equal("settled", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CancellationWhileWaitingForWorldLockRollsBackAndReturnsPoolSlot()
    {
        string tag = ConnectionTag();
        await using var observer = await ObserveAsync();
        await using var s = await CreateAsync(1, tag);
        var settings = new NpgsqlConnectionStringBuilder(s.Db.ConnectionString) { Pooling = false, ApplicationName = tag + "-blocker" };
        await using var blocker = new NpgsqlConnection(settings.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = await blocker.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT world_id FROM world_instances WHERE world_id=$1 FOR UPDATE", blocker, transaction))
        {
            hold.Parameters.AddWithValue(s.Map.WorldId);
            await hold.ExecuteNonQueryAsync();
        }
        using var cancel = new CancellationTokenSource();
        var recovery = s.Store.RecoverAsync(cancel.Token);
        try
        {
            await WaitForConnectionCountAsync(observer, tag, 1, "wait_event_type='Lock'");
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recovery);
        }
        finally
        {
            cancel.Cancel();
            await transaction.RollbackAsync();
            try { await recovery; }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        }
        await AssertReleasedAsync(s, observer, tag);
        await s.Store.RecoverAsync(None);
        await AssertReleasedAsync(s, observer, tag);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task FixtureDisposalClosesTaggedPoolsBeforeTheTestProcessExits()
    {
        string tag = ConnectionTag();
        await using var observer = await ObserveAsync();
        await using (var s = await CreateAsync(1, tag))
        {
            await s.Store.NodeAsync("slg:test_grain", None);
            long before = await ConnectionCountAsync(observer, tag);
            output.WriteLine("{0}: before fixture disposal tagged connections={1}", tag, before);
            Assert.Equal(2L, before);
        }
        await WaitForConnectionCountAsync(observer, tag, 0);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task SetupCancellationClosesItsAdminPoolBeforeReturningTheFailure()
    {
        string tag = ConnectionTag();
        await using var observer = await ObserveAsync();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateAsync(1, tag, cancel.Token));
        await WaitForConnectionCountAsync(observer, tag, 0);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task SchemaCleanupFailureStillClosesBothFixturePools()
    {
        string tag = ConnectionTag();
        await using var observer = await ObserveAsync();
        var database = await WorldTestDatabase.CreateAsync(1, tag);
        try
        {
            string schema = new NpgsqlConnectionStringBuilder(database.ConnectionString).SearchPath!;
            await using (var connect = await database.Source.OpenConnectionAsync())
            { Assert.Equal(System.Data.ConnectionState.Open, connect.State); }
            // Remove only this fixture's generated schema, forcing its normal DROP to fail.
            await using (var drop = new NpgsqlCommand("DROP SCHEMA " + schema + " CASCADE", observer))
            { await drop.ExecuteNonQueryAsync(); }
        }
        finally { await Assert.ThrowsAsync<PostgresException>(() => database.DisposeAsync().AsTask()); }
        await WaitForConnectionCountAsync(observer, tag, 0);
    }
}
