using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;

namespace SowSiege.Server.Tests;

/// <summary>One account on the shared ADR0078 writer with the recruit and research handlers registered.</summary>
internal sealed class SlgFixture(NpgsqlDataSource source, AccountStore store, SlgFixture.ManualClock clock, RecruitPool pool, ResearchContent research, Guid id) : IAsyncDisposable
{
    internal sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public NpgsqlDataSource Source => source;
    public AccountStore Store => store;
    public ManualClock Clock => clock;
    public RecruitPool Pool => pool;
    public ResearchContent Research => research;
    public Guid Id => id;

    public static async Task<AccountStore> StoreAsync(NpgsqlDataSource source, byte[] key, TimeProvider time)
    {
        string root = ContentEstateTests.Root();
        var registry = new DomainRegistry([new RecruitDomain(RecruitPool.Load(root), 3000), new ResearchDomain(ResearchContent.Load(root))], key);
        var store = new AccountStore(source, new EstateApplication(EstateContent.Load(root)), time, registry);
        await store.InitializeAsync(CancellationToken.None);
        return store;
    }

    public static async Task<SlgFixture> CreateAsync(byte[] key)
    {
        var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required."));
        var clock = new ManualClock();
        var store = await StoreAsync(source, key, clock);
        var created = await store.CreateAsync(CancellationToken.None);
        Guid id = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        string root = ContentEstateTests.Root();
        return new(source, store, clock, RecruitPool.Load(root), ResearchContent.Load(root), id);
    }

    public static AccountReply Parse(string json) => JsonSerializer.Deserialize<AccountReply>(json, AccountStore.Json)!;

    public async Task<AccountReply> ViewAsync() => Parse(await store.ExecuteAccountAsync(id, null, CancellationToken.None));

    public AccountCommand Recruit(long version) =>
        new(Guid.NewGuid(), version, RecruitDomain.Name, "recruit", JsonSerializer.SerializeToElement(new { poolVersion = pool.PoolVersion }));

    /// <summary>Test-only: overwrite one domain's stored state.</summary>
    public async Task SetDomainAsync(string domain, object state)
    {
        await ViewAsync();
        await using var command = source.CreateCommand("UPDATE account_domains SET state_json=$3 WHERE account_id=$1 AND domain=$2");
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(domain);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(state, AccountStore.Json));
        Assert1(await command.ExecuteNonQueryAsync());
    }

    /// <summary>Test-only: raise building levels and set wallet entries in the stored Core payload.</summary>
    public async Task SetEstateAsync(Dictionary<string, int> buildings, Dictionary<string, int>? wallet = null)
    {
        await using var read = source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
        read.Parameters.AddWithValue(id);
        var state = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
        var estate = state.Estate! with { Buildings = state.Estate!.Buildings.Select(b => buildings.TryGetValue(b.Kind, out int level) ? b with { Level = level } : b).ToArray() };
        var purse = new Dictionary<string, int>(state.Wallet);
        foreach (var pair in wallet ?? new Dictionary<string, int>())
        {
            purse[pair.Key] = pair.Value;
        }
        await using var write = source.CreateCommand("UPDATE accounts SET payload=$2 WHERE id=$1");
        write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(MetaSaveCodec.Encode(state with { Estate = estate, Wallet = purse }));
        Assert1(await write.ExecuteNonQueryAsync());
    }

    public async Task<JsonElement[]> EventsAsync(string kind)
    {
        await using var command = source.CreateCommand("SELECT data::text FROM domain_events WHERE account_id=$1 AND kind=$2 ORDER BY version,ordinal");
        command.Parameters.AddWithValue(id); command.Parameters.AddWithValue(kind);
        await using var reader = await command.ExecuteReaderAsync();
        var list = new List<JsonElement>();
        while (await reader.ReadAsync())
        {
            using var document = JsonDocument.Parse(reader.GetString(0));
            list.Add(document.RootElement.Clone());
        }
        return list.ToArray();
    }

    public async Task<long> ScalarAsync(string sql)
    {
        await using var command = source.CreateCommand(sql);
        command.Parameters.AddWithValue(id);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static void Assert1(int rows)
    {
        if (rows != 1)
        {
            throw new InvalidOperationException("Expected one row.");
        }
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();
}
