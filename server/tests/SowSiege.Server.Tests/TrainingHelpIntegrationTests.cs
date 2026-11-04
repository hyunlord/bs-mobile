using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class TrainingHelpIntegrationTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public long Ms => Now.ToUnixTimeMilliseconds();
        public void At(long ms) => Now = DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    private sealed class Fixture(NpgsqlDataSource source, string name, Clock clock) : IAsyncDisposable
    {
        public NpgsqlDataSource Source => source;
        public string Name => name;
        public Clock Time => clock;
        public EstateContent Estate { get; } = EstateContent.Load(ContentEstateTests.Root());
        public ResearchContent Research { get; } = ResearchContent.Load(ContentEstateTests.Root());
        public TrainingContent Training => TrainingContent.Load(ContentEstateTests.Root(), Estate);
        public AccountStore Accounts => new(source, new EstateApplication(Estate), clock,
            new DomainRegistry([new ResearchDomain(Research), new TrainingHandler(Estate, Training)]));
        public AllianceStore Alliances => new(source, AllianceContent.Load(ContentEstateTests.Root()), clock, Accounts);
        public AuthStore Auth => new(source, Accounts, new AuthOptions(TimeSpan.FromMinutes(15), TimeSpan.FromDays(30),
            TimeSpan.FromMinutes(1), AuthTests.ShippedLimits(), true, Convert.FromHexString(RecruitTests.TestKeyHex)), clock);
        public static async Task<Fixture> Create()
        {
            string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("Postgres required");
            string schema = "training_help_" + Guid.NewGuid().ToString("N");
            await using (var admin = NpgsqlDataSource.Create(connection))
            await using (var create = admin.CreateCommand("CREATE SCHEMA " + schema)) { await create.ExecuteNonQueryAsync(); }
            var builder = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, ApplicationName = schema, CommandTimeout = 15 };
            var f = new Fixture(NpgsqlDataSource.Create(builder.ConnectionString), schema, new Clock());
            await f.Accounts.InitializeAsync(CancellationToken.None);
            await f.Alliances.InitializeAsync(CancellationToken.None);
            await f.Auth.InitializeAsync(CancellationToken.None);
            return f;
        }
        public ValueTask DisposeAsync() => source.DisposeAsync();
        public async Task<Guid> Account()
        {
            var created = await Accounts.CreateAsync(CancellationToken.None);
            Guid id = (await Accounts.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
            // Fixture prerequisites only; all measured transitions use the common writer.
            var state = new EstateApplication(Estate).Create(clock.Ms).State;
            state = state with { Estate = state.Estate! with { Buildings = state.Estate!.Buildings.Select(b =>
                b.Kind == Training.Rules.BarracksKind ? b with { Level = 2 } : b).ToArray() },
                Wallet = state.Wallet.ToDictionary(p => p.Key, p => Estate.Config.WalletCaps[p.Key]) };
            await Sql("UPDATE accounts SET payload=$2 WHERE id=$1", id, MetaSaveCodec.Encode(state));
            return id;
        }
        public async Task Sql(string sql, params object[] values)
        {
            await using var q = source.CreateCommand(sql);
            foreach (var value in values) { q.Parameters.AddWithValue(value); }
            await q.ExecuteNonQueryAsync();
        }
        public async Task<long> Number(string sql, params object[] values)
        {
            await using var q = source.CreateCommand(sql);
            foreach (var value in values) { q.Parameters.AddWithValue(value); }
            return (long)(await q.ExecuteScalarAsync())!;
        }
        public Task<long> Version(Guid id) => Number("SELECT version FROM accounts WHERE id=$1", id);
        public async Task<TrainingState> State(Guid id)
        {
            await using var q = source.CreateCommand("SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='training'");
            q.Parameters.AddWithValue(id);
            return JsonSerializer.Deserialize<TrainingState>((string)(await q.ExecuteScalarAsync())!, TrainingContent.Json)!;
        }
        public async Task Start(Guid id, int count)
        {
            var command = new AccountCommand(Guid.NewGuid(), await Version(id), "training", "start",
                JsonSerializer.SerializeToElement(new TrainingStart("slg:levy", count), TrainingContent.Json));
            var reply = JsonSerializer.Deserialize<AccountReply>(await Accounts.ExecuteAccountAsync(id, command, CancellationToken.None), AccountStore.Json)!;
            Assert.True(reply.Done, reply.Reason);
        }
        public Task<string> Settle(Guid id) => Accounts.ExecuteAccountAsync(id, null, CancellationToken.None);
        public async Task<AllianceReply> Alliance(Guid id, string operation, object payload)
        {
            var command = new AccountCommand(Guid.NewGuid(), await Version(id), "alliance", operation, JsonSerializer.SerializeToElement(payload));
            return JsonSerializer.Deserialize<AllianceReply>(await Alliances.ExecuteAsync(id, command, CancellationToken.None), AccountStore.Json)!;
        }
        public async Task SeedResearch(Guid id, long completesAt)
        {
            var node = Research.Nodes.Single(n => n.Domain == "training.speedPercent");
            var state = new ResearchState(2, [new ResearchJob(1, node.Id, node.Level, clock.Ms, completesAt)],
                new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
            await using var q = source.CreateCommand("INSERT INTO account_domains(account_id,domain,state_version,content_hash,state_json) VALUES($1,'research',1,$2,$3)");
            q.Parameters.AddWithValue(id); q.Parameters.AddWithValue(Research.Hash);
            q.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(state, AccountStore.Json));
            await q.ExecuteNonQueryAsync();
        }
        public async Task<(Guid Requester, Guid Giver, Guid Alliance, string Key)> HelpSetup()
        {
            Guid giver = await Account(), requester = await Account();
            var created = await Alliance(giver, "create", new { name = "Training", mode = "open" });
            Assert.True(created.Done, created.Reason);
            Assert.NotNull(created.CreatedId);
            var alliance = created.CreatedId.Value;
            Assert.True((await Alliance(requester, "apply", new { allianceId = alliance })).Done);
            await Start(requester, 10);
            string key = "training:" + (await State(requester)).Active!.JobId.ToString("D");
            Assert.True((await Alliance(requester, "request-help", new { jobKey = key })).Done);
            return (requester, giver, alliance, key);
        }
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task CompletedResearchAffectsStartThroughCommonWriterAtExactBoundary()
    {
        await using var f = await Fixture.Create();
        Guid before = await f.Account(), exact = await f.Account();
        long boundary = f.Time.Ms + 1000;
        await f.SeedResearch(before, boundary); await f.SeedResearch(exact, boundary);
        f.Time.At(boundary - 1); await f.Start(before, 2);
        f.Time.At(boundary); await f.Start(exact, 2);
        var unchanged = (await f.State(before)).Active!;
        var accelerated = (await f.State(exact)).Active!;
        Assert.Equal(0, unchanged.BonusPermille);
        Assert.True(accelerated.BonusPermille > 0);
        Assert.True(accelerated.CompletesAtMilliseconds - accelerated.StartedAtMilliseconds <
            unchanged.CompletesAtMilliseconds - unchanged.StartedAtMilliseconds);
        await f.Settle(before);
        Assert.Equal(JsonSerializer.Serialize(unchanged, TrainingContent.Json), JsonSerializer.Serialize((await f.State(before)).Active, TrainingContent.Json));
        Assert.Equal(1L, await f.Number("SELECT count(*) FROM domain_events WHERE account_id=$1 AND kind='research-completed'", exact));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task OfflineAndStepwiseSettlementPromoteQueueWithSameResearchSnapshot()
    {
        await using var f = await Fixture.Create();
        Guid step = await f.Account(), late = await f.Account();
        long started = f.Time.Ms;
        foreach (var id in new[] { step, late })
        {
            await f.SeedResearch(id, started + 10000);
            await f.Start(id, 2); await f.Start(id, 1);
        }
        long firstEnd = (await f.State(step)).Active!.CompletesAtMilliseconds!.Value;
        f.Time.At(firstEnd); await f.Settle(step);
        var promoted = (await f.State(step)).Active!;
        Assert.True(promoted.BonusPermille > 0);
        f.Time.At(promoted.CompletesAtMilliseconds!.Value); await f.Settle(step);
        f.Time.At(started + 86_400_000); await f.Settle(late);
        var a = await f.State(step); var b = await f.State(late);
        Assert.Null(a.Active); Assert.Null(a.Waiting); Assert.Null(b.Active); Assert.Null(b.Waiting);
        Assert.Equal(a.Units, b.Units);
        Assert.Equal(3, b.Units.Single(u => u.UnitId == "slg:levy").Ready);
        foreach (var id in new[] { step, late })
        {
            Assert.Equal(2L, await f.Number("SELECT count(*) FROM domain_events WHERE account_id=$1 AND kind='training-completed'", id));
            Assert.Equal(promoted.CompletesAtMilliseconds.Value, await f.Number("SELECT max((data->>'completedAtMilliseconds')::bigint) FROM domain_events WHERE account_id=$1 AND kind='training-completed'", id));
        }
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task TrainingHelpPersistsDeadlineVersionOutboxAndReplaysAfterStoreRestart()
    {
        await using var f = await Fixture.Create();
        var setup = await f.HelpSetup();
        var old = (await f.State(setup.Requester)).Active!;
        long version = await f.Version(setup.Requester);
        var command = new AccountCommand(Guid.NewGuid(), await f.Version(setup.Giver), "alliance", "give-help",
            JsonSerializer.SerializeToElement(new { requester = setup.Requester, jobKey = setup.Key }));
        var reply = JsonSerializer.Deserialize<AllianceReply>(await f.Alliances.ExecuteAsync(setup.Giver, command, CancellationToken.None), AccountStore.Json)!;
        Assert.True(reply.Done); Assert.True(reply.ShortenedMs > 0);
        var shortened = (await f.State(setup.Requester)).Active!;
        Assert.Equal(old.CompletesAtMilliseconds - reply.ShortenedMs, shortened.CompletesAtMilliseconds);
        Assert.Equal(version + 1, await f.Version(setup.Requester));
        Assert.Equal(1L, await f.Number("SELECT count(*) FROM outbox WHERE account_id=$1 AND version=$2", setup.Requester, version + 1));
        Assert.Equal(1L, await f.Number("SELECT count(*) FROM domain_events WHERE account_id=$1 AND kind='alliance-help-applied'", setup.Requester));
        // Properties construct fresh store instances against persisted state; no in-memory job cache is retained.
        var replay = JsonSerializer.Deserialize<AllianceReply>(await f.Alliances.ExecuteAsync(setup.Giver, command, CancellationToken.None), AccountStore.Json)!;
        Assert.Equal(reply.ShortenedMs, replay.ShortenedMs);
        Assert.Equal(version + 1, await f.Version(setup.Requester));
        Assert.Equal(JsonSerializer.Serialize(shortened, TrainingContent.Json), JsonSerializer.Serialize((await f.State(setup.Requester)).Active, TrainingContent.Json));
        f.Time.At(shortened.CompletesAtMilliseconds!.Value); await f.Settle(setup.Requester);
        Assert.Equal(10, (await f.State(setup.Requester)).Units.Single(u => u.UnitId == "slg:levy").Ready);
        Assert.Equal(1L, await f.Number("SELECT count(*) FROM domain_events WHERE account_id=$1 AND kind='training-completed'", setup.Requester));
    }

    [Fact, Trait("Category", "Postgres")]
    public async Task GiveHelpAndActualAccountDeletionWaitForAllianceThenTerminateWithoutDeadlock()
    {
        await using var f = await Fixture.Create();
        var setup = await f.HelpSetup();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var command = new AccountCommand(Guid.NewGuid(), await f.Version(setup.Giver), "alliance", "give-help",
            JsonSerializer.SerializeToElement(new { requester = setup.Requester, jobKey = setup.Key }));
        await using var gate = await f.Source.OpenConnectionAsync(timeout.Token);
        await using var transaction = await gate.BeginTransactionAsync(timeout.Token);
        await using (var row = new NpgsqlCommand("SELECT id FROM alliances WHERE id=$1 FOR UPDATE", gate, transaction))
        { row.Parameters.AddWithValue(setup.Alliance); await row.ExecuteScalarAsync(timeout.Token); }
        var help = f.Alliances.ExecuteAsync(setup.Giver, command, timeout.Token);
        var delete = f.Auth.DeleteAccountAsync(setup.Requester, timeout.Token);
        bool released = false;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (true)
            {
                await using var blocked = f.Source.CreateCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name=$1 AND cardinality(pg_blocking_pids(pid))>0");
                blocked.Parameters.AddWithValue(f.Name);
                if ((long)(await blocked.ExecuteScalarAsync(timeout.Token))! >= 2) { break; }
                await timer.WaitForNextTickAsync(timeout.Token);
            }
            await transaction.CommitAsync(timeout.Token);
            released = true;
            await Task.WhenAll(help, delete).WaitAsync(timeout.Token);
        }
        finally
        {
            timeout.Cancel();
            if (!released) { await transaction.RollbackAsync(CancellationToken.None); }
            // Observe both tasks even when the barrier fails; do not leave background mutations in the fixture.
            try { await Task.WhenAll(help, delete); } catch (OperationCanceledException) { }
        }
        var result = JsonSerializer.Deserialize<AllianceReply>(await help, AccountStore.Json)!;
        Assert.True(result.Done || result.Reason is "unknown-request" or "request-closed");
        Assert.Equal(1L, await f.Number("SELECT count(*) FROM auth_deleted_accounts WHERE account_id=$1", setup.Requester));
        Assert.Equal(0L, await f.Number("SELECT count(*) FROM alliance_members WHERE account_id=$1", setup.Requester));
        // Soft deletion preserves prior grant history; either serial order is legal, but never a duplicate grant.
        Assert.Equal(result.Done ? 1L : 0L, await f.Number("SELECT count(*) FROM alliance_help_grants WHERE requester=$1", setup.Requester));
        Assert.Equal(1L, await f.Number("SELECT count(*) FROM alliance_members WHERE alliance_id=$1 AND role='leader'", setup.Alliance));
        Assert.Equal(1L, await f.Number("SELECT member_count::bigint FROM alliances WHERE id=$1", setup.Alliance));
    }
}
