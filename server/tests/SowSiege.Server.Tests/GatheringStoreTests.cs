using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class GatheringStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private sealed class Clock : TimeProvider
    {
        public long Now = 1_793_520_000_000;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
    private sealed record Setup(WorldTestDatabase Db, Clock Time, WorldMapContent Map, WorldStore World,
        AccountStore Accounts, GatheringContent Content, GatheringStore Store, Guid Account) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private static async Task<Setup> CreateAsync()
    {
        var db = await WorldTestDatabase.CreateAsync();
        var clock = new Clock();
        var map = WorldMapContent.Parse(WorldStoreTests.MapBytes());
        var world = new WorldStore(db.Source, map);
        var accounts = new AccountStore(db.Source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock, world: world);
        await accounts.InitializeAsync(None);
        await new AllianceStore(db.Source, AllianceContent.Load(ContentEstateTests.Root()), clock).InitializeAsync(None);
        await new AuthStore(db.Source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true), clock).InitializeAsync(None);
        await world.InitializeAsync(None);
        // Fixture target uses slg:grain rather than shipped meta:grain; its material mapping is explicit.
        var content = GatheringContent.Parse(JsonSerializer.SerializeToUtf8Bytes(new {
            id = "slg:gathering_v1", kind = "slg-gathering", schemaVersion = 1, status = "hypothesis", numericStatus = "[가설]",
            sources = new[] { "docs/design/gdd/18-slg-world-map-proposal.md" },
            policy = new { stock = 600, unitsPerSoldierMinute = 1, capacityPerSoldier = 10, respawnMilliseconds = 600_000 },
            resources = new[] { new { contentId = "slg:grain", materialId = "meta:grain" } }
        }));
        var store = new GatheringStore(db.Source, world, map, content, accounts, clock);
        await store.InitializeAsync(None);
        var created = await accounts.CreateAsync(None);
        return new(db, clock, map, world, accounts, content, store, (await accounts.AuthenticateAsync(created.Token, None))!.Value);
    }
    private static GatheringArrival Arrival(Setup s, int soldiers = 1, long travel = 1000) =>
        new(Guid.NewGuid(), s.Account, "slg:test_grain", 1, soldiers, travel);

    [Fact, Trait("Category", "Postgres")]
    public async Task ConcurrentArrivalsGrantOneLeaseAndRetriesNeverAcquireAgain()
    {
        await using var s = await CreateAsync();
        var arrivals = Enumerable.Range(0, 12).Select(_ => Arrival(s)).ToArray();
        var results = await Task.WhenAll(arrivals.Select(a => s.Store.ArriveAsync(a, None))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, r => r.Status == "collecting");
        Assert.Equal(12, results.Select(r => r.ArrivalOrder).Distinct().Count());
        for (int i = 0; i < arrivals.Length; i++) { Assert.Equal(results[i], await s.Store.ArriveAsync(arrivals[i], None)); }
        await Assert.ThrowsAsync<CommandConflictException>(() => s.Store.ArriveAsync(arrivals[0] with { Soldiers = 2 }, None));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CargoIsNotCreditedBeforeReturnAndDuplicateRecoveryCreditsOnce()
    {
        await using var s = await CreateAsync();
        var arrival = Arrival(s);
        await s.Store.ArriveAsync(arrival, None);
        s.Time.Now += 600_000;
        await s.Store.RecoverAsync(None);
        var cargo = (await s.Store.FindAsync(arrival.MarchId, None))!;
        Assert.Equal(10, cargo.Cargo);
        Assert.Equal("returning", cargo.Status);
        s.Time.Now += 999;
        await s.Store.RecoverAsync(None);
        Assert.Equal("returning", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
        s.Time.Now++;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => s.Store.RecoverAsync(None))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("settled", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
        await using var credits = s.Db.Source.CreateCommand("SELECT sum(delta) FROM resource_events WHERE account_id=$1 AND material='meta:grain'");
        credits.Parameters.AddWithValue(s.Account);
        Assert.Equal(10L, await credits.ExecuteScalarAsync());
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task RestartRecoversDepletionReturnAndRespawnInDeadlineOrder()
    {
        await using var s = await CreateAsync();
        var arrival = Arrival(s, 60);
        await s.Store.ArriveAsync(arrival, None);
        s.Time.Now += 1_200_000;
        var restarted = new GatheringStore(s.Db.Source, s.World, s.Map, s.Content, s.Accounts, s.Time);
        await restarted.InitializeAsync(None);
        await restarted.RecoverAsync(None);
        var node = await restarted.NodeAsync("slg:test_grain", None);
        Assert.Equal(2, node.Generation);
        Assert.Equal(600, node.RemainingStock);
        Assert.Equal("settled", (await restarted.FindAsync(arrival.MarchId, None))!.Status);
        Assert.Equal(600, (await restarted.FindAsync(arrival.MarchId, None))!.Cargo);
        var stale = await restarted.ArriveAsync(Arrival(s), None);
        Assert.Equal("stale-generation", stale.Reason);
        Assert.Equal(600, (await restarted.NodeAsync("slg:test_grain", None)).RemainingStock);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CancellationUsesElapsedCargoAndClockRollbackCannotUndoIt()
    {
        await using var s = await CreateAsync();
        var arrival = Arrival(s, 2);
        await s.Store.ArriveAsync(arrival, None);
        s.Time.Now += 90_000;
        var cancelled = await s.Store.CancelAsync(arrival.MarchId, s.Account, None);
        Assert.Equal(3, cancelled.Cargo);
        Assert.Equal("returning", cancelled.Status);
        s.Time.Now -= 80_000;
        Assert.Equal(cancelled, await s.Store.CancelAsync(arrival.MarchId, s.Account, None));
        Assert.Equal(597, (await s.Store.NodeAsync(arrival.TargetId, None)).RemainingStock);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task FailedReturnRollsBackWalletAndCompletionThenCanRetry()
    {
        await using var s = await CreateAsync();
        var arrival = Arrival(s);
        await s.Store.ArriveAsync(arrival, None);
        s.Time.Now += 601_000;
        await using var fail = s.Db.Source.CreateCommand("CREATE FUNCTION fail_return() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.status='settled' THEN RAISE EXCEPTION 'test-failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_return BEFORE UPDATE ON world_gathering_arrivals FOR EACH ROW EXECUTE FUNCTION fail_return();");
        await fail.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<PostgresException>(() => s.Store.RecoverAsync(None));
        Assert.Equal("collecting", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
        await using var repair = s.Db.Source.CreateCommand("DROP TRIGGER fail_return ON world_gathering_arrivals");
        await repair.ExecuteNonQueryAsync();
        await s.Store.RecoverAsync(None);
        Assert.Equal("settled", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
    }
}
