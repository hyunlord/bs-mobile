using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class RecruitTests
{
    internal static readonly string TestKeyHex = string.Concat(Enumerable.Repeat("5d1f0c3a", 8));
    private static readonly byte[] Key = Convert.FromHexString(TestKeyHex);

    private static RecruitPool LoadPool() => RecruitPool.Load(ContentEstateTests.Root());

    [Fact]
    public void PublishedOddsMatchGddAndGuaranteeOnlyAtNineteenMisses()
    {
        var pool = LoadPool();
        Assert.Equal(64, pool.PolicyHash.Length);
        foreach (int pity in new[] { 0, 18 })
        {
            var odds = pool.Odds(pity);
            Assert.Equal(1.0, odds.Sum(o => o.Probability), 12);
            Assert.Equal(new[] { 0.70, 0.25, 0.05 }, odds.Select(o => Math.Round(o.Probability, 12)));
        }
        var guaranteed = pool.Odds(19);
        Assert.Equal(new[] { 0.0, 0.0, 1.0 }, guaranteed.Select(o => o.Probability));
        Assert.DoesNotContain(pool.Entries, e => e.HeroId == "core:frontier_knight");
    }

    [Fact]
    public void RollBoundariesAndCounterTransitions()
    {
        var pool = LoadPool();
        Assert.Equal("common", RecruitDraw.Pick(pool.Entries, 0).Rarity);
        Assert.Equal("common", RecruitDraw.Pick(pool.Entries, 6999).Rarity);
        Assert.Equal("rare", RecruitDraw.Pick(pool.Entries, 7000).Rarity);
        Assert.Equal("rare", RecruitDraw.Pick(pool.Entries, 9499).Rarity);
        Assert.Equal("epic", RecruitDraw.Pick(pool.Entries, 9500).Rarity);
        Assert.Equal("epic", RecruitDraw.Pick(pool.Entries, 9999).Rarity);
        var account = Guid.Parse("6a2f6f1e-1111-4a4a-8b8b-000000000001");
        // c=19 always yields epic and resets to 0, including a duplicate epic.
        for (long index = 0; index < 200; index++)
        {
            var forced = RecruitDraw.Draw(pool, Key, account, index, 19);
            Assert.True(forced.Guaranteed);
            Assert.Equal("epic", forced.Entry.Rarity);
            Assert.Equal(0, forced.PityAfter);
        }
        // Normal epic resets to 0; non-epic increments; c=18 is still a normal draw.
        bool sawNormalEpic = false, sawMiss = false;
        for (long index = 0; index < 2000 && !(sawNormalEpic && sawMiss); index++)
        {
            var draw = RecruitDraw.Draw(pool, Key, account, index, 18);
            Assert.False(draw.Guaranteed);
            if (draw.Entry.Rarity == "epic") { Assert.Equal(0, draw.PityAfter); sawNormalEpic = true; }
            else { Assert.Equal(19, draw.PityAfter); sawMiss = true; }
        }
        Assert.True(sawNormalEpic && sawMiss);
    }

    [Fact]
    public void DrawIsReproducibleFromKeyAccountIndexAndPoolVersion()
    {
        var pool = LoadPool();
        var account = Guid.NewGuid();
        var first = RecruitDraw.Draw(pool, Key, account, 7, 3);
        Assert.Equal(first, RecruitDraw.Draw(pool, Key, account, 7, 3));
        Assert.NotEqual(first.SeedHex, RecruitDraw.Draw(pool, Key, account, 8, 3).SeedHex);
        Assert.NotEqual(first.SeedHex, RecruitDraw.Draw(pool, Key, Guid.NewGuid(), 7, 3).SeedHex);
        var otherKey = (byte[])Key.Clone();
        otherKey[0] ^= 1;
        Assert.NotEqual(first.SeedHex, RecruitDraw.Draw(pool, otherKey, account, 7, 3).SeedHex);
    }

    [Fact]
    public void HundredThousandDrawsMatchPublishedOdds()
    {
        var pool = LoadPool();
        const int draws = 100_000;
        var account = Guid.Parse("0f0e0d0c-0b0a-4908-8706-050403020100");
        var normal = new Dictionary<string, long> { ["common"] = 0, ["rare"] = 0, ["epic"] = 0 };
        long guaranteed = 0, epics = 0;
        int pity = 0, maxGap = 0, gap = 0;
        for (long index = 0; index < draws; index++)
        {
            var draw = RecruitDraw.Draw(pool, Key, account, index, pity);
            gap++;
            if (draw.Guaranteed) { guaranteed++; Assert.Equal("epic", draw.Entry.Rarity); }
            else { normal[draw.Entry.Rarity]++; }
            if (draw.Entry.Rarity == "epic") { epics++; maxGap = Math.Max(maxGap, gap); gap = 0; }
            pity = draw.PityAfter;
        }
        Assert.True(maxGap <= 20, $"epic gap {maxGap}");
        // Normal draws must follow the published 70/25/5. Chi-square, df=2, critical 13.82 at alpha=0.001.
        long n = normal.Values.Sum();
        double chi = new[] { ("common", 0.70), ("rare", 0.25), ("epic", 0.05) }
            .Sum(x => Math.Pow(normal[x.Item1] - n * x.Item2, 2) / (n * x.Item2));
        Assert.True(chi < 13.82, $"chi-square {chi:F3} normal={string.Join(',', normal)}");
        // With the 19-miss guarantee, epic share is 1/E[draws per epic] = 0.05/(1-0.95^20).
        double expected = 0.05 / (1 - Math.Pow(0.95, 20));
        double share = (double)epics / draws, sigma = Math.Sqrt(expected * (1 - expected) / draws);
        Assert.True(Math.Abs(share - expected) < 4 * sigma, $"epic share {share:F5} vs {expected:F5}");
        Assert.True(guaranteed > 0);
    }

    [Fact]
    public void InvalidPoolsAreRejected()
    {
        string data = Path.Combine(ContentEstateTests.Root(), "data");
        byte[] draft = File.ReadAllBytes(Path.Combine(data, RecruitPool.DraftPath));
        var original = JsonNode.Parse(File.ReadAllBytes(Path.Combine(data, RecruitPool.PoolPath)))!;
        byte[] With(Action<JsonNode> edit)
        {
            var copy = original.DeepClone();
            edit(copy);
            return Encoding.UTF8.GetBytes(copy.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(() => RecruitPool.Parse(With(p => p["entries"]![0]!["weight"] = 6999), draft));
        Assert.Throws<InvalidDataException>(() => RecruitPool.Parse(With(p => p["entries"]![0]!["rarity"] = "rare"), draft));
        Assert.Throws<InvalidDataException>(() => RecruitPool.Parse(With(p => p["entries"]![0]!["heroId"] = "core:frontier_knight"), draft));
        Assert.Throws<InvalidDataException>(() => RecruitPool.Parse(With(p => p["duplicateShards"]!.AsObject().Remove("epic")), draft));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RecruitIsAtomicIdempotentLedgeredAndSurvivesRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var odds = await fixture.Recruit.OddsAsync(fixture.Id, CancellationToken.None);
        Assert.Equal((3000L, 0, 20), (odds.Gems, odds.Pity, odds.DrawsUntilGuarantee));
        var command = new RecruitCommand(Guid.NewGuid(), odds.AccountVersion, odds.PoolVersion);
        string first = await fixture.Recruit.RecruitAsync(fixture.Id, command, CancellationToken.None);
        var reply = Parse(first);
        Assert.True(reply.Done);
        Assert.Equal((2900L, 1L), (reply.Gems, reply.AccountVersion));
        Assert.Equal("unlock", reply.Result!.Outcome);
        Assert.Equal(reply.Result.HeroId, Assert.Single(reply.Heroes).HeroId);
        Assert.Equal(first, await fixture.Recruit.RecruitAsync(fixture.Id, command, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => fixture.Recruit.RecruitAsync(fixture.Id, command with { PoolVersion = "other" }, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => fixture.Recruit.RecruitAsync(fixture.Id, command with { CommandId = Guid.NewGuid() }, CancellationToken.None));
        // The ledger records pool version, policy hash and a seed that the server key reproduces.
        await using (var ledger = fixture.Source.CreateCommand("SELECT draw_index,pool_version,policy_hash,seed_hex,hero_id,gems_before,gems_after FROM recruit_ledger WHERE account_id=$1"))
        {
            ledger.Parameters.AddWithValue(fixture.Id);
            await using var reader = await ledger.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            var replay = RecruitDraw.Draw(fixture.Pool, Key, fixture.Id, reader.GetInt64(0), 0);
            Assert.Equal((fixture.Pool.PoolVersion, fixture.Pool.PolicyHash, replay.SeedHex, replay.Entry.HeroId, 3000L, 2900L),
                (reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt64(5), reader.GetInt64(6)));
            Assert.False(await reader.ReadAsync());
        }
        // Recruit and estate commands share the account version envelope.
        string estate = await fixture.Accounts.ExecuteAsync(fixture.Id, new EstateCommand(Guid.NewGuid(), 1, "claim-production", null), CancellationToken.None);
        Assert.Equal(2, JsonSerializer.Deserialize<CommandReply>(estate, AccountStore.Json)!.Estate.Version);
        var restarted = new RecruitStore(fixture.Source, fixture.Pool, new RecruitOptions(Key));
        await restarted.InitializeAsync(CancellationToken.None);
        var after = await restarted.OddsAsync(fixture.Id, CancellationToken.None);
        Assert.Equal((2900L, reply.Pity, 2L), (after.Gems, after.Pity, after.AccountVersion));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task GuaranteeResetsCounterAndDuplicatesBecomeShards()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Recruit.OddsAsync(fixture.Id, CancellationToken.None);
        await fixture.SqlAsync("UPDATE hero_accounts SET pity=19 WHERE account_id=$1");
        foreach (var entry in fixture.Pool.Entries)
        {
            await fixture.SqlAsync("INSERT INTO hero_roster VALUES($1,'" + entry.HeroId + "',0)");
        }
        var guaranteed = Parse(await fixture.Recruit.RecruitAsync(fixture.Id, new(Guid.NewGuid(), 0, fixture.Pool.PoolVersion), CancellationToken.None));
        Assert.True(guaranteed.Result!.Guaranteed);
        Assert.Equal(("epic", 19, 0, "shards", 20), (guaranteed.Result.Rarity, guaranteed.Result.PityBefore, guaranteed.Result.PityAfter, guaranteed.Result.Outcome, guaranteed.Result.Shards));
        Assert.Equal(20, guaranteed.Heroes.Single(h => h.HeroId == "slg:ruin_seeker").Shards);
        long version = 1;
        for (int i = 0; i < 25; i++)
        {
            var next = Parse(await fixture.Recruit.RecruitAsync(fixture.Id, new(Guid.NewGuid(), version++, fixture.Pool.PoolVersion), CancellationToken.None));
            Assert.Equal("shards", next.Result!.Outcome);
            Assert.Equal(fixture.Pool.DuplicateShards[next.Result.Rarity], next.Result.Shards);
            Assert.Equal(next.Result.Rarity == "epic" ? 0 : next.Result.PityBefore + 1, next.Result.PityAfter);
        }
        Assert.Equal(3000L - 26 * 100, (await fixture.Recruit.OddsAsync(fixture.Id, CancellationToken.None)).Gems);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RejectionsChangeNothingAndReplay()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Recruit.OddsAsync(fixture.Id, CancellationToken.None);
        await fixture.SqlAsync("UPDATE hero_accounts SET gems=99,pity=7 WHERE account_id=$1");
        var poor = new RecruitCommand(Guid.NewGuid(), 0, fixture.Pool.PoolVersion);
        string first = await fixture.Recruit.RecruitAsync(fixture.Id, poor, CancellationToken.None);
        var rejected = Parse(first);
        Assert.Equal((false, "insufficient-gems", 99L, 7, 0L), (rejected.Done, rejected.Reason, rejected.Gems, rejected.Pity, rejected.AccountVersion));
        Assert.Empty(rejected.Heroes);
        Assert.Equal(first, await fixture.Recruit.RecruitAsync(fixture.Id, poor, CancellationToken.None));
        await fixture.SqlAsync("UPDATE hero_accounts SET gems=1000 WHERE account_id=$1");
        var stale = Parse(await fixture.Recruit.RecruitAsync(fixture.Id, new(Guid.NewGuid(), 0, "slg:standard-v0"), CancellationToken.None));
        Assert.Equal((false, "pool-changed", 1000L, 7), (stale.Done, stale.Reason, stale.Gems, stale.Pity));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM recruit_ledger WHERE account_id=$1"));
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT version FROM accounts WHERE id=$1"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentRequestsChargeOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Recruit.OddsAsync(fixture.Id, CancellationToken.None);
        async Task<string?> Attempt(RecruitCommand command)
        {
            try { return await fixture.Recruit.RecruitAsync(fixture.Id, command, CancellationToken.None); }
            catch (CommandConflictException) { return null; }
        }
        var a = new RecruitCommand(Guid.NewGuid(), 0, fixture.Pool.PoolVersion);
        var rivals = await Task.WhenAll(Attempt(a), Attempt(a with { CommandId = Guid.NewGuid() }));
        Assert.Single(rivals, r => r is not null);
        var same = new RecruitCommand(Guid.NewGuid(), 1, fixture.Pool.PoolVersion);
        var duplicates = await Task.WhenAll(Attempt(same), Attempt(same));
        Assert.NotNull(duplicates[0]);
        Assert.Equal(duplicates[0], duplicates[1]);
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT count(*) FROM recruit_ledger WHERE account_id=$1"));
        Assert.Equal(2800L, await fixture.ScalarAsync("SELECT gems FROM hero_accounts WHERE account_id=$1"));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT version FROM accounts WHERE id=$1"));
    }

    private static RecruitReply Parse(string json) => JsonSerializer.Deserialize<RecruitReply>(json, AccountStore.Json)!;

    private sealed class Fixture(NpgsqlDataSource source, AccountStore accounts, RecruitStore recruit, RecruitPool pool, Guid id) : IAsyncDisposable
    {
        public NpgsqlDataSource Source => source;
        public AccountStore Accounts => accounts;
        public RecruitStore Recruit => recruit;
        public RecruitPool Pool => pool;
        public Guid Id => id;

        public static async Task<Fixture> CreateAsync()
        {
            string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
            var source = NpgsqlDataSource.Create(connection);
            var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), TimeProvider.System);
            await accounts.InitializeAsync(CancellationToken.None);
            var pool = LoadPool();
            var recruit = new RecruitStore(source, pool, new RecruitOptions(Key));
            await recruit.InitializeAsync(CancellationToken.None);
            var created = await accounts.CreateAsync(CancellationToken.None);
            Guid id = (await accounts.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
            return new(source, accounts, recruit, pool, id);
        }

        public async Task SqlAsync(string sql)
        {
            await using var command = source.CreateCommand(sql);
            command.Parameters.AddWithValue(id);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<long> ScalarAsync(string sql)
        {
            await using var command = source.CreateCommand(sql);
            command.Parameters.AddWithValue(id);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public ValueTask DisposeAsync() => source.DisposeAsync();
    }
}
