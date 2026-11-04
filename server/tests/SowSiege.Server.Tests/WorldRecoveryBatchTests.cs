using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class WorldMarchStoreTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4097)]
    public void RecoveryBudgetRejectsInvalidOperationalLimits(int size) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldRecoveryOptions(size));

    [Theory]
    [InlineData(1)]
    [InlineData(128)]
    [InlineData(4096)]
    public void RecoveryBudgetAcceptsBoundaries(int size) => Assert.Equal(size, new WorldRecoveryOptions(size).BatchSize);

    private static WorldMarchStore RestartRecovery(Fixture f, int size) => new(f.Db.Source, f.World, f.Map,
        f.Accounts, f.Policy, f.Training, f.TrainingContent, f.Research, f.Time,
        f.Gathering is null ? [f.Activity] : [new GatheringMarchActivity(f.Gathering)],
        gathering: f.Gathering, recoveryOptions: new(size));

    [Theory, Trait("Category", "Postgres")]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(128)]
    public async Task ThirtyDayOutageRestartsBetweenBatchesWithoutChangingDeadlinesOrCredits(int size)
    {
        await using var f = await Create(true, size);
        var command = await f.Command(1);
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var original = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds += 30L * 86_400_000;
        int processed = 0;
        int batches = 0;
        WorldRecoveryBatchResult result;
        do
        {
            // A fresh store reconstructs scheduling solely from persisted rows each time.
            var restarted = RestartRecovery(f, size);
            result = await restarted.RecoverBatchAsync(None);
            Assert.InRange(result.ProcessedEvents, 1, size);
            Assert.Equal(result.ProcessedEvents, result.CommittedEvents);
            Assert.Equal(0, result.CombatRecoveryAttempts);
            Assert.Equal(result.HasMore, result.OldestDueAtMs.HasValue);
            if (result.OldestDueAtMs is { } due) { Assert.True(due <= f.Time.Milliseconds); }
            processed += result.ProcessedEvents;
            Assert.True(++batches < 20, "Recovery must converge after a bounded number of batches.");
        }
        while (result.HasMore);
        Assert.Equal(4, processed); // Arrival, collection completion, return, resource respawn.
        Assert.Equal("returned", Assert.Single(await f.Marches.GetAsync(f.Account, None)).State);
        var node = await f.Gathering!.NodeAsync("slg:test_grain", None);
        Assert.Equal(2, node.Generation);
        Assert.Equal(1, node.RemainingStock);
        await using (var events = f.Db.Source.CreateCommand("SELECT at_ms,kind FROM world_march_events WHERE march_id=$1 ORDER BY ordinal"))
        {
            events.Parameters.AddWithValue(command.CommandId);
            var actual = new List<(long At, string Kind)>();
            await using var reader = await events.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { actual.Add((reader.GetInt64(0), reader.GetString(1))); }
            Assert.Equal(new[] { original.StartedAtMs, original.DueAtMs, original.DueAtMs + 60_000,
                original.DueAtMs + 60_000 + original.TravelMilliseconds }, actual.Select(e => e.At));
        }
        await using (var credits = f.Db.Source.CreateCommand("SELECT sum(delta) FROM resource_events WHERE account_id=$1 AND material='meta:grain'"))
        {
            credits.Parameters.AddWithValue(f.Account);
            Assert.Equal(1L, await credits.ExecuteScalarAsync());
        }
        var training = (await f.View()).Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.Equal(10, training.Units.Single(u => u.UnitId == "slg:levy").Ready);
        Assert.Equal(0, training.Units.Sum(u => u.Deployed));
        Assert.Equal(new WorldRecoveryBatchResult(0, 0, 0, false, null), await RestartRecovery(f, size).RecoverBatchAsync(None));
    }

    [Theory, Trait("Category", "Postgres")]
    [InlineData("callback")]
    [InlineData("march-command")]
    [InlineData("march-read")]
    [InlineData("account-read")]
    public async Task BacklogRejectsAdmissionBeforeCallbackOrCommandReceipt(string path)
    {
        await using var f = await Create(true, 1);
        var dispatch = await f.Command(1);
        await f.Marches.ExecuteAsync(f.Account, dispatch, None);
        var pending = new WorldMarchCommand(Guid.NewGuid(), await f.Version(), "cancel", MarchId: dispatch.CommandId);
        f.Time.Milliseconds += 30L * 86_400_000;
        bool called = false;
        Task Act() => path switch
        {
            "callback" => f.Marches.ExecuteAsync((_, _, _, _) => { called = true; return Task.FromResult(0); }, None),
            "march-command" => f.Marches.ExecuteAsync(f.Account, pending, None),
            "march-read" => f.Marches.GetAsync(f.Account, None),
            _ => f.Accounts.ExecuteAccountAsync(f.Account, null, None)
        };
        await Assert.ThrowsAsync<WorldRecoveryPendingException>(Act);
        Assert.False(called);
        await using var receipt = f.Db.Source.CreateCommand("SELECT count(*) FROM commands WHERE command_id=$1");
        receipt.Parameters.AddWithValue(pending.CommandId);
        Assert.Equal(0L, await receipt.ExecuteScalarAsync());
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CancellationInsideSelectedEventRollsBackAndPreservesRetryDeadline()
    {
        await using var f = await Create(recoveryBatchSize: 1);
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var original = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds = original.DueAtMs;
        using var cancellation = new CancellationTokenSource();
        f.Activity.BeforeArrival = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Marches.RecoverBatchAsync(cancellation.Token));
        await using (var query = f.Db.Source.CreateCommand("SELECT state,due_at FROM world_marches WHERE march_id=$1"))
        {
            query.Parameters.AddWithValue(command.CommandId);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("outbound", reader.GetString(0));
            Assert.Equal(original.DueAtMs, reader.GetInt64(1));
        }
        f.Activity.BeforeArrival = null;
        var retry = await RestartRecovery(f, 1).RecoverBatchAsync(None);
        Assert.Equal(1, retry.ProcessedEvents);
        Assert.Equal(1, retry.CommittedEvents);
        Assert.False(retry.HasMore);
        Assert.Equal(original.DueAtMs, Assert.Single(f.Activity.Arrivals).At);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ConcurrentBoundedRecoveryAndWriterCommitEachEventOnce()
    {
        await using var f = await Create(recoveryBatchSize: 1);
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        f.Time.Milliseconds += 30L * 86_400_000;
        async Task Writer()
        {
            try { await f.Accounts.ExecuteAccountAsync(f.Account, null, None); }
            catch (WorldRecoveryPendingException) { }
        }
        await Task.WhenAll(f.Marches.RecoverBatchAsync(None), RestartRecovery(f, 1).RecoverBatchAsync(None), Writer())
            .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(f.Activity.Arrivals);
        Assert.Equal("returned", Assert.Single(await f.Marches.GetAsync(f.Account, None)).State);
        await using var events = f.Db.Source.CreateCommand("SELECT count(*) FROM world_march_events WHERE march_id=$1");
        events.Parameters.AddWithValue(command.CommandId);
        Assert.Equal(3L, await events.ExecuteScalarAsync());
    }
}
