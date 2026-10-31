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
    public void PrototypeGemGrantIsOffOutsideDevelopmentPrototype()
    {
        Assert.Equal(3000, RecruitDomain.PrototypeGems("Development", "true", "3000"));
        Assert.Equal(0, RecruitDomain.PrototypeGems("Production", "true", "3000"));
        Assert.Equal(0, RecruitDomain.PrototypeGems("Development", null, "3000"));
        Assert.Equal(0, RecruitDomain.PrototypeGems("Development", "true", null));
        var released = new RecruitDomain(LoadPool(), RecruitDomain.PrototypeGems("Production", "true", "3000"));
        Assert.Equal(0, released.InitialState.GetProperty("gems").GetInt32());
        Assert.Equal(new DomainResource(RecruitDomain.GemResource, 0), Assert.Single(released.Resources(released.InitialState)));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RecruitIsAtomicIdempotentLedgeredAndSurvivesRestart()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        var view = await fixture.ViewAsync();
        var odds = view.Domains[RecruitDomain.Name];
        Assert.Equal((3000, 0, 20), (odds.GetProperty("gems").GetInt32(), odds.GetProperty("pity").GetInt32(), odds.GetProperty("drawsUntilGuarantee").GetInt32()));
        Assert.Equal(3000, view.Resources.Single(r => r.Id == RecruitDomain.GemResource).Amount);
        var command = fixture.Recruit(view.Estate.Version);
        string first = await fixture.Store.ExecuteAccountAsync(fixture.Id, command, CancellationToken.None);
        var reply = SlgFixture.Parse(first);
        var recruit = reply.Domains[RecruitDomain.Name];
        Assert.True(reply.Done);
        Assert.Equal(2900, reply.Resources.Single(r => r.Id == RecruitDomain.GemResource).Amount);
        Assert.Equal("unlock", recruit.GetProperty("last").GetProperty("outcome").GetString());
        string hero = recruit.GetProperty("last").GetProperty("heroId").GetString()!;
        Assert.Equal(first, await fixture.Store.ExecuteAccountAsync(fixture.Id, command, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => fixture.Store.ExecuteAccountAsync(fixture.Id,
            command with { Payload = JsonSerializer.SerializeToElement(new { poolVersion = "other" }) }, CancellationToken.None));
        await Assert.ThrowsAsync<CommandConflictException>(() => fixture.Store.ExecuteAccountAsync(fixture.Id, command with { CommandId = Guid.NewGuid() }, CancellationToken.None));
        // The ledger stores pool version, policy hash and a seed the server key reproduces.
        var ledger = Assert.Single(await fixture.EventsAsync("recruited"));
        var replay = RecruitDraw.Draw(fixture.Pool, Key, fixture.Id, 0, 0);
        Assert.Equal((fixture.Pool.PoolVersion, fixture.Pool.PolicyHash, replay.SeedHex, replay.Entry.HeroId, hero, 3000, 2900),
            (ledger.GetProperty("poolVersion").GetString(), ledger.GetProperty("policyHash").GetString(), ledger.GetProperty("seed").GetString(),
             ledger.GetProperty("heroId").GetString(), hero, ledger.GetProperty("gemsBefore").GetInt32(), ledger.GetProperty("gemsAfter").GetInt32()));
        // A new store (server restart) restores counter, gems and roster.
        var restarted = await SlgFixture.StoreAsync(fixture.Source, Key, fixture.Clock);
        var after = SlgFixture.Parse(await restarted.ExecuteAccountAsync(fixture.Id, null, CancellationToken.None)).Domains[RecruitDomain.Name];
        Assert.Equal((2900, recruit.GetProperty("pity").GetInt32(), 0), (after.GetProperty("gems").GetInt32(), after.GetProperty("pity").GetInt32(), after.GetProperty("heroes").GetProperty(hero).GetInt32()));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task GuaranteeResetsCounterAndDuplicatesBecomeShards()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        var heroes = fixture.Pool.Entries.ToDictionary(e => e.HeroId, _ => 0);
        await fixture.SetDomainAsync(RecruitDomain.Name, new { gems = 3000, pity = 19, draws = 0, poolFamily = fixture.Pool.PoolFamilyId, poolVersion = fixture.Pool.PoolVersion, heroes });
        long version = (await fixture.ViewAsync()).Estate.Version;
        var guaranteed = SlgFixture.Parse(await fixture.Store.ExecuteAccountAsync(fixture.Id, fixture.Recruit(version++), CancellationToken.None)).Domains[RecruitDomain.Name].GetProperty("last");
        Assert.True(guaranteed.GetProperty("guaranteed").GetBoolean());
        Assert.Equal(("epic", 19, 0, "shards", 20), (guaranteed.GetProperty("rarity").GetString(), guaranteed.GetProperty("pityBefore").GetInt32(),
            guaranteed.GetProperty("pityAfter").GetInt32(), guaranteed.GetProperty("outcome").GetString(), guaranteed.GetProperty("shards").GetInt32()));
        for (int i = 0; i < 25; i++)
        {
            var last = SlgFixture.Parse(await fixture.Store.ExecuteAccountAsync(fixture.Id, fixture.Recruit(version++), CancellationToken.None)).Domains[RecruitDomain.Name].GetProperty("last");
            string rarity = last.GetProperty("rarity").GetString()!;
            Assert.Equal("shards", last.GetProperty("outcome").GetString());
            Assert.Equal(fixture.Pool.DuplicateShards[rarity], last.GetProperty("shards").GetInt32());
            Assert.Equal(rarity == "epic" ? 0 : last.GetProperty("pityBefore").GetInt32() + 1, last.GetProperty("pityAfter").GetInt32());
        }
        Assert.Equal(3000 - 26 * 100, (await fixture.ViewAsync()).Domains[RecruitDomain.Name].GetProperty("gems").GetInt32());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RejectionsChangeNothingAndReplay()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        await fixture.SetDomainAsync(RecruitDomain.Name, new { gems = 99, pity = 7, draws = 0, poolFamily = fixture.Pool.PoolFamilyId, poolVersion = fixture.Pool.PoolVersion, heroes = new { } });
        long version = (await fixture.ViewAsync()).Estate.Version;
        var poor = fixture.Recruit(version);
        string first = await fixture.Store.ExecuteAccountAsync(fixture.Id, poor, CancellationToken.None);
        var rejected = SlgFixture.Parse(first);
        Assert.Equal((false, "insufficient-gems"), (rejected.Done, rejected.Reason));
        Assert.Equal((99, 7), (rejected.Domains[RecruitDomain.Name].GetProperty("gems").GetInt32(), rejected.Domains[RecruitDomain.Name].GetProperty("pity").GetInt32()));
        Assert.Equal(first, await fixture.Store.ExecuteAccountAsync(fixture.Id, poor, CancellationToken.None));
        var stale = new AccountCommand(Guid.NewGuid(), version + 1, RecruitDomain.Name, "recruit", JsonSerializer.SerializeToElement(new { poolVersion = "slg:standard-v0" }));
        Assert.Equal("pool-changed", SlgFixture.Parse(await fixture.Store.ExecuteAccountAsync(fixture.Id, stale, CancellationToken.None)).Reason);
        Assert.Empty(await fixture.EventsAsync("recruited"));
        var forged = new AccountCommand(Guid.NewGuid(), version + 2, RecruitDomain.Name, "recruit", JsonSerializer.SerializeToElement(new { poolVersion = fixture.Pool.PoolVersion, heroId = "slg:ruin_seeker" }));
        await Assert.ThrowsAsync<JsonException>(() => fixture.Store.ExecuteAccountAsync(fixture.Id, forged, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentRequestsChargeOnce()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        long version = (await fixture.ViewAsync()).Estate.Version;
        async Task<string?> Attempt(AccountCommand command)
        {
            try { return await fixture.Store.ExecuteAccountAsync(fixture.Id, command, CancellationToken.None); }
            catch (CommandConflictException) { return null; }
        }
        var a = fixture.Recruit(version);
        var rivals = await Task.WhenAll(Attempt(a), Attempt(a with { CommandId = Guid.NewGuid() }));
        Assert.Single(rivals, r => r is not null);
        var same = fixture.Recruit(version + 1);
        var duplicates = await Task.WhenAll(Attempt(same), Attempt(same));
        Assert.NotNull(duplicates[0]);
        Assert.Equal(duplicates[0], duplicates[1]);
        Assert.Equal(2, (await fixture.EventsAsync("recruited")).Length);
        Assert.Equal(2800, (await fixture.ViewAsync()).Domains[RecruitDomain.Name].GetProperty("gems").GetInt32());
    }
}
