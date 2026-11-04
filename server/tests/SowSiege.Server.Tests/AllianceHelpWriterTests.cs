using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class AllianceTests
{
    private static async Task<ResearchState> ResearchStateAsync(World world, Guid id)
    {
        await using var query = world.Source.CreateCommand("SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='research'");
        query.Parameters.AddWithValue(id);
        return JsonSerializer.Deserialize<ResearchState>((string)(await query.ExecuteScalarAsync())!, AccountStore.Json)!;
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task HelpWritesRealDeadlineAndRequesterVersionAndDuplicateDoesNotRepeat()
    {
        await using var world = await World.CreateAsync();
        var requester = await world.AccountAsync(); var giver = await world.AccountAsync();
        var alliance = await world.AllianceAsync(requester);
        await world.RunAsync(giver, "apply", new { allianceId = alliance });
        await world.ResearchJobAsync(requester, 1, world.NowMs, world.NowMs + 200_000);
        await world.RunAsync(requester, "request-help", new { jobKey = "research:1" });
        long version = await world.VersionAsync(requester);
        var command = new AccountCommand(Guid.NewGuid(), await world.VersionAsync(giver), AllianceStore.Domain, "give-help",
            JsonSerializer.SerializeToElement(new { requester, jobKey = "research:1" }));
        var reply = Parse(await world.Alliances.ExecuteAsync(giver, command, CancellationToken.None));
        Assert.True(reply.Done);
        var active = Assert.Single((await ResearchStateAsync(world, requester)).Active);
        Assert.Equal(world.NowMs + 200_000 - reply.ShortenedMs!.Value, active.CompletesAtMs);
        Assert.Equal(reply.ShortenedMs.Value, active.HelpMilliseconds);
        Assert.Equal(version + 1, await world.VersionAsync(requester));
        await using (var evidence = world.Source.CreateCommand("SELECT (SELECT count(*) FROM domain_events WHERE account_id=$1 AND version=$2 AND kind='alliance-help-applied'), (SELECT count(*) FROM outbox WHERE account_id=$1 AND version=$2)"))
        {
            evidence.Parameters.AddWithValue(requester); evidence.Parameters.AddWithValue(version + 1);
            await using var reader = await evidence.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0)); Assert.Equal(1L, reader.GetInt64(1));
        }
        var repeated = Parse(await world.Alliances.ExecuteAsync(giver, command, CancellationToken.None));
        Assert.Equal(reply.ShortenedMs, repeated.ShortenedMs);
        Assert.Equal(version + 1, await world.VersionAsync(requester));
        Assert.Equal(active, Assert.Single((await ResearchStateAsync(world, requester)).Active));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task HelpThatConsumesRemainingTimeCompletesResearchInSameTransaction()
    {
        await using var world = await World.CreateAsync();
        var requester = await world.AccountAsync(); var giver = await world.AccountAsync();
        var alliance = await world.AllianceAsync(requester);
        await world.RunAsync(giver, "apply", new { allianceId = alliance });
        await world.ResearchJobAsync(requester, 1, world.NowMs, world.NowMs + 200_000);
        await world.RunAsync(requester, "request-help", new { nodeId = Node });
        world.Clock.Now = world.Clock.Now.AddSeconds(198);
        var reply = await world.RunAsync(giver, "give-help", new { requester, jobKey = "research:1" });
        Assert.Equal(2000, reply.ShortenedMs);
        var state = await ResearchStateAsync(world, requester);
        Assert.Empty(state.Active);
        Assert.Equal(1, state.Levels[Node]);
        Assert.Equal(world.NowMs, state.CompletedAtMs[Node]);
        long version = await world.VersionAsync(requester);
        Assert.Equal("request-closed", (await world.RunAsync(giver, "give-help", new { requester, jobKey = "research:1" })).Reason);
        Assert.Equal(version, await world.VersionAsync(requester));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DifferentAllianceCannotHelpOrMutateRequester()
    {
        await using var world = await World.CreateAsync();
        var requester = await world.AccountAsync(); var outsider = await world.AccountAsync();
        await world.AllianceAsync(requester); await world.AllianceAsync(outsider);
        await world.ResearchJobAsync(requester, 1, world.NowMs, world.NowMs + 200_000);
        await world.RunAsync(requester, "request-help", new { nodeId = Node });
        var before = await ResearchStateAsync(world, requester);
        long version = await world.VersionAsync(requester);
        Assert.Equal("request-closed", (await world.RunAsync(outsider, "give-help", new { requester, jobKey = "research:1" })).Reason);
        Assert.Equal(version, await world.VersionAsync(requester));
        Assert.Equal(before.Active, (await ResearchStateAsync(world, requester)).Active);
    }
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task HelpWaitsForAllianceLockAndRechecksMembershipAfterLeaveCommit()
    {
        await using var world = await World.CreateAsync();
        var leader = await world.AccountAsync(); var requester = await world.AccountAsync(); var giver = await world.AccountAsync();
        var alliance = await world.AllianceAsync(leader);
        await world.RunAsync(requester, "apply", new { allianceId = alliance });
        await world.RunAsync(giver, "apply", new { allianceId = alliance });
        await world.ResearchJobAsync(requester, 1, world.NowMs, world.NowMs + 200_000);
        await world.RunAsync(requester, "request-help", new { nodeId = Node });
        long version = await world.VersionAsync(requester);
        await using var connection = await world.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT id FROM alliances WHERE id=$1 FOR UPDATE", connection, transaction))
        { hold.Parameters.AddWithValue(alliance); await hold.ExecuteScalarAsync(); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var command = new AccountCommand(Guid.NewGuid(), await world.VersionAsync(giver), AllianceStore.Domain, "give-help",
            JsonSerializer.SerializeToElement(new { requester, jobKey = "research:1" }));
        var giving = world.Alliances.ExecuteAsync(giver, command, timeout.Token);
        bool blocked = false;
        for (int attempt = 0; attempt < 100 && !blocked; attempt++)
        {
            await using var wait = world.Source.CreateCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE $1=ANY(pg_blocking_pids(pid)))");
            wait.Parameters.AddWithValue(connection.ProcessID);
            blocked = (bool)(await wait.ExecuteScalarAsync(timeout.Token))!;
            if (!blocked) { await Task.Delay(10, timeout.Token); }
        }
        Assert.True(blocked, "Help must actually wait on the held alliance row.");
        // Model the membership-removal commit with the production alliance -> account lock order.
        await using (var account = new NpgsqlCommand("SELECT id FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction))
        { account.Parameters.AddWithValue(requester); await account.ExecuteScalarAsync(timeout.Token); }
        await using (var leave = new NpgsqlCommand("DELETE FROM alliance_members WHERE account_id=$1", connection, transaction))
        { leave.Parameters.AddWithValue(requester); await leave.ExecuteNonQueryAsync(timeout.Token); }
        await using (var count = new NpgsqlCommand("UPDATE alliances SET member_count=member_count-1 WHERE id=$1", connection, transaction))
        { count.Parameters.AddWithValue(alliance); await count.ExecuteNonQueryAsync(timeout.Token); }
        await transaction.CommitAsync(timeout.Token);
        Assert.Equal("request-closed", Parse(await giving).Reason);
        Assert.Equal(version, await world.VersionAsync(requester));
        Assert.Equal(0, Assert.Single((await ResearchStateAsync(world, requester)).Active).HelpMilliseconds);
    }

}
