using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class GatheringStoreTests
{
    private static async Task SetGrainAsync(Setup s, int amount)
    {
        await using var read = s.Db.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
        read.Parameters.AddWithValue(s.Account);
        var decoded = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!);
        Assert.True(decoded.Valid);
        var wallet = new Dictionary<string, int>(decoded.State!.Wallet) { ["meta:grain"] = amount };
        var state = decoded.State with { Wallet = wallet };
        new EstateApplication(EstateContent.Load(ContentEstateTests.Root())).Validate(state);
        await using var write = s.Db.Source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
        write.Parameters.AddWithValue(s.Account);
        write.Parameters.AddWithValue(MetaSaveCodec.Encode(state));
        await write.ExecuteNonQueryAsync();
    }

    private static async Task<int> GrainAsync(Setup s)
    {
        await using var read = s.Db.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
        read.Parameters.AddWithValue(s.Account);
        return MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!.Wallet["meta:grain"];
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ReturnOverflowKeepsItsIdentityAndLaterWalletSpaceFlushesExactlyOnce()
    {
        await using var s = await CreateAsync();
        int cap = new EstateApplication(EstateContent.Load(ContentEstateTests.Root())).WalletCap("meta:grain");
        await SetGrainAsync(s, cap - 3);
        var arrival = Arrival(s);
        await s.Store.ArriveAsync(arrival, None);
        s.Time.Now += 601_000;
        await s.Store.RecoverAsync(None);
        Assert.Equal(cap, await GrainAsync(s));
        await using (var held = s.Db.Source.CreateCommand("SELECT command_id,material,amount FROM account_held_materials WHERE account_id=$1"))
        {
            held.Parameters.AddWithValue(s.Account);
            await using var rows = await held.ExecuteReaderAsync();
            Assert.True(await rows.ReadAsync());
            Assert.Equal(arrival.MarchId, rows.GetGuid(0));
            Assert.Equal("meta:grain", rows.GetString(1));
            Assert.Equal(7, rows.GetInt32(2));
            Assert.False(await rows.ReadAsync());
        }
        // Model a prior, separately committed spend; this test owns the isolated account.
        await SetGrainAsync(s, cap - 5);
        await s.Accounts.ExecuteAccountAsync(s.Account, null, None);
        Assert.Equal(cap, await GrainAsync(s));
        await using var remaining = s.Db.Source.CreateCommand("SELECT sum(amount) FROM account_held_materials WHERE account_id=$1");
        remaining.Parameters.AddWithValue(s.Account);
        Assert.Equal(2L, await remaining.ExecuteScalarAsync());
        await s.Store.RecoverAsync(None);
        Assert.Equal(2L, await remaining.ExecuteScalarAsync());
        await SetGrainAsync(s, cap - 2);
        await s.Accounts.ExecuteAccountAsync(s.Account, null, None);
        Assert.Equal(cap, await GrainAsync(s));
        Assert.Equal(DBNull.Value, await remaining.ExecuteScalarAsync());
        await s.Accounts.ExecuteAccountAsync(s.Account, null, None);
        Assert.Equal(cap, await GrainAsync(s));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task FailedReturnPreservesWalletVersionHeldAndOutboxUntilRetry()
    {
        await using var s = await CreateAsync();
        int cap = new EstateApplication(EstateContent.Load(ContentEstateTests.Root())).WalletCap("meta:grain");
        await SetGrainAsync(s, cap - 3);
        var arrival = Arrival(s);
        await s.Store.ArriveAsync(arrival, None);
        s.Time.Now += 601_000;
        await using var before = s.Db.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
        before.Parameters.AddWithValue(s.Account);
        long version = (long)(await before.ExecuteScalarAsync())!;
        await using var fail = s.Db.Source.CreateCommand("CREATE FUNCTION reject_credit_notice() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected-world-return-outbox-failure'; END $$; CREATE TRIGGER reject_credit_notice BEFORE INSERT ON outbox FOR EACH ROW EXECUTE FUNCTION reject_credit_notice();");
        await fail.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<PostgresException>(() => s.Store.RecoverAsync(None));
        Assert.Equal(cap - 3, await GrainAsync(s));
        Assert.Equal(version, await before.ExecuteScalarAsync());
        await using var held = s.Db.Source.CreateCommand("SELECT count(*) FROM account_held_materials WHERE account_id=$1");
        held.Parameters.AddWithValue(s.Account);
        Assert.Equal(0L, await held.ExecuteScalarAsync());
        await using var events = s.Db.Source.CreateCommand("SELECT count(*) FROM resource_events WHERE account_id=$1");
        events.Parameters.AddWithValue(s.Account);
        Assert.Equal(0L, await events.ExecuteScalarAsync());
        await using var repair = s.Db.Source.CreateCommand("DROP TRIGGER reject_credit_notice ON outbox");
        await repair.ExecuteNonQueryAsync();
        await s.Store.RecoverAsync(None);
        Assert.Equal(cap, await GrainAsync(s));
        Assert.Equal(version + 1, await before.ExecuteScalarAsync());
        Assert.Equal(1L, await held.ExecuteScalarAsync());
        Assert.Equal(1L, await events.ExecuteScalarAsync());
    }
}
