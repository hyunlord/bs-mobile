using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldMarchStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds { get; set; } = 1_793_520_000_000;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Milliseconds);
    }
    // Test activity isolates travel/locking/transactions from gathering economics (tested by GatheringStore).
    private sealed class ImmediateReturn : IWorldMarchActivity
    {
        public bool FailArrival { get; set; }
        public List<(Guid March, long At)> Arrivals { get; } = [];
        public bool Supports(string operation) => operation == "gather";
        public Task DispatchAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct) => Task.CompletedTask;
        public Task<MarchActivityResult> ArriveAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct)
        {
            if (FailArrival) { throw new InvalidOperationException("injected-before-arrival-commit"); }
            Arrivals.Add((m.MarchId, at));
            return Task.FromResult(new MarchActivityResult("returning", at + m.TravelMilliseconds, "no-cargo"));
        }
        public Task<MarchActivityResult> AdvanceAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, bool cancel, CancellationToken ct) =>
            Task.FromResult(new MarchActivityResult("returning", at + m.TravelMilliseconds, "recalled"));
        public Task<DomainCredit[]> ReturnAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct) =>
            Task.FromResult(Array.Empty<DomainCredit>());
    }
    private sealed record Fixture(WorldTestDatabase Db, Clock Time, AccountStore Accounts, WorldMarchStore Marches,
        WorldStore World, WorldMapContent Map, WorldMarchContent Policy, TrainingHandler Training, TrainingContent TrainingContent,
        ResearchContent Research, DomainRegistry Registry, ImmediateReturn Activity, Guid Account, GatheringStore? Gathering) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        public async Task<AccountReply> View() => SlgFixture.Parse(await Accounts.ExecuteAccountAsync(Account, null, None));
        public async Task<long> Version() => (await View()).Estate.Version;
        public async Task<WorldMarchCommand> Command(int count = 2) => new(Guid.NewGuid(), await Version(), "gather",
            "slg:test_grain", 1, Policy.BasicHero, [new("slg:levy", count)]);
    }

    private static async Task<Fixture> Create(bool realGathering = false)
    {
        var db = await WorldTestDatabase.CreateAsync();
        var clock = new Clock();
        string root = ContentEstateTests.Root();
        var estate = EstateContent.Load(root);
        var trainingContent = TrainingContent.Load(root, estate);
        var training = new TrainingHandler(estate, trainingContent);
        var research = ResearchContent.Load(root);
        var registry = new DomainRegistry([training, new RecruitDomain(RecruitPool.Load(root), 0),
            new ResearchDomain(research, new ExpeditionRules(3_600_000, 3, estate.Config.WalletCaps))], new byte[32]);
        var map = WorldMapContent.Parse(WorldStoreTests.MapBytes());
        var world = new WorldStore(db.Source, map);
        var gate = new WorldRecoveryGate(db.Source, clock);
        var accounts = new AccountStore(db.Source, new EstateApplication(estate), clock, registry, world, gate);
        await accounts.InitializeAsync(None);
        await new AllianceStore(db.Source, AllianceContent.Load(root), clock).InitializeAsync(None);
        await new AuthStore(db.Source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30),
            TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true), clock).InitializeAsync(None);
        await world.InitializeAsync(None);
        var created = await accounts.CreateAsync(None);
        Guid id = (await accounts.AuthenticateAsync(created.Token, None))!.Value;
        await using (var read = db.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1"))
        {
            read.Parameters.AddWithValue(id);
            var state = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
            state = state with { Estate = state.Estate! with { Buildings = state.Estate!.Buildings.Select(b =>
                b.Kind == trainingContent.Rules.BarracksKind ? b with { Level = 2 } : b).ToArray() } };
            await using var write = db.Source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
            write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(MetaSaveCodec.Encode(state)); await write.ExecuteNonQueryAsync();
        }
        await accounts.ExecuteAccountAsync(id, null, None);
        var troops = training.InitialState.Deserialize<TrainingState>(AccountStore.Json)!;
        troops = troops with { Units = troops.Units.Select(u => u with { Ready = 10 }).ToArray() };
        await using (var write = db.Source.CreateCommand("UPDATE account_domains SET state_json=$2 WHERE account_id=$1 AND domain='training'"))
        {
            write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(troops, AccountStore.Json));
            await write.ExecuteNonQueryAsync();
        }
        var activity = new ImmediateReturn();
        var policy = WorldMarchContent.Load(root);
        GatheringStore? gathering = null;
        IWorldMarchActivity[] adapters = [activity];
        if (realGathering)
        {
            var gatheringContent = GatheringContent.Parse(JsonSerializer.SerializeToUtf8Bytes(new
            {
                id = "slg:gathering_v1", kind = "slg-gathering", schemaVersion = 1, status = "hypothesis",
                numericStatus = "[가설]", sources = new[] { "docs/design/gdd/18-slg-world-map-proposal.md" },
                policy = new { stock = 1, unitsPerSoldierMinute = 1, capacityPerSoldier = 10, respawnMilliseconds = 600_000 },
                resources = new[] { new { contentId = "meta:grain", materialId = "meta:grain" } }
            }));
            gathering = new(db.Source, world, map, gatheringContent, accounts, clock);
            await gathering.InitializeAsync(None);
            adapters = [new GatheringMarchActivity(gathering)];
        }
        var marches = new WorldMarchStore(db.Source, world, map, accounts, policy, training, trainingContent, research, clock, adapters, gate, gathering);
        await marches.InitializeAsync(None);
        return new(db, clock, accounts, marches, world, map, policy, training, trainingContent, research, registry, activity, id, gathering);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DispatchReceiptDeploysExactlyOnceAndConflictingPayloadCannotReuseIdentity()
    {
        await using var f = await Create();
        var command = await f.Command();
        string first = await f.Marches.ExecuteAsync(f.Account, command, None);
        Assert.Equal(first, await f.Marches.ExecuteAsync(f.Account, command, None));
        await Assert.ThrowsAsync<CommandConflictException>(() => f.Marches.ExecuteAsync(f.Account,
            command with { Troops = [new("slg:levy", 3)] }, None));
        var view = await f.View();
        var levy = view.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!.Units.Single(u => u.UnitId == "slg:levy");
        Assert.Equal(8, levy.Ready);
        Assert.Equal(2, levy.Deployed);
        Assert.Single(await f.Marches.GetAsync(f.Account, None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentDepartureCannotCloneHeroOrSpendSameReadyTroops()
    {
        await using var f = await Create();
        var command = await f.Command();
        async Task<bool> Try(WorldMarchCommand request)
        {
            try { await f.Marches.ExecuteAsync(f.Account, request, None); return true; }
            catch (CommandConflictException) { return false; }
        }
        var results = await Task.WhenAll(Try(command), Try(command with { CommandId = Guid.NewGuid() })).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, x => x);
        Assert.Single(await f.Marches.GetAsync(f.Account, None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task MidEdgeRecallKeepsDeploymentUntilExactFractionalReturnDeadline()
    {
        await using var f = await Create();
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        f.Time.Milliseconds += 3456;
        var cancel = new WorldMarchCommand(Guid.NewGuid(), await f.Version(), "cancel", MarchId: command.CommandId);
        await f.Marches.ExecuteAsync(f.Account, cancel, None);
        var march = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        Assert.Equal("returning", march.State);
        Assert.Equal(f.Time.Milliseconds + 3456, march.DueAtMs);
        f.Time.Milliseconds = march.DueAtMs - 1;
        Assert.Equal("returning", Assert.Single(await f.Marches.GetAsync(f.Account, None)).State);
        f.Time.Milliseconds++;
        Assert.Equal("returned", Assert.Single(await f.Marches.GetAsync(f.Account, None)).State);
        var levy = (await f.View()).Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!.Units.Single(u => u.UnitId == "slg:levy");
        Assert.Equal(10, levy.Ready);
        Assert.Equal(0, levy.Deployed);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RestartAndOrdinaryAccountReadRecoverOriginalArrivalAndReturnBeforeAdvancingToNow()
    {
        await using var f = await Create();
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var original = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds += 86_400_000;
        var gate = new WorldRecoveryGate(f.Db.Source, f.Time);
        var accounts = new AccountStore(f.Db.Source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())),
            f.Time, f.Registry, f.World, gate);
        var recovered = new WorldMarchStore(f.Db.Source, f.World, f.Map, accounts, f.Policy, f.Training, f.TrainingContent,
            f.Research, f.Time, [f.Activity], gate);
        await recovered.InitializeAsync(None);
        var view = SlgFixture.Parse(await accounts.ExecuteAccountAsync(f.Account, null, None));
        Assert.Equal(original.DueAtMs, Assert.Single(f.Activity.Arrivals).At);
        Assert.Equal("returned", Assert.Single(await recovered.GetAsync(f.Account, None)).State);
        Assert.Equal(0, view.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!.Units.Sum(u => u.Deployed));
        Assert.Equal(0, await recovered.RecoverAsync(None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ArrivalFailureRollsBackWorldAndAccountAndRestartRetriesSameDeadline()
    {
        await using var f = await Create();
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var original = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds = original.DueAtMs;
        f.Activity.FailArrival = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Marches.RecoverAsync(None));
        await using (var q = f.Db.Source.CreateCommand("SELECT state FROM world_marches WHERE march_id=$1"))
        {
            q.Parameters.AddWithValue(command.CommandId);
            Assert.Equal("outbound", await q.ExecuteScalarAsync());
        }
        f.Activity.FailArrival = false;
        Assert.Equal(1, await f.Marches.RecoverAsync(None));
        Assert.Equal(original.DueAtMs, Assert.Single(f.Activity.Arrivals).At);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DeletedTargetGenerationReturnsWithoutEnteringActivity()
    {
        await using var f = await Create();
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var original = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        await using (var q = f.Db.Source.CreateCommand("UPDATE world_targets SET generation=generation+1 WHERE target_id='slg:test_grain'"))
        { await q.ExecuteNonQueryAsync(); }
        f.Time.Milliseconds = original.DueAtMs;
        await f.Marches.RecoverAsync(None);
        Assert.Empty(f.Activity.Arrivals);
        Assert.Equal("target-gone", Assert.Single(await f.Marches.GetAsync(f.Account, None)).Reason);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task BusyKnightCannotBeClonedIntoPersonalExpedition()
    {
        await using var f = await Create();
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var run = new AccountCommand(Guid.NewGuid(), await f.Version(), ResearchDomain.Name, "start-expedition",
            JsonSerializer.SerializeToElement(new { proofId = f.Research.Fallback.ProofIds[0], resource = "meta:grain" }));
        var exception = await Assert.ThrowsAsync<CommandConflictException>(() => f.Accounts.ExecuteAccountAsync(f.Account, run, None));
        Assert.Equal("hero-busy", exception.Message);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AccountWriterAndMarchRecoveryDoNotDeadlock()
    {
        await using var f = await Create();
        var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var original = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds = original.DueAtMs + original.TravelMilliseconds;
        await Task.WhenAll(f.Marches.RecoverAsync(None), f.Accounts.ExecuteAccountAsync(f.Account, null, None)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal("returned", Assert.Single(await f.Marches.GetAsync(f.Account, None)).State);
    }
    [Fact, Trait("Category", "Postgres")]
    public async Task IdleDepletedNodeRespawnsAfterLastMarchHasReturned()
    {
        await using var f = await Create(true);
        var command = await f.Command(1);
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var march = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds = march.DueAtMs;
        await f.Marches.RecoverAsync(None);
        var visit = (await f.Gathering!.FindAsync(command.CommandId, None))!;
        f.Time.Milliseconds = visit.DueAtMs!.Value + march.TravelMilliseconds;
        await f.Marches.RecoverAsync(None);
        Assert.Equal("returned", Assert.Single(await f.Marches.GetAsync(f.Account, None)).State);
        Assert.Equal(1, (await f.Gathering.NodeAsync("slg:test_grain", None)).Generation);
        f.Time.Milliseconds += 600_000;
        await f.Marches.RecoverAsync(None);
        var respawned = await f.Gathering.NodeAsync("slg:test_grain", None);
        Assert.Equal(2, respawned.Generation);
        Assert.Equal(1, respawned.RemainingStock);
        Assert.Null(respawned.Lease);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task PurgedOwnerCannotStrandManagedLeaseOrRespawn()
    {
        await using var f = await Create(true);
        var command = await f.Command(1);
        await f.Marches.ExecuteAsync(f.Account, command, None);
        var march = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds = march.DueAtMs;
        await f.Marches.RecoverAsync(None);
        // Simulate the referential effects of account purge without deleting unrelated account-owned tables.
        await using (var detach = f.Db.Source.CreateCommand("UPDATE world_gathering_arrivals SET account_id=NULL WHERE march_id=$1"))
        { detach.Parameters.AddWithValue(command.CommandId); await detach.ExecuteNonQueryAsync(); }
        await using (var remove = f.Db.Source.CreateCommand("DELETE FROM world_marches WHERE march_id=$1"))
        { remove.Parameters.AddWithValue(command.CommandId); await remove.ExecuteNonQueryAsync(); }
        f.Time.Milliseconds += 1_000_000;
        await f.Marches.RecoverAsync(None);
        Assert.Equal("forfeited", (await f.Gathering!.FindAsync(command.CommandId, None))!.Status);
        Assert.Equal(2, (await f.Gathering.NodeAsync("slg:test_grain", None)).Generation);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task EarlierArrivalDoesNotObserveFutureRespawnAfterLongDowntime()
    {
        await using var f = await Create(true);
        var first = await f.Command(1);
        await f.Marches.ExecuteAsync(f.Account, first, None);
        var march = Assert.Single(await f.Marches.GetAsync(f.Account, None));
        f.Time.Milliseconds = march.DueAtMs + 60_000 + march.TravelMilliseconds;
        await f.Marches.RecoverAsync(None);
        var second = await f.Command(1);
        await f.Marches.ExecuteAsync(f.Account, second, None);
        f.Time.Milliseconds += 1_000_000;
        await f.Marches.RecoverAsync(None);
        var retry = (await f.Marches.GetAsync(f.Account, None)).Single(m => m.MarchId == second.CommandId);
        Assert.Equal("returned", retry.State);
        var visit = await f.Gathering!.FindAsync(second.CommandId, None);
        Assert.NotNull(visit);
        Assert.Equal(0, visit.Cargo);
        Assert.Equal(2, (await f.Gathering.NodeAsync("slg:test_grain", None)).Generation);
    }

}
