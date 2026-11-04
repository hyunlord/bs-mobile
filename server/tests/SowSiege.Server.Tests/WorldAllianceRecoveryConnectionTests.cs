using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class WorldMarchStoreTests
{
    [Fact, Trait("Category", "Postgres")]
    public async Task AllianceCommandUsesRecoveryTransactionWithOneConnectionAndPreservesReceipts()
    {
        await using var fixture = await Create();
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.Db.ConnectionString)
        {
            MaxPoolSize = 1,
            Timeout = 3
        };
        await using var source = NpgsqlDataSource.Create(connectionString.ConnectionString);
        var gate = new WorldRecoveryGate(source, fixture.Time);
        var marches = new WorldMarchStore(source, fixture.World, fixture.Map, fixture.Accounts, fixture.Policy,
            fixture.Training, fixture.TrainingContent, fixture.Research, fixture.Time, [fixture.Activity], gate,
            recoveryOptions: new(1));
        // Initialization must release its migration connection before the first recovery pass.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await marches.InitializeAsync(timeout.Token);
        var alliances = new AllianceStore(source, AllianceContent.Load(ContentEstateTests.Root()), fixture.Time, gate);
        long version = await fixture.Version();
        var create = new AccountCommand(Guid.NewGuid(), version, AllianceStore.Domain, "create",
            JsonSerializer.SerializeToElement(new { name = "Field Guard", mode = "open" }));
        string first = await alliances.ExecuteAsync(fixture.Account, create, timeout.Token);
        var reply = JsonSerializer.Deserialize<AllianceReply>(first, AccountStore.Json)!;
        Assert.True(reply.Done);
        Assert.Equal("created", reply.Reason);
        Assert.Equal(version + 1, reply.AccountVersion);
        Assert.Equal(first, await alliances.ExecuteAsync(fixture.Account, create, timeout.Token));
        var conflict = await Assert.ThrowsAsync<CommandConflictException>(() => alliances.ExecuteAsync(fixture.Account,
            create with { Payload = JsonSerializer.SerializeToElement(new { name = "New Guard", mode = "open" }) }, timeout.Token));
        Assert.Equal("command-id-reused", conflict.Message);

        var notice = new AccountCommand(Guid.NewGuid(), reply.AccountVersion, AllianceStore.Domain, "notice",
            JsonSerializer.SerializeToElement(new { text = "One connection owns world, alliance and account locks." }));
        var updated = JsonSerializer.Deserialize<AllianceReply>(await alliances.ExecuteAsync(fixture.Account, notice, timeout.Token), AccountStore.Json)!;
        Assert.True(updated.Done);
        Assert.Equal(version + 2, updated.AccountVersion);
        Assert.Equal("One connection owns world, alliance and account locks.", updated.Alliance.Alliance!.Notice);
        await using var receipts = source.CreateCommand("SELECT count(*) FROM commands WHERE account_id=$1 AND command_id=ANY($2)");
        receipts.Parameters.AddWithValue(fixture.Account);
        receipts.Parameters.AddWithValue(new[] { create.CommandId, notice.CommandId });
        Assert.Equal(2L, await receipts.ExecuteScalarAsync(timeout.Token));
    }
}
