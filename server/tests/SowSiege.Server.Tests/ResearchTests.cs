using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class ResearchTests
{
    private const string Field = "slg:research-field-yield";
    private const string Wood = "slg:research-wood-yield";
    private const string Training = "slg:research-training";

    private static ResearchContent LoadContent() => ResearchContent.Load(ContentEstateTests.Root());

    [Fact]
    public void DraftNodesLoadAsAcyclicLevelOneTreeWithSharedCaps()
    {
        var content = LoadContent();
        Assert.Equal(10, content.Nodes.Length);
        Assert.Equal(1, content.ActiveSlots);
        var field = content.Nodes.Single(n => n.Id == Field);
        Assert.Equal((60, 40, 0, 120L), (field.Cost["meta:grain"], field.Cost["meta:timber"], field.Cost["meta:iron"], field.Seconds));
        var all = content.Nodes.ToDictionary(n => n.Id, n => 1);
        var grain = ResearchRules.Effects(content, all).Single(e => e.Domain == "estate.grainProductionPercent");
        Assert.Equal((5, 20, 5), (grain.Raw, grain.Cap, grain.Applied));
        var tight = content with { Caps = new Dictionary<string, int>(content.Caps) { ["estate.grainProductionPercent"] = 3 } };
        Assert.Equal(3, ResearchRules.Effects(tight, all).Single(e => e.Domain == "estate.grainProductionPercent").Applied);
        Assert.Empty(ResearchRules.Effects(content, new Dictionary<string, int>()));
    }

    [Fact]
    public void CyclicOrUnknownPrerequisitesAreRejected()
    {
        string path = Path.Combine(ContentEstateTests.Root(), "data", RecruitPool.DraftPath);
        var original = JsonNode.Parse(File.ReadAllBytes(path))!;
        byte[] With(Action<JsonNode> edit)
        {
            var copy = original.DeepClone();
            edit(copy);
            return Encoding.UTF8.GetBytes(copy.ToJsonString());
        }
        JsonNode NodeOf(JsonNode root, string id) => root["researchNodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == id)!;
        Assert.Throws<InvalidDataException>(() => ResearchContent.Parse(With(r =>
            NodeOf(r, Field)["prerequisites"]!["nodes"]!.AsArray().Add(new JsonObject { ["id"] = Wood, ["level"] = 1 }))));
        Assert.Throws<InvalidDataException>(() => ResearchContent.Parse(With(r =>
            NodeOf(r, Field)["prerequisites"]!["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "slg:missing", ["level"] = 1 }))));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task StartChargesOnceAndCompletesOnServerTimeAcrossRestart()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.PrepareAsync(new() { ["meta:scriptorium"] = 1, ["m3a:field"] = 1 }, "slg:proof-field-boundary");
        var start = new ResearchCommand(Guid.NewGuid(), 0, "start-research", Field);
        string first = await fixture.Research.ExecuteAsync(fixture.Id, start, CancellationToken.None);
        var started = Parse(first);
        Assert.Equal((true, "started", 1L), (started.Done, started.Reason, started.Research.AccountVersion));
        Assert.Equal((60, 140, 60), (started.Research.Wallet["meta:grain"], started.Research.Wallet["meta:timber"], started.Research.Wallet["meta:iron"]));
        var job = Assert.Single(started.Research.Active);
        Assert.Equal(120L, job.RemainingSeconds);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(30);
        Assert.Equal(first, await fixture.Research.ExecuteAsync(fixture.Id, start, CancellationToken.None));
        Assert.Equal(-60L, await fixture.ScalarAsync("SELECT delta FROM resource_events WHERE account_id=$1 AND version=1 AND material='meta:grain'"));

        var early = Parse(await fixture.Research.ExecuteAsync(fixture.Id, new(Guid.NewGuid(), 1, "complete-research", null), CancellationToken.None));
        Assert.Equal((false, "not-finished", 1L), (early.Done, early.Reason, early.Research.AccountVersion));

        // A new store instance (server restart) keeps the original server completion time; no recharge.
        var restarted = new ResearchStore(fixture.Source, fixture.Content, fixture.Clock);
        await restarted.InitializeAsync(CancellationToken.None);
        var resumed = await restarted.ViewAsync(fixture.Id, CancellationToken.None);
        Assert.Equal(job.CompletesAt, Assert.Single(resumed.Active).CompletesAt);
        Assert.Equal(90L, resumed.Active[0].RemainingSeconds);
        Assert.Equal(60, resumed.Wallet["meta:grain"]);

        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(90);
        var completed = Parse(await restarted.ExecuteAsync(fixture.Id, new(Guid.NewGuid(), 1, "complete-research", null), CancellationToken.None));
        Assert.Equal((true, "completed", 2L), (completed.Done, completed.Reason, completed.Research.AccountVersion));
        Assert.Equal(new[] { Field }, completed.Research.Settled);
        Assert.Equal("completed", completed.Research.Nodes.Single(n => n.Id == Field).Status);
        Assert.Equal(5, completed.Research.Effects.Single(e => e.Domain == "estate.grainProductionPercent").Applied);

        // Duplicate completion: a second complete and a later view settle nothing and bump nothing.
        var again = Parse(await restarted.ExecuteAsync(fixture.Id, new(Guid.NewGuid(), 2, "complete-research", null), CancellationToken.None));
        Assert.Equal((false, "no-active-research", 2L), (again.Done, again.Reason, again.Research.AccountVersion));
        Assert.Empty((await restarted.ViewAsync(fixture.Id, CancellationToken.None)).Settled);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM research_levels WHERE account_id=$1"));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT version FROM accounts WHERE id=$1"));
        var repeat = Parse(await restarted.ExecuteAsync(fixture.Id, new(Guid.NewGuid(), 2, "start-research", Field), CancellationToken.None));
        Assert.Equal("already-completed", repeat.Reason);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task PrerequisitesResourcesAndSlotsAreEnforcedWithoutCharge()
    {
        await using var fixture = await Fixture.CreateAsync();
        async Task<ResearchReply> Start(string node) => Parse(await fixture.Research.ExecuteAsync(fixture.Id,
            new(Guid.NewGuid(), await fixture.ScalarAsync("SELECT version FROM accounts WHERE id=$1"), "start-research", node), CancellationToken.None));
        Assert.Contains("building:meta:scriptorium:1", (await Start(Field)).Reason);
        await fixture.PrepareAsync(new() { ["meta:scriptorium"] = 1, ["m3a:field"] = 1, ["m3a:lumber_camp"] = 1, ["meta:barracks"] = 1 });
        Assert.Equal("locked:proof:slg:proof-field-boundary", (await Start(Field)).Reason);
        await fixture.PrepareAsync(new(), "slg:proof-field-boundary", "slg:proof-wood-worksite", "slg:proof-outpost-defense");
        Assert.Equal("locked:node:" + Field + ":1", (await Start(Wood)).Reason);
        Assert.Equal("unknown-node", (await Start("slg:missing")).Reason);
        await fixture.PrepareAsync(new(), wallet: new() { ["meta:grain"] = 59 });
        var poor = await Start(Field);
        Assert.Equal(("insufficient-resources", 0L, 59), (poor.Reason, poor.Research.AccountVersion, poor.Research.Wallet["meta:grain"]));
        await fixture.PrepareAsync(new(), wallet: new() { ["meta:grain"] = 1000 });
        Assert.Equal("started", (await Start(Field)).Reason);
        var full = await Start(Training);
        Assert.Equal(("queue-full", 1L, 940), (full.Reason, full.Research.AccountVersion, full.Research.Wallet["meta:grain"]));
        Assert.Equal("already-active", (await Start(Field)).Reason);
        Assert.Equal(0L, await fixture.ScalarAsync("SELECT count(*) FROM resource_events WHERE account_id=$1 AND version<>1"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentStartsAndSettlementsApplyOnce()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.PrepareAsync(new() { ["meta:scriptorium"] = 1, ["m3a:field"] = 1, ["meta:barracks"] = 1 }, "slg:proof-field-boundary", "slg:proof-outpost-defense");
        async Task<string?> Attempt(ResearchCommand command)
        {
            try { return await fixture.Research.ExecuteAsync(fixture.Id, command, CancellationToken.None); }
            catch (CommandConflictException) { return null; }
        }
        var rivals = await Task.WhenAll(Attempt(new(Guid.NewGuid(), 0, "start-research", Field)), Attempt(new(Guid.NewGuid(), 0, "start-research", Training)));
        Assert.Single(rivals, r => r is not null);
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM research_jobs WHERE account_id=$1"));
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(301);
        var views = await Task.WhenAll(fixture.Research.ViewAsync(fixture.Id, CancellationToken.None), fixture.Research.ViewAsync(fixture.Id, CancellationToken.None));
        Assert.Equal(1, views.Sum(v => v.Settled.Length));
        Assert.Equal(1L, await fixture.ScalarAsync("SELECT count(*) FROM research_levels WHERE account_id=$1"));
        Assert.Equal(2L, await fixture.ScalarAsync("SELECT version FROM accounts WHERE id=$1"));
    }

    private static ResearchReply Parse(string json) => JsonSerializer.Deserialize<ResearchReply>(json, AccountStore.Json)!;

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture(NpgsqlDataSource source, ResearchStore research, ResearchContent content, ManualClock clock, Guid id) : IAsyncDisposable
    {
        public NpgsqlDataSource Source => source;
        public ResearchStore Research => research;
        public ResearchContent Content => content;
        public ManualClock Clock => clock;
        public Guid Id => id;

        public static async Task<Fixture> CreateAsync()
        {
            string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
            var source = NpgsqlDataSource.Create(connection);
            var clock = new ManualClock();
            var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
            await accounts.InitializeAsync(CancellationToken.None);
            var content = LoadContent();
            var research = new ResearchStore(source, content, clock);
            await research.InitializeAsync(CancellationToken.None);
            var created = await accounts.CreateAsync(CancellationToken.None);
            Guid id = (await accounts.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
            return new(source, research, content, clock, id);
        }

        /// <summary>Test-only setup: raise building levels, set wallet entries and grant proofs directly in storage.</summary>
        public async Task PrepareAsync(Dictionary<string, int> buildings, params string[] proofs) => await PrepareAsync(buildings, null, proofs);

        public async Task PrepareAsync(Dictionary<string, int> buildings, Dictionary<string, int>? wallet, params string[] proofs)
        {
            await using (var read = source.CreateCommand("SELECT payload FROM accounts WHERE id=$1"))
            {
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
                await write.ExecuteNonQueryAsync();
            }
            foreach (string proof in proofs)
            {
                await using var grant = source.CreateCommand("INSERT INTO research_proofs VALUES($1,$2,'test') ON CONFLICT DO NOTHING");
                grant.Parameters.AddWithValue(id); grant.Parameters.AddWithValue(proof);
                await grant.ExecuteNonQueryAsync();
            }
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
