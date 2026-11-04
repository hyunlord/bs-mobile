using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldCombatMarchTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private sealed class Clock : TimeProvider
    {
        public long Now { get; set; } = 1_793_520_000_000;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Now);
    }
    // This adapter isolates march transaction boundaries; Core verification has separate tests.
    private sealed class Battle : IWorldMarchActivity, IWorldCombatReturnActivity
    {
        public WorldCombatReturn? Result { get; set; }
        public bool Supports(string operation) => operation == "pve";
        public Task DispatchAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct) => Task.CompletedTask;
        public Task<MarchActivityResult> ArriveAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct) =>
            Task.FromResult(new MarchActivityResult("returning", checked(at + m.TravelMilliseconds), "battle-committed"));
        public Task<MarchActivityResult> AdvanceAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, bool cancel, CancellationToken ct) =>
            Task.FromResult(new MarchActivityResult("returning", checked(at + m.TravelMilliseconds), "recalled"));
        public Task<DomainCredit[]> ReturnAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct) =>
            Task.FromResult(Array.Empty<DomainCredit>());
        public Task<WorldCombatReturn?> PrepareReturnAsync(NpgsqlConnection c, NpgsqlTransaction t, WorldMarch m, long at, CancellationToken ct) => Task.FromResult(Result);
    }
    private sealed record Fixture(WorldTestDatabase Db, Clock Clock, AccountStore Accounts, WorldMarchStore Marches,
        TrainingContent Training, WorldCombatContent Combat, EstateContent Estate, Battle Battle, Guid Id) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Db.DisposeAsync();
        public async Task<AccountReply> View() => SlgFixture.Parse(await Accounts.ExecuteAccountAsync(Id, null, None));
        public async Task<WorldMarchCommand> Command() => new(Guid.NewGuid(), (await View()).Estate.Version, "pve",
            "slg:test_grain", 1, "core:frontier_knight", [new("slg:levy", 5)]);
        public async Task SetEstate(int level, bool fillWallet = false)
        {
            await using var read = Db.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
            read.Parameters.AddWithValue(Id);
            var state = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
            state = state with { Estate = state.Estate! with { Buildings = state.Estate!.Buildings.Select(b =>
                b.Kind == Training.Rules.BarracksKind ? b with { Level = level } : b).ToArray() } };
            if (fillWallet) { state = state with { Wallet = new Dictionary<string, int>(state.Wallet) { ["meta:grain"] = Estate.Config.WalletCaps["meta:grain"] } }; }
            await using var write = Db.Source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
            write.Parameters.AddWithValue(Id); write.Parameters.AddWithValue(MetaSaveCodec.Encode(state));
            await write.ExecuteNonQueryAsync();
        }
        public async Task<TrainingState> RawTraining()
        {
            await using var q = Db.Source.CreateCommand("SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='training'");
            q.Parameters.AddWithValue(Id);
            return JsonSerializer.Deserialize<TrainingState>((string)(await q.ExecuteScalarAsync())!, AccountStore.Json)!;
        }
    }
    private static async Task<Fixture> Create()
    {
        var db = await WorldTestDatabase.CreateAsync();
        try
        {
            string root = ContentEstateTests.Root(); var clock = new Clock();
            var estate = EstateContent.Load(root); var trainingContent = TrainingContent.Load(root, estate);
            var training = new TrainingHandler(estate, trainingContent); var research = ResearchContent.Load(root);
            var registry = new DomainRegistry([training, new RecruitDomain(RecruitPool.Load(root), 0),
                new ResearchDomain(research, new ExpeditionRules(3_600_000, 3, estate.Config.WalletCaps))], new byte[32]);
            var map = WorldMapContent.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(WorldStoreTests.MapBytes()).Replace("\"resource\"", "\"monster\"", StringComparison.Ordinal)));
            var world = new WorldStore(db.Source, map); var gate = new WorldRecoveryGate(db.Source, clock);
            var accounts = new AccountStore(db.Source, new EstateApplication(estate), clock, registry, world, recoveryGate: gate);
            await accounts.InitializeAsync(None);
            await new AllianceStore(db.Source, AllianceContent.Load(root), clock).InitializeAsync(None);
            await new AuthStore(db.Source, accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30),
                TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true), clock).InitializeAsync(None);
            await world.InitializeAsync(None);
            var created = await accounts.CreateAsync(None); Guid id = (await accounts.AuthenticateAsync(created.Token, None))!.Value;
            var battle = new Battle(); var combat = WorldCombatContent.Load(root);
            var marches = new WorldMarchStore(db.Source, world, map, accounts, WorldMarchContent.Load(root), training,
                trainingContent, research, clock, [battle], gate, combatContent: combat);
            var f = new Fixture(db, clock, accounts, marches, trainingContent, combat, estate, battle, id);
            await f.SetEstate(2);
            await accounts.ExecuteAccountAsync(id, null, None);
            var troops = training.InitialState.Deserialize<TrainingState>(AccountStore.Json)!;
            troops = troops with { Units = troops.Units.Select(u => u with { Ready = 10 }).ToArray() };
            await using var write = db.Source.CreateCommand("UPDATE account_domains SET state_json=$2 WHERE account_id=$1 AND domain='training'");
            write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(troops, AccountStore.Json));
            await write.ExecuteNonQueryAsync();
            await marches.InitializeAsync(None);
            return f;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task VersionOneSchemaUpgradesWithoutLosingDeployedMarch()
    {
        await using var f = await Create(); var command = await f.Command();
        await f.Marches.ExecuteAsync(f.Id, command, None);
        await using (var downgrade = f.Db.Source.CreateCommand("""
            UPDATE world_march_schema SET version=1;
            ALTER TABLE world_marches DROP CONSTRAINT world_marches_state_check;
            ALTER TABLE world_marches ADD CONSTRAINT world_marches_state_check CHECK(state IN ('outbound','gathering','returning','returned'));
            """)) { await downgrade.ExecuteNonQueryAsync(); }
        await f.Marches.InitializeAsync(None);
        await using var version = f.Db.Source.CreateCommand("SELECT version FROM world_march_schema");
        Assert.Equal(2, await version.ExecuteScalarAsync());
        var march = Assert.Single(await f.Marches.GetAsync(f.Id, None));
        Assert.Equal(command.CommandId, march.MarchId);
        await f.Marches.ExecuteLockedAsync(async (c, t, now, ct) =>
        {
            await f.Marches.RescheduleAsync(c, t, march, "battle-choice", now + 1, "test-choice", now, ct);
            await f.Marches.RescheduleAsync(c, t, march, "battle-active", now + 2, "test-active", now, ct);
            return true;
        }, None);
        Assert.Equal(5, (await f.RawTraining()).Units.Single(u => u.UnitId == "slg:levy").Deployed);
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ConcurrentPveDepartureFreezesAccountAndCannotCloneHeroOrTroops()
    {
        await using var f = await Create(); var command = await f.Command();
        async Task<bool> Try(WorldMarchCommand c)
        {
            try { await f.Marches.ExecuteAsync(f.Id, c, None); return true; }
            catch (CommandConflictException) { return false; }
        }
        var results = await Task.WhenAll(Try(command), Try(command with { CommandId = Guid.NewGuid() })).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Single(results, r => r);
        var march = Assert.Single(await f.Marches.GetAsync(f.Id, None));
        Assert.Equal(5, Assert.Single(march.Troops).Count);
        Assert.Equal(JsonValueKind.Object, march.Snapshot.GetProperty("combatAccount").ValueKind);
        var unit = (await f.RawTraining()).Units.Single(u => u.UnitId == "slg:levy");
        Assert.Equal(5, unit.Ready); Assert.Equal(5, unit.Deployed);
    }

    [Theory, InlineData("resource", false), InlineData("stronghold", true), Trait("Category", "Postgres")]
    public async Task PveTargetKindIsCheckedBeforeDeployment(string kind, bool allowed)
    {
        await using var f = await Create();
        await using (var q = f.Db.Source.CreateCommand("UPDATE world_targets SET kind=$1 WHERE target_id='slg:test_grain'"))
        { q.Parameters.AddWithValue(kind); await q.ExecuteNonQueryAsync(); }
        var command = await f.Command();
        if (allowed) { await f.Marches.ExecuteAsync(f.Id, command, None); }
        else
        {
            Assert.Equal("invalid-target-generation", (await Assert.ThrowsAsync<CommandConflictException>(() => f.Marches.ExecuteAsync(f.Id, command, None))).Message);
            Assert.Equal(0, (await f.RawTraining()).Units.Sum(u => u.Deployed));
        }
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ReturnRejectsWrongArmyWithoutReleasingDeploymentOrPayingCredits()
    {
        await using var f = await Create(); var command = await f.Command();
        f.Battle.Result = new([new("slg:levy", 4, 1)], 80, [new("meta:grain", 7)]);
        await f.Marches.ExecuteAsync(f.Id, command, None);
        var march = Assert.Single(await f.Marches.GetAsync(f.Id, None));
        f.Clock.Now = march.DueAtMs + march.TravelMilliseconds;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Marches.RecoverAsync(None));
        Assert.Equal(5, (await f.RawTraining()).Units.Single(u => u.UnitId == "slg:levy").Deployed);
        await using var q = f.Db.Source.CreateCommand("SELECT count(*) FROM resource_events WHERE account_id=$1 AND material='meta:grain'");
        q.Parameters.AddWithValue(f.Id); Assert.Equal(0L, await q.ExecuteScalarAsync());
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task ReturnUsesCurrentBedsAndCreditsOverflowExactlyOnce()
    {
        await using var f = await Create(); var command = await f.Command();
        f.Battle.Result = new([new("slg:levy", 5, 4)], 80, [new("meta:grain", 7)]);
        await f.Marches.ExecuteAsync(f.Id, command, None);
        var march = Assert.Single(await f.Marches.GetAsync(f.Id, None));
        f.Clock.Now = march.DueAtMs;
        await f.Marches.RecoverAsync(None);
        Assert.Equal(5, (await f.RawTraining()).Units.Single(u => u.UnitId == "slg:levy").Deployed);
        // Keep a valid troop capacity while making the live level's beds full at return.
        // The level-2 battle snapshot would still have room for new wounded soldiers.
        int occupiedBeds = f.Combat.Beds[1];
        Assert.True(f.Combat.Beds[2] > occupiedBeds);
        await f.SetEstate(1, fillWallet: true);
        var beforeReturn = await f.RawTraining();
        beforeReturn = beforeReturn with { Units = beforeReturn.Units.Select(u =>
            u.UnitId == "slg:levy" ? u with { Wounded = occupiedBeds } : u).ToArray() };
        Assert.True(beforeReturn.Units.Sum(u => u.Ready + u.Deployed + u.Wounded + u.Reserved) <=
            f.Training.Rules.Levels.Single(l => l.Level == 1).Capacity);
        await using (var write = f.Db.Source.CreateCommand("UPDATE account_domains SET state_json=$2 WHERE account_id=$1 AND domain='training'"))
        {
            write.Parameters.AddWithValue(f.Id);
            write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(beforeReturn, AccountStore.Json));
            await write.ExecuteNonQueryAsync();
        }
        f.Clock.Now += march.TravelMilliseconds;
        await Task.WhenAll(f.Marches.RecoverAsync(None), f.Marches.RecoverAsync(None)).WaitAsync(TimeSpan.FromSeconds(20));
        var unit = (await f.RawTraining()).Units.Single(u => u.UnitId == "slg:levy");
        Assert.Equal(0, unit.Deployed); Assert.Equal(occupiedBeds, unit.Wounded); Assert.Equal(6, unit.Ready);
        var view = await f.View();
        Assert.Equal(7, Assert.Single(view.Held!, h => h.Id == "meta:grain").Amount);
        Assert.Equal("returned", Assert.Single(await f.Marches.GetAsync(f.Id, None)).State);
        Assert.Equal(0, await f.Marches.RecoverAsync(None));
        Assert.Equal(7, Assert.Single((await f.View()).Held!, h => h.Id == "meta:grain").Amount);
    }
}
