using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class GatheringStoreTests
{
    private static AuthStore Auth(Setup s) => new(s.Db.Source, s.Accounts,
        new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true), s.Time);

    [Fact, Trait("Category", "Postgres")]
    public async Task DeletedOwnersPendingReturnDoesNotStarveAnotherAccountsLaterReturn()
    {
        await using var s = await CreateAsync();
        var first = Arrival(s, travel: 0);
        await s.Store.ArriveAsync(first, None);
        await Auth(s).DeleteAccountAsync(s.Account, None);
        s.Time.Now += 600_000;
        var created = await s.Accounts.CreateAsync(None);
        Guid other = (await s.Accounts.AuthenticateAsync(created.Token, None))!.Value;
        var second = Arrival(s) with { AccountId = other };
        await s.Store.ArriveAsync(second, None);
        s.Time.Now += 601_000;
        await s.Store.RecoverAsync(None);
        Assert.Equal("returning", (await s.Store.FindAsync(first.MarchId, None))!.Status);
        Assert.Equal("settled", (await s.Store.FindAsync(second.MarchId, None))!.Status);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task OrdinaryAccountWriterAndRecoveryCannotFormAnInverseLockCycle()
    {
        await using var s = await CreateAsync();
        await s.Store.ArriveAsync(Arrival(s), None);
        s.Time.Now += 601_000;
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => i % 2 == 0
            ? s.Store.RecoverAsync(None) : (Task)s.Accounts.ExecuteAccountAsync(s.Account, null, None))).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(590, (await s.Store.NodeAsync("slg:test_grain", None)).RemainingStock);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ChangedContentCannotSilentlyRewritePersistentNodePolicy()
    {
        await using var s = await CreateAsync();
        var before = await s.Store.NodeAsync("slg:test_grain", None);
        var changed = new GatheringStore(s.Db.Source, s.World, s.Map, GatheringContent.Load(ContentEstateTests.Root()), s.Accounts, s.Time);
        await Assert.ThrowsAsync<InvalidDataException>(() => changed.InitializeAsync(None));
        Assert.Equal(before, await s.Store.NodeAsync("slg:test_grain", None));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task InstalledMarchSchedulerRejectsStandaloneWorkEvenBeforeAnyManagedArrival()
    {
        await using var s = await CreateAsync();
        await using var installed = s.Db.Source.CreateCommand("CREATE TABLE world_marches(id uuid PRIMARY KEY)");
        await installed.ExecuteNonQueryAsync();
        var arrival = Arrival(s);
        var first = await Assert.ThrowsAsync<CommandConflictException>(() => s.Store.ArriveAsync(arrival, None));
        Assert.Equal("march-scheduler-required", first.Message);
        await Assert.ThrowsAsync<CommandConflictException>(() => s.Store.RecoverAsync(None));
        await Assert.ThrowsAsync<CommandConflictException>(() => s.Store.CancelAsync(arrival.MarchId, s.Account, None));
        Assert.Null(await s.Store.FindAsync(arrival.MarchId, None));
        Assert.Equal(600, (await s.Store.NodeAsync(arrival.TargetId, None)).RemainingStock);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task PurgeRacingArrivalRecoveryCannotDeadlockOrRetainADeletedOwner()
    {
        await using var s = await CreateAsync();
        var arrival = Arrival(s);
        await s.Store.ArriveAsync(arrival, None);
        var auth = new AuthStore(s.Db.Source, s.Accounts,
            new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30), TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true,
                Convert.FromHexString(RecruitTests.TestKeyHex), DeletionGraceDays: 0), s.Time);
        await auth.DeleteAccountAsync(s.Account, None);
        s.Time.Now += 601_000;
        async Task RejectedArrival()
        {
            await Assert.ThrowsAsync<CommandConflictException>(() => s.Store.ArriveAsync(Arrival(s), None));
        }
        var purge = auth.PurgeExpiredAsync(10, None);
        await Task.WhenAll(purge, RejectedArrival()).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(1, await purge + await auth.PurgeExpiredAsync(10, None));
        await s.Store.RecoverAsync(None);
        Assert.Equal("forfeited", (await s.Store.FindAsync(arrival.MarchId, None))!.Status);
        Assert.Equal(590, (await s.Store.NodeAsync(arrival.TargetId, None)).RemainingStock);
        await using var owner = s.Db.Source.CreateCommand("SELECT account_id FROM world_gathering_arrivals WHERE march_id=$1");
        owner.Parameters.AddWithValue(arrival.MarchId);
        Assert.Equal(DBNull.Value, await owner.ExecuteScalarAsync());
    }
}
