using System.Text.Json;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class HeldMaterialTests
{
    private const string Grain = "meta:grain";

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Test domain: "credit" asks the writer for materials, "spend" removes them from the wallet.</summary>
    private sealed class Bank(string name) : IDomainHandler
    {
        public string Domain => name;
        public int StateVersion => 1;
        public string ContentHash => "test-bank-v1";
        public JsonElement InitialState => JsonSerializer.SerializeToElement(new { });
        public JsonElement View(DomainContext context) => context.State;
        public DomainResult Advance(DomainContext context) => new(context.Account.State, context.State, context.State, true, "", []);
        public DomainResult Execute(DomainContext context, string operation, JsonElement payload)
        {
            string material = payload.GetProperty("material").GetString()!;
            int amount = payload.GetProperty("amount").GetInt32();
            if (operation == "held")
            {
                // Echo what the writer reports as this domain's held amount.
                return new(context.Account.State, JsonSerializer.SerializeToElement(new { held = context.Held?.GetValueOrDefault(material) ?? -1 }), context.State, true, "held", []);
            }
            if (operation == "credit")
            {
                return new(context.Account.State, context.State, context.State, true, "credited", [], [new(material, amount)]);
            }
            var wallet = new Dictionary<string, int>(context.Account.State.Wallet);
            wallet[material] -= amount;
            return new(context.Account.State with { Wallet = wallet }, context.State, context.State, true, "spent", []);
        }
    }

    private sealed record Setup(NpgsqlDataSource Source, AccountStore Store, ManualClock Clock, Guid Id) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Source.DisposeAsync();

        public async Task<AccountReply> RunAsync(string domain, string operation, int amount)
        {
            long version = Parse(await Store.ExecuteAccountAsync(Id, null, CancellationToken.None)).Estate.Version;
            var command = new AccountCommand(Guid.NewGuid(), version, domain, operation, JsonSerializer.SerializeToElement(new { material = Grain, amount }));
            var reply = Parse(await Store.ExecuteAccountAsync(Id, command, CancellationToken.None));
            Clock.Now = Clock.Now.AddSeconds(1);
            return reply;
        }

        public async Task<JsonElement[]> HeldEventsAsync()
        {
            await using var query = Source.CreateCommand("SELECT data::text FROM domain_events WHERE account_id=$1 AND domain='@held' ORDER BY version,ordinal");
            query.Parameters.AddWithValue(Id);
            await using var reader = await query.ExecuteReaderAsync();
            var list = new List<JsonElement>();
            while (await reader.ReadAsync())
            {
                using var document = JsonDocument.Parse(reader.GetString(0));
                list.Add(document.RootElement.Clone());
            }
            return list.ToArray();
        }
    }

    private static AccountReply Parse(string json) => JsonSerializer.Deserialize<AccountReply>(json, AccountStore.Json)!;

    private static async Task<Setup> CreateAsync()
    {
        var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required."));
        var clock = new ManualClock();
        var store = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock,
            new DomainRegistry([new Bank("a"), new Bank("z")]));
        await store.InitializeAsync(CancellationToken.None);
        var created = await store.CreateAsync(CancellationToken.None);
        return new(source, store, clock, (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value);
    }

    private static int Amount(ResourceAmount[]? list, string id) => list?.SingleOrDefault(r => r.Id == id)?.Amount ?? 0;

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task HeldMaterialsAreCreditedInArrivalOrderNotDomainOrder()
    {
        await using var setup = await CreateAsync();
        var app = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
        int cap = app.WalletCap(Grain);
        int start = Amount((await setup.RunAsync("a", "credit", 1)).Resources, Grain);
        var full = await setup.RunAsync("z", "credit", cap - start);
        Assert.Equal((cap, 0), (Amount(full.Resources, Grain), Amount(full.Held, Grain)));
        // "z" overflows first, "a" later: arrival order must win over domain name order.
        var zHeld = await setup.RunAsync("z", "credit", 50);
        Assert.Equal((cap, 50), (Amount(zHeld.Resources, Grain), Amount(zHeld.Held, Grain)));
        var aHeld = await setup.RunAsync("a", "credit", 40);
        Assert.Equal(90, Amount(aHeld.Held, Grain));
        // Spending 60 makes room right after the spend: z's 50 first, then 10 of a's 40.
        var spent = await setup.RunAsync("a", "spend", 60);
        Assert.Equal((cap, 30), (Amount(spent.Resources, Grain), Amount(spent.Held, Grain)));
        var credited = (await setup.HeldEventsAsync()).Select(e => (e.GetProperty("domain").GetString()!, e.GetProperty("amount").GetInt32())).ToArray();
        Assert.Equal(new[] { ("z", 50), ("a", 10) }, credited);
        // A restarted store keeps the held remainder and credits it when room appears.
        var restarted = new AccountStore(setup.Source, app, setup.Clock, new DomainRegistry([new Bank("a"), new Bank("z")]));
        await restarted.InitializeAsync(CancellationToken.None);
        Assert.Equal(30, Amount(Parse(await restarted.ExecuteAccountAsync(setup.Id, null, CancellationToken.None)).Held, Grain));
        Assert.Equal(30, (await setup.RunAsync("a", "held", 0)).Domains["a"].GetProperty("held").GetInt32());
        Assert.Equal(0, (await setup.RunAsync("z", "held", 0)).Domains["z"].GetProperty("held").GetInt32());
        var after = await setup.RunAsync("z", "spend", 100);
        Assert.Equal((cap - 70, 0), (Amount(after.Resources, Grain), Amount(after.Held, Grain)));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task HeldMaterialsEnterBeforeAnEstateProductionClaim()
    {
        await using var setup = await CreateAsync();
        var app = new EstateApplication(EstateContent.Load(ContentEstateTests.Root()));
        int cap = app.WalletCap(Grain);
        int start = Amount(Parse(await setup.Store.ExecuteAccountAsync(setup.Id, null, CancellationToken.None)).Resources, Grain);
        await setup.RunAsync("a", "credit", cap - start);
        Assert.Equal(30, Amount((await setup.RunAsync("a", "credit", 30)).Held, Grain));
        // Outside the writer: room for 30 appears and a production site holds stock.
        await using (var read = setup.Source.CreateCommand("SELECT payload FROM accounts WHERE id=$1"))
        {
            read.Parameters.AddWithValue(setup.Id);
            var state = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
            var estate = state.Estate! with { Buildings = state.Estate!.Buildings.Select(b => b.Kind == "m3a:field" ? b with { Level = 1, Stock = 100 } : b).ToArray() };
            var wallet = new Dictionary<string, int>(state.Wallet) { [Grain] = cap - 30 };
            await using var write = setup.Source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
            write.Parameters.AddWithValue(setup.Id); write.Parameters.AddWithValue(MetaSaveCodec.Encode(state with { Estate = estate, Wallet = wallet }));
            await write.ExecuteNonQueryAsync();
        }
        // Read the version without a writer pass so the claim request is the first one to see the room.
        await using var current = setup.Source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
        current.Parameters.AddWithValue(setup.Id);
        long version = (long)(await current.ExecuteScalarAsync())!;
        await setup.Store.ExecuteAsync(setup.Id, new EstateCommand(Guid.NewGuid(), version, "claim-production", null), CancellationToken.None);
        var account = Parse(await setup.Store.ExecuteAccountAsync(setup.Id, null, CancellationToken.None));
        Assert.Equal((cap, 0), (Amount(account.Resources, Grain), Amount(account.Held, Grain)));
        Assert.Contains(await setup.HeldEventsAsync(), e => e.GetProperty("amount").GetInt32() == 30);
    }

    [Fact]
    public void EffectiveWalletCapIsTheSmallerOfConfigAndCatalog()
    {
        var content = EstateContent.Load(ContentEstateTests.Root());
        var app = new EstateApplication(content);
        foreach (var cap in content.Config.WalletCaps)
        {
            Assert.Equal(Math.Min(cap.Value, content.Meta.Materials.Single(m => m.Id == cap.Key).WalletCap), app.WalletCap(cap.Key));
        }
    }
}
