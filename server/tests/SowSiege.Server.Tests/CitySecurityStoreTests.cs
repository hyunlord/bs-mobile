using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

[Trait("Category", "Postgres")]
public sealed class CitySecurityStoreTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ms { get; set; } = 1000000;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(Ms);
    }

    private sealed class Fixture(NpgsqlDataSource source, AccountStore accounts, CitySecurityStore security, Clock clock) : IAsyncDisposable
    {
        public NpgsqlDataSource Source => source;
        public CitySecurityStore Security => security;
        public Clock Time => clock;
        public static readonly CitySecurityPolicy Policy = new(240, 40, 240, 30, 30, [new("meta:grain", 100)], 100, "test-policy");

        public static async Task<Fixture> Create()
        {
            string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES") ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES required.");
            await using var admin = NpgsqlDataSource.Create(connection);
            string schema = "security_" + Guid.NewGuid().ToString("N");
            await using (var create = admin.CreateCommand("CREATE SCHEMA " + schema)) { await create.ExecuteNonQueryAsync(); }
            var builder = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, CommandTimeout = 10 };
            var source = NpgsqlDataSource.Create(builder.ConnectionString);
            var clock = new Clock();
            var security = new CitySecurityStore(source, Policy, clock);
            var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock, citySecurity: security);
            await accounts.InitializeAsync(default);
            await security.InitializeAsync(default);
            await using var tables = source.CreateCommand("""
                CREATE TABLE auth_deleted_accounts(account_id uuid PRIMARY KEY REFERENCES accounts(id),deleted_at timestamptz NOT NULL);
                CREATE TABLE alliance_members(account_id uuid PRIMARY KEY REFERENCES accounts(id),alliance_id uuid NOT NULL);
                """);
            await tables.ExecuteNonQueryAsync();
            return new(source, accounts, security, clock);
        }

        public async Task<Guid> Account()
        {
            var created = await accounts.CreateAsync(default);
            return (await accounts.AuthenticateAsync(created.Token, default))!.Value;
        }

        public async Task<T> Transaction<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> operation, bool commit = true)
        {
            await using var c = await source.OpenConnectionAsync();
            await using var t = await c.BeginTransactionAsync();
            T result = await operation(c, t);
            if (commit) { await t.CommitAsync(); }
            return result;
        }

        public Task<CitySecurityDecision> Dispatch(Guid march, Guid actor, Guid target, string operation = "attack", bool commit = true) =>
            Transaction((c, t) => security.DispatchAsync(c, t, march, actor, target, operation, clock.Ms, default), commit);

        public Task<CitySecurityDecision> Arrival(Guid march, Guid actor, Guid target, string operation = "attack") =>
            Transaction((c, t) => security.ArriveAsync(c, t, march, actor, target, operation, clock.Ms, default));

        public Task<bool> Return(Guid march, Guid actor) =>
            Transaction(async (c, t) => { await security.ReturnAsync(c, t, march, actor, clock.Ms, default); return true; });

        public async Task Execute(string sql, params object[] args)
        {
            await using var command = source.CreateCommand(sql);
            foreach (var arg in args) { command.Parameters.AddWithValue(arg); }
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync() => source.DisposeAsync();
    }

    [Fact]
    public async Task FailedDispatchLeavesAttackersNewShieldUnchanged()
    {
        await using var f = await Fixture.Create();
        Guid attacker = await f.Account(), target = await f.Account();
        var before = await f.Security.ViewAsync(attacker, default);
        Assert.Equal("target-protected", (await f.Dispatch(Guid.NewGuid(), attacker, target)).Reason);
        Assert.Equal(before.ShieldUntilMs, (await f.Security.ViewAsync(attacker, default)).ShieldUntilMs);
        Assert.Equal("own-city", (await f.Dispatch(Guid.NewGuid(), attacker, attacker)).Reason);
    }

    [Fact]
    public async Task SuccessfulDispatchRollbackRestoresShieldAndDoesNotReserveMarch()
    {
        await using var f = await Fixture.Create();
        Guid target = await f.Account();
        f.Time.Ms += 240;
        Guid actor = await f.Account(), march = Guid.NewGuid();
        long until = (await f.Security.ViewAsync(actor, default)).ShieldUntilMs;
        Assert.True((await f.Dispatch(march, actor, target, commit: false)).Allowed);
        Assert.Equal(until, (await f.Security.ViewAsync(actor, default)).ShieldUntilMs);
        Assert.True((await f.Dispatch(march, actor, target)).Allowed);
        Assert.Equal(0, (await f.Security.ViewAsync(actor, default)).ShieldUntilMs);
        Assert.Equal("hostile-march-active", (await f.Security.ActivateAsync(actor, Guid.NewGuid(), default)).Reason);
    }

    [Fact]
    public async Task TargetShieldAtArrivalBoundaryAndDecisionSurviveRestart()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account(), target = await f.Account(), march = Guid.NewGuid();
        f.Time.Ms += 240;
        Assert.True((await f.Dispatch(march, actor, target)).Allowed);
        Assert.True((await f.Security.ActivateAsync(target, Guid.NewGuid(), default)).Done);
        f.Time.Ms += 39;
        var result = await f.Arrival(march, actor, target);
        Assert.Equal("target-protected", result.Reason);
        var restarted = new CitySecurityStore(f.Source, Fixture.Policy, f.Time);
        await restarted.InitializeAsync(default);
        f.Time.Ms += 1;
        var replayed = await f.Transaction((c, t) => restarted.ArriveAsync(c, t, march, actor, target, "attack", f.Time.Ms, default));
        Assert.Equal(result, replayed);
        Guid second = Guid.NewGuid();
        Assert.True((await f.Dispatch(second, actor, target)).Allowed);
        Assert.True((await f.Arrival(second, actor, target)).Allowed);
    }

    [Fact]
    public async Task RelationChangeDuringTravelReturnsWithoutReport()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account(), target = await f.Account(), march = Guid.NewGuid(), alliance = Guid.NewGuid();
        f.Time.Ms += 240;
        Assert.True((await f.Dispatch(march, actor, target, "scout")).Allowed);
        await f.Execute("INSERT INTO alliance_members VALUES($1,$3),($2,$3)", actor, target, alliance);
        var result = await f.Arrival(march, actor, target, "scout");
        Assert.Equal("same-alliance", result.Reason);
        Assert.Null(result.Scout);
        Assert.Equal("same-alliance", (await f.Dispatch(Guid.NewGuid(), actor, target)).Reason);
    }

    [Fact]
    public async Task ConcurrentActivationGrantsOnceAndDuplicateIsStable()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account();
        f.Time.Ms += 240;
        var replies = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => f.Security.ActivateAsync(actor, Guid.NewGuid(), default)));
        Assert.Single(replies, r => r.Done);
        Guid key = Guid.NewGuid();
        var rejected = await f.Security.ActivateAsync(actor, key, default);
        f.Time.Ms += 1000;
        Assert.Equal(rejected, await f.Security.ActivateAsync(actor, key, default));
    }

    [Fact]
    public async Task LastReturnCooldownCannotBeShortenedByDuplicateOrClockRollback()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account(), target = await f.Account(), a = Guid.NewGuid(), b = Guid.NewGuid();
        f.Time.Ms += 240;
        Assert.True((await f.Dispatch(a, actor, target)).Allowed);
        Assert.True((await f.Dispatch(b, actor, target)).Allowed);
        await f.Return(a, actor);
        f.Time.Ms += 100;
        Assert.Equal("hostile-march-active", (await f.Security.ActivateAsync(actor, Guid.NewGuid(), default)).Reason);
        await f.Return(b, actor);
        long until = (await f.Security.ViewAsync(actor, default)).HostileUntilMs;
        f.Time.Ms -= 100;
        await f.Return(a, actor);
        Assert.Equal(until, (await f.Security.ViewAsync(actor, default)).HostileUntilMs);
        f.Time.Ms = until - 1;
        Assert.Equal("hostile-cooldown", (await f.Security.ActivateAsync(actor, Guid.NewGuid(), default)).Reason);
        f.Time.Ms = until;
        Assert.True((await f.Security.ActivateAsync(actor, Guid.NewGuid(), default)).Done);
    }

    [Fact]
    public async Task ReversedConcurrentAttackLocksDoNotDeadlock()
    {
        await using var f = await Fixture.Create();
        Guid a = await f.Account(), b = await f.Account();
        f.Time.Ms += 240;
        var replies = await Task.WhenAll(f.Dispatch(Guid.NewGuid(), a, b), f.Dispatch(Guid.NewGuid(), b, a))
            .WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(replies, reply => Assert.True(reply.Allowed));
    }

    [Fact]
    public async Task ScoutOnlyDisclosesReadyTroopsAndWhitelistedWalletLimits()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account(), target = await f.Account(), march = Guid.NewGuid();
        string state = JsonSerializer.Serialize(new { units = new[] { new { unitId = "slg:guard", ready = 3, deployed = 999, wounded = 999, reserved = 999 } }, secret = "not-disclosed" });
        await f.Execute("INSERT INTO account_domains VALUES($1,'training',1,'fixture',$2::jsonb)", target, state);
        f.Time.Ms += 240;
        Assert.True((await f.Dispatch(march, actor, target, "scout")).Allowed);
        var report = (await f.Arrival(march, actor, target, "scout")).Scout!;
        Assert.Equal(f.Time.Ms, report.ObservedAtMs);
        Assert.Equal(new ScoutTroops("slg:guard", 3), Assert.Single(report.Troops));
        Assert.Equal("meta:grain", Assert.Single(report.Loot).MaterialId);
        string json = JsonSerializer.Serialize(report);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("deployed", json);
        Assert.DoesNotContain("999", json);
    }

    [Fact]
    public async Task ChangedContentCannotReinterpretPersistedShield()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account();
        var changed = new CitySecurityStore(f.Source, Fixture.Policy with { Hash = "different" }, f.Time);
        await Assert.ThrowsAsync<CommandConflictException>(() => changed.ViewAsync(actor, default));
    }
    [Fact]
    public async Task PurgedTargetDoesNotBlockPurgeOrPreventSurvivingArmyReturn()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account(), target = await f.Account(), march = Guid.NewGuid();
        f.Time.Ms += 240;
        Assert.True((await f.Dispatch(march, actor, target)).Allowed);
        await f.Execute("DELETE FROM sessions WHERE account_id=$1", target);
        await f.Execute("DELETE FROM accounts WHERE id=$1", target);
        Assert.Equal("account-missing", (await f.Arrival(march, actor, target)).Reason);
        await f.Return(march, actor);
        Assert.Equal(f.Time.Ms + 30, (await f.Security.ViewAsync(actor, default)).HostileUntilMs);
        await f.Execute("DELETE FROM sessions WHERE account_id=$1", actor);
        await f.Execute("DELETE FROM accounts WHERE id=$1", actor);
        await using var count = f.Source.CreateCommand("SELECT (SELECT count(*) FROM city_security)+(SELECT count(*) FROM city_hostilities)+(SELECT count(*) FROM city_security_events)");
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ActivationAndArrivalAreSerializedAndArrivalReceiptDoesNotChange()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account(), target = await f.Account(), march = Guid.NewGuid();
        f.Time.Ms += 240;
        Assert.True((await f.Dispatch(march, actor, target)).Allowed);
        Task<CityShieldReply> activate = f.Security.ActivateAsync(target, Guid.NewGuid(), default);
        Task<CitySecurityDecision> arrive = f.Arrival(march, actor, target);
        await Task.WhenAll(activate, arrive).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True((await activate).Done);
        Assert.Contains((await arrive).Reason, new[] { "arrived", "target-protected" });
        f.Time.Ms += 100;
        Assert.Equal(await arrive, await f.Arrival(march, actor, target));
    }

    [Fact]
    public async Task PersistedMarchesFailClosedWithoutRecoveryOwner()
    {
        await using var f = await Fixture.Create();
        Guid actor = await f.Account();
        await f.Execute("CREATE TABLE world_marches(id uuid PRIMARY KEY)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Security.ViewAsync(actor, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Security.ActivateAsync(actor, Guid.NewGuid(), default));
    }

}
