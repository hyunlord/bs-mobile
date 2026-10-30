using System.Text.Json;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class DomainFoundationTests
{
    private static JsonElement Object(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void RegistryRejectsDuplicatesAndOrdersDomains()
    {
        Assert.Throws<InvalidOperationException>(() => new DomainRegistry([new Counter("a"), new Counter("a")]));
        Assert.Equal(new[] { "a", "z" }, new DomainRegistry([new Counter("z"), new Counter("a")]).Handlers.Select(h => h.Domain));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task LegacyAndDomainCommandsShareWriterClockAndReceipts()
    {
        await using var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES required."));
        var time = new Clock();
        var app = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
        var store = new AccountStore(source, app, time, new DomainRegistry([new Counter("test")]));
        await store.InitializeAsync(CancellationToken.None);
        var created = await store.CreateAsync(CancellationToken.None);
        Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        var request = new AccountCommand(Guid.NewGuid(), 0, "test", "spend", Object(new { amount = 1 }));
        string first = await store.ExecuteAccountAsync(id, request, CancellationToken.None);
        var response = JsonSerializer.Deserialize<AccountReply>(first, AccountStore.Json)!;
        Assert.Equal(119, response.Resources.Single(r => r.Id == "meta:grain").Amount);
        time.Now = time.Now.AddDays(10);
        Assert.Equal(first, await store.ExecuteAccountAsync(id, request, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => store.ExecuteAsync(id,
            new EstateCommand(request.CommandId, 0, "claim-production", null), CancellationToken.None));
        await store.ExecuteAsync(id, null, CancellationToken.None);
        await using var query = source.CreateCommand("SELECT state_json->>'utc',state_json->>'advances' FROM account_domains WHERE account_id=$1 AND domain='test'");
        query.Parameters.AddWithValue(id);
        await using (var reader = await query.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(time.Now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), reader.GetString(0));
            Assert.Equal("2", reader.GetString(1));
        }
        time.Now = time.Now.AddDays(-1);
        await store.ExecuteAsync(id, null, CancellationToken.None);
        var missing = new AccountStore(source, app, time);
        await Assert.ThrowsAsync<CommandConflictException>(() => missing.ExecuteAsync(id, null, CancellationToken.None));
        var changed = new AccountStore(source, app, time, new DomainRegistry([new Counter("test", "different")]));
        await Assert.ThrowsAsync<CommandConflictException>(() => changed.ExecuteAsync(id, null, CancellationToken.None));
        await using var clock = source.CreateCommand("SELECT utc_highwater FROM account_job_clocks WHERE account_id=$1");
        clock.Parameters.AddWithValue(id);
        Assert.Equal(time.Now.AddDays(1).ToUnixTimeMilliseconds(), await clock.ExecuteScalarAsync());
        // A late outbox failure must roll back wallet, domain state, event and receipt together.
        await using var collision = source.CreateCommand("INSERT INTO outbox VALUES($1,4,'collision')");
        collision.Parameters.AddWithValue(id);
        await collision.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<PostgresException>(() => store.ExecuteAccountAsync(id, request with { CommandId = Guid.NewGuid(), ExpectedVersion = 3 }, CancellationToken.None));
        await using var count = source.CreateCommand("SELECT version,(SELECT count(*) FROM commands WHERE account_id=$1),(SELECT count(*) FROM domain_events WHERE account_id=$1) FROM accounts WHERE id=$1");
        count.Parameters.AddWithValue(id);
        await using var counts = await count.ExecuteReaderAsync();
        Assert.True(await counts.ReadAsync());
        Assert.Equal(3, counts.GetInt64(0)); Assert.Equal(1, counts.GetInt64(1)); Assert.Equal(1, counts.GetInt64(2));
    }

    [Fact]
    public void DomainWalletCannotExceedPinnedEstateCap()
    {
        var app = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
        var account = app.Create(0);
        var wallet = new Dictionary<string, int>(account.State.Wallet) { ["meta:grain"] = 2001 };
        Assert.Throws<InvalidDataException>(() => app.Validate(account.State with { Wallet = wallet }));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task DomainSettlementPrecedesLegacySpendingAndFinalViewReflectsResult()
    {
        await using var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Postgres required."));
        var store = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())),
            TimeProvider.System, new DomainRegistry([new Drain()]));
        await store.InitializeAsync(CancellationToken.None);
        var created = await store.CreateAsync(CancellationToken.None);
        Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        int building = created.Estate.Buildings.Single(b => b.Kind == "meta:granary").Id;
        var command = new EstateCommand(Guid.NewGuid(), 0, "start-construction", building);
        var reply = JsonSerializer.Deserialize<CommandReply>(await store.ExecuteAsync(id, command, CancellationToken.None), AccountStore.Json)!;
        Assert.False(reply.Done);
        Assert.Null(reply.Estate.Construction);
        Assert.Equal(0, reply.Estate.Wallet["meta:timber"]);
        Assert.Equal(1, reply.Estate.Version);
        var account = JsonSerializer.Deserialize<AccountReply>(await store.ExecuteAccountAsync(id, null, CancellationToken.None), AccountStore.Json)!;
        Assert.Equal(0, account.Domains["drain"].GetProperty("timber").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task PendingBalanceFillsAfterLegacyOrOtherDomainSpend(bool legacy)
    {
        await using var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("Postgres required."));
        var store = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())),
            new Clock(), new DomainRegistry([new Counter("spender"), new PendingBalance()]));
        await store.InitializeAsync(CancellationToken.None);
        var created = await store.CreateAsync(CancellationToken.None);
        Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        Guid commandId = Guid.NewGuid();
        var estate = new EstateCommand(commandId, 0, "start-construction", created.Estate.Buildings.Single(b => b.Kind == "meta:granary").Id);
        var domain = new AccountCommand(commandId, 0, "spender", "spend", Object(new { amount = 10 }));
        string reply = legacy ? await store.ExecuteAsync(id, estate, CancellationToken.None)
            : await store.ExecuteAccountAsync(id, domain, CancellationToken.None);
        using var parsed = JsonDocument.Parse(reply);
        Assert.True(parsed.RootElement.GetProperty("done").GetBoolean());
        Assert.Equal(120, parsed.RootElement.GetProperty("estate").GetProperty("wallet").GetProperty("meta:grain").GetInt32());
        Assert.Equal(reply, legacy ? await store.ExecuteAsync(id, estate, CancellationToken.None)
            : await store.ExecuteAccountAsync(id, domain, CancellationToken.None));
        await using var query = source.CreateCommand("SELECT count(*) FROM domain_events WHERE account_id=$1 AND domain='refund'");
        query.Parameters.AddWithValue(id);
        Assert.Equal(1L, await query.ExecuteScalarAsync());
    }

    private sealed class PendingBalance : IDomainHandler
    {
        public string Domain => "refund";
        public int StateVersion => 1;
        public string ContentHash => "test-refund-v1";
        public JsonElement InitialState => Object(new { pending = 1000 });
        public JsonElement View(DomainContext context) => context.State;
        public DomainResult Advance(DomainContext context) => new(context.Account.State, context.State, context.State, true, "", []);
        public DomainResult Execute(DomainContext context, string operation, JsonElement payload) => throw new NotSupportedException();
        public DomainResult AfterCommand(DomainContext context)
        {
            int amount = Math.Max(0, 120 - context.Account.State.Wallet["meta:grain"]);
            var wallet = new Dictionary<string, int>(context.Account.State.Wallet) { ["meta:grain"] = 120 };
            var state = Object(new { pending = context.State.GetProperty("pending").GetInt32() - amount });
            return new(context.Account.State with { Wallet = wallet }, state, state, true, "", [new("credited", Object(new { amount }))]);
        }
    }

    private sealed class Drain : IDomainHandler
    {
        public string Domain => "drain";
        public int StateVersion => 1;
        public string ContentHash => "test-drain-v1";
        public JsonElement InitialState => Object(new { });
        public JsonElement View(DomainContext context) => Object(new { timber = context.Account.State.Wallet["meta:timber"] });
        public DomainResult Advance(DomainContext context)
        {
            var wallet = new Dictionary<string, int>(context.Account.State.Wallet) { ["meta:timber"] = 0 };
            return new(context.Account.State with { Wallet = wallet }, context.State, Object(new { }), true, "", []);
        }
        public DomainResult Execute(DomainContext context, string operation, JsonElement payload) => throw new NotSupportedException();
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Counter(string domain, string hash = "counter-v1") : IDomainHandler
    {
        public string Domain => domain;
        public int StateVersion => 1;
        public string ContentHash => hash;
        public JsonElement InitialState => Object(new { advances = 0, utc = 0L });
        public JsonElement View(DomainContext context) => context.State;
        public DomainResult Advance(DomainContext context)
        {
            var state = Object(new { advances = context.State.GetProperty("advances").GetInt32() + 1, utc = context.UtcMilliseconds });
            return new(context.Account.State, state, state, true, "", []);
        }
        public DomainResult Execute(DomainContext context, string operation, JsonElement payload)
        {
            Assert.NotNull(context.CommandId);
            var wallet = new Dictionary<string, int>(context.Account.State.Wallet);
            wallet["meta:grain"] -= payload.GetProperty("amount").GetInt32();
            return new(context.Account.State with { Wallet = wallet }, context.State, context.State, true, "",
                [new("spent", Object(new { amount = 1 }))]);
        }
    }
}
