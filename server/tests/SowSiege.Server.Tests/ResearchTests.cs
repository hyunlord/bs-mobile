using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class ResearchTests
{
    private const string Field = "slg:research-field-yield";
    private const string Wood = "slg:research-wood-yield";
    private const string Training = "slg:research-training";
    private static readonly byte[] Key = Convert.FromHexString(RecruitTests.TestKeyHex);

    private static ResearchContent LoadContent() => ResearchContent.Load(ContentEstateTests.Root());

    private static AccountCommand Command(long version, string operation, string node) =>
        new(Guid.NewGuid(), version, ResearchDomain.Name, operation, JsonSerializer.SerializeToElement(new { nodeId = node }));

    private static object State(string[] proofs) => new { nextSeq = 1, active = Array.Empty<object>(), levels = new { }, completedAtMs = new { }, proofs };

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
        await using var fixture = await SlgFixture.CreateAsync(Key);
        await fixture.SetDomainAsync(ResearchDomain.Name, State(["slg:proof-field-boundary"]));
        await fixture.SetEstateAsync(new() { ["meta:scriptorium"] = 1, ["m3a:field"] = 1 });
        long version = (await fixture.ViewAsync()).Estate.Version;
        var start = Command(version, "start-research", Field);
        string first = await fixture.Store.ExecuteAccountAsync(fixture.Id, start, CancellationToken.None);
        var started = SlgFixture.Parse(first);
        Assert.Equal((true, "started"), (started.Done, started.Reason));
        Assert.Equal((60, 140, 60), (Amount(started, "meta:grain"), Amount(started, "meta:timber"), Amount(started, "meta:iron")));
        var job = started.Domains[ResearchDomain.Name].GetProperty("active")[0];
        Assert.Equal(120, job.GetProperty("remainingSeconds").GetInt64());
        long completesAt = job.GetProperty("completesAtMs").GetInt64();
        Assert.Equal(fixture.Clock.Now.AddSeconds(120).ToUnixTimeMilliseconds(), completesAt);

        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(30);
        Assert.Equal(first, await fixture.Store.ExecuteAccountAsync(fixture.Id, start, CancellationToken.None));
        var early = SlgFixture.Parse(await fixture.Store.ExecuteAccountAsync(fixture.Id, Command(version + 1, "complete-research", Field), CancellationToken.None));
        Assert.Equal((false, "not-finished"), (early.Done, early.Reason));

        // A server clock that steps backwards does not move the job; restart keeps the original deadline and charge.
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(-600);
        var restarted = await SlgFixture.StoreAsync(fixture.Source, Key, fixture.Clock);
        var resumed = SlgFixture.Parse(await restarted.ExecuteAccountAsync(fixture.Id, null, CancellationToken.None));
        var resumedJob = resumed.Domains[ResearchDomain.Name].GetProperty("active")[0];
        Assert.Equal(completesAt, resumedJob.GetProperty("completesAtMs").GetInt64());
        Assert.Equal(90, resumedJob.GetProperty("remainingSeconds").GetInt64());
        Assert.Equal(60, Amount(resumed, "meta:grain"));

        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(600 + 90);
        var done = SlgFixture.Parse(await restarted.ExecuteAccountAsync(fixture.Id, Command(resumed.Estate.Version, "complete-research", Field), CancellationToken.None));
        Assert.Equal((true, "completed"), (done.Done, done.Reason));
        var research = done.Domains[ResearchDomain.Name];
        Assert.Equal(0, research.GetProperty("active").GetArrayLength());
        Assert.Contains(research.GetProperty("effects").EnumerateArray(), e => e.GetProperty("domain").GetString() == "estate.grainProductionPercent" && e.GetProperty("applied").GetInt32() == 5);

        // Repeated completion and later reads never settle again.
        var again = SlgFixture.Parse(await restarted.ExecuteAccountAsync(fixture.Id, Command(done.Estate.Version, "complete-research", Field), CancellationToken.None));
        Assert.Equal("completed", again.Reason);
        await fixture.ViewAsync();
        Assert.Single(await fixture.EventsAsync("research-completed"));
        Assert.Single(await fixture.EventsAsync("research-started"));
        Assert.Equal(60, Amount(await fixture.ViewAsync(), "meta:grain"));
        var repeat = SlgFixture.Parse(await restarted.ExecuteAccountAsync(fixture.Id, Command(again.Estate.Version + 2, "start-research", Field), CancellationToken.None));
        Assert.Equal("already-completed", repeat.Reason);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task PrerequisitesResourcesAndSlotsAreEnforcedWithoutCharge()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        async Task<AccountReply> Start(string node) => SlgFixture.Parse(await fixture.Store.ExecuteAccountAsync(fixture.Id,
            Command((await fixture.ViewAsync()).Estate.Version, "start-research", node), CancellationToken.None));
        Assert.Contains("building:meta:scriptorium:1", (await Start(Field)).Reason);
        await fixture.SetEstateAsync(new() { ["meta:scriptorium"] = 1, ["m3a:field"] = 1, ["m3a:lumber_camp"] = 1, ["meta:barracks"] = 1 });
        Assert.Equal("locked:proof:slg:proof-field-boundary", (await Start(Field)).Reason);
        await fixture.SetDomainAsync(ResearchDomain.Name, State(["slg:proof-field-boundary", "slg:proof-wood-worksite", "slg:proof-outpost-defense"]));
        Assert.Equal("locked:node:" + Field + ":1", (await Start(Wood)).Reason);
        Assert.Equal("unknown-node", (await Start("slg:missing")).Reason);
        await fixture.SetEstateAsync(new(), new() { ["meta:grain"] = 59 });
        var poor = await Start(Field);
        Assert.Equal(("insufficient-resources", 59), (poor.Reason, Amount(poor, "meta:grain")));
        await fixture.SetEstateAsync(new(), new() { ["meta:grain"] = 1000 });
        Assert.Equal("started", (await Start(Field)).Reason);
        var full = await Start(Training);
        Assert.Equal(("queue-full", 940), (full.Reason, Amount(full, "meta:grain")));
        Assert.Equal("already-active", (await Start(Field)).Reason);
        Assert.Single(await fixture.EventsAsync("research-started"));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentStartsAndSettlementsApplyOnce()
    {
        await using var fixture = await SlgFixture.CreateAsync(Key);
        await fixture.SetDomainAsync(ResearchDomain.Name, State(["slg:proof-field-boundary", "slg:proof-outpost-defense"]));
        await fixture.SetEstateAsync(new() { ["meta:scriptorium"] = 1, ["m3a:field"] = 1, ["meta:barracks"] = 1 });
        long version = (await fixture.ViewAsync()).Estate.Version;
        async Task<string?> Attempt(AccountCommand command)
        {
            try { return await fixture.Store.ExecuteAccountAsync(fixture.Id, command, CancellationToken.None); }
            catch (CommandConflictException) { return null; }
        }
        var rivals = await Task.WhenAll(Attempt(Command(version, "start-research", Field)), Attempt(Command(version, "start-research", Training)));
        Assert.Single(rivals, r => r is not null);
        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(301);
        await Task.WhenAll(fixture.ViewAsync(), fixture.ViewAsync());
        Assert.Single(await fixture.EventsAsync("research-completed"));
        Assert.Single(await fixture.EventsAsync("research-started"));
    }

    private static int Amount(AccountReply reply, string id) => reply.Resources.Single(r => r.Id == id).Amount;
}
