using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class AllianceTests
{
    private static readonly AllianceContent Content = AllianceContent.Load(ContentEstateTests.Root());

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class World(NpgsqlDataSource source, AccountStore accounts, AllianceStore alliances, ManualClock clock) : IAsyncDisposable
    {
        public NpgsqlDataSource Source => source;
        public AllianceStore Alliances => alliances;
        public ManualClock Clock => clock;

        public static async Task<World> CreateAsync(AllianceContent? content = null)
        {
            var source = NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required."));
            var clock = new ManualClock();
            var accounts = new AccountStore(source, new EstateApplication(EstateContent.Load(ContentEstateTests.Root())), clock);
            await accounts.InitializeAsync(CancellationToken.None);
            var alliances = new AllianceStore(source, content ?? Content, clock);
            await alliances.InitializeAsync(CancellationToken.None);
            return new(source, accounts, alliances, clock);
        }

        public async Task<Guid> AccountAsync()
        {
            var created = await accounts.CreateAsync(CancellationToken.None);
            return (await accounts.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        }

        public async Task<long> VersionAsync(Guid id)
        {
            await using var query = source.CreateCommand("SELECT version FROM accounts WHERE id=$1");
            query.Parameters.AddWithValue(id);
            return (long)(await query.ExecuteScalarAsync())!;
        }

        public async Task<AllianceReply> RunAsync(Guid id, string operation, object payload, Guid? commandId = null)
        {
            var command = new AccountCommand(commandId ?? Guid.NewGuid(), await VersionAsync(id), AllianceStore.Domain, operation, JsonSerializer.SerializeToElement(payload));
            return Parse(await alliances.ExecuteAsync(id, command, CancellationToken.None));
        }

        /// <summary>Retries version conflicts, as a client would, so concurrency tests exercise alliance-level races.</summary>
        public async Task<AllianceReply?> TryAsync(Guid id, string operation, object payload)
        {
            for (int i = 0; i < 200; i++)
            {
                try { return await RunAsync(id, operation, payload); }
                catch (CommandConflictException conflict) when (conflict.Message is "version-conflict" or "state-changed") { await Task.Delay(Random.Shared.Next(1, 10)); }
            }
            return null;
        }

        public async Task<Guid> AllianceAsync(Guid leader, string mode = "open")
        {
            var reply = await RunAsync(leader, "create", new { name = "Field Guard", mode });
            Assert.Equal("created", reply.Reason);
            return reply.CreatedId!.Value;
        }

        /// <summary>Test setup: a research job in the account's stored domain state.</summary>
        public async Task ResearchJobAsync(Guid id, long seq, long startedAtMs, long completesAtMs)
        {
            var state = JsonSerializer.Serialize(new
            {
                nextSeq = seq + 1,
                active = new[] { new { seq, nodeId = "slg:research-field-yield", level = 1, startedAtMs, completesAtMs } },
                levels = new { }, completedAtMs = new { }, proofs = Array.Empty<string>(),
            }, AccountStore.Json);
            await using var write = source.CreateCommand("INSERT INTO account_domains(account_id,domain,state_version,content_hash,state_json) VALUES($1,'research',1,'test',$2) ON CONFLICT(account_id,domain) DO UPDATE SET state_json=$2");
            write.Parameters.AddWithValue(id); write.Parameters.AddWithValue(NpgsqlDbType.Jsonb, state);
            await write.ExecuteNonQueryAsync();
        }

        public long NowMs => clock.Now.ToUnixTimeMilliseconds();

        public ValueTask DisposeAsync() => source.DisposeAsync();
    }

    private static AllianceReply Parse(string json) => JsonSerializer.Deserialize<AllianceReply>(json, AccountStore.Json)!;

    [Fact]
    public void TextIsNormalisedCountedAndStripped()
    {
        Assert.Equal("필드가드", AllianceRules.Clean("  필드가드 ", 2, 12));
        Assert.Null(AllianceRules.Clean("a", 2, 12));
        Assert.Null(AllianceRules.Clean("<b>x</b>", 2, 12));
        Assert.Null(AllianceRules.Clean("ab\u0007", 2, 12));
        Assert.Null(AllianceRules.Clean("   ", 2, 12));
        Assert.Equal("", AllianceRules.Clean("", 0, 120, allowEmpty: true));
        Assert.Equal(new string('가', 200), AllianceRules.Clean(new string('가', 200), 1, 200));
        Assert.Null(AllianceRules.Clean(new string('가', 201), 1, 200));
    }

    [Theory]
    [InlineData(200_000, 200_000, 0, 4_000)]   // GDD19: D0=200 s -> 4 s per grant
    [InlineData(200_000, 200_000, 16_000, 4_000)]
    [InlineData(200_000, 200_000, 18_000, 2_000)]  // total cap floor(D0*10%) = 20 s
    [InlineData(200_000, 200_000, 20_000, 0)]
    [InlineData(200_000, 2_500, 0, 2_000)]     // never more than the whole remaining seconds
    [InlineData(10_000_000, 10_000_000, 0, 30_000)] // 30 s per-grant cap
    [InlineData(40_000, 40_000, 0, 0)]         // floor(40 s * 2%) = 0: refused, consumes nothing
    public void HelpShorteningFollowsGddFormula(long d0, long remaining, long given, long expected) =>
        Assert.Equal(expected, AllianceRules.HelpShortening(Content.Help, d0, remaining, given));

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task LifecycleApplicationsRestrictionsAndPermissions()
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), applicant = await world.AccountAsync(), other = await world.AccountAsync();
        var alliance = await world.AllianceAsync(leader, "approval");
        var second = await world.AllianceAsync(other, "approval");
        Assert.Equal("applied", (await world.RunAsync(applicant, "apply", new { allianceId = alliance })).Reason);
        Assert.Equal("application-pending", (await world.RunAsync(applicant, "apply", new { allianceId = second })).Reason);
        Assert.Equal("not-permitted", (await world.RunAsync(applicant, "notice", new { text = "hi" })).Reason);
        Assert.Equal("approved", (await world.RunAsync(leader, "approve", new { accountId = applicant })).Reason);
        var view = await world.Alliances.ViewAsync(applicant, CancellationToken.None);
        Assert.Equal(("member", 2), (view.Role, view.Alliance!.MemberCount));
        Assert.Equal("not-permitted", (await world.RunAsync(applicant, "notice", new { text = "hi" })).Reason);
        Assert.Equal("invalid-text", (await world.RunAsync(leader, "notice", new { text = new string('x', 121) })).Reason);
        Assert.Equal("notice-set", (await world.RunAsync(leader, "notice", new { text = "Mind the field mites" })).Reason);
        Assert.Equal("leader-must-delegate", (await world.RunAsync(leader, "leave", new { })).Reason);
        Assert.Equal("left", (await world.RunAsync(applicant, "leave", new { })).Reason);
        Assert.Equal("rejoin-restricted", (await world.RunAsync(applicant, "apply", new { allianceId = alliance })).Reason);
        world.Clock.Now = world.Clock.Now.AddHours(12).AddSeconds(1);
        Assert.Equal("applied", (await world.RunAsync(applicant, "apply", new { allianceId = alliance })).Reason);
        // Applications expire after 24 h and can no longer be approved.
        world.Clock.Now = world.Clock.Now.AddHours(24).AddSeconds(1);
        Assert.Equal("no-application", (await world.RunAsync(leader, "approve", new { accountId = applicant })).Reason);
        Assert.Equal("dissolved", (await world.RunAsync(leader, "leave", new { })).Reason);
        // Resending the same create command returns the same alliance, never a second one.
        var founder = await world.AccountAsync();
        var create = new AccountCommand(Guid.NewGuid(), await world.VersionAsync(founder), AllianceStore.Domain, "create", JsonSerializer.SerializeToElement(new { name = "Again", mode = "open" }));
        string once = await world.Alliances.ExecuteAsync(founder, create, CancellationToken.None);
        Assert.Equal(once, await world.Alliances.ExecuteAsync(founder, create, CancellationToken.None));
        await using var created = world.Source.CreateCommand("SELECT count(*) FROM alliances WHERE leader=$1");
        created.Parameters.AddWithValue(founder);
        Assert.Equal(1L, await created.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ChatRateRetentionReportsAndPersonalBlocks()
    {
        await using var world = await World.CreateAsync(Content with { Chat = Content.Chat with { RetainedPerAlliance = 2 } });
        Guid leader = await world.AccountAsync(), member = await world.AccountAsync();
        var alliance = await world.AllianceAsync(leader);
        await world.RunAsync(member, "apply", new { allianceId = alliance });
        var first = Guid.NewGuid();
        Assert.Equal("sent", (await world.RunAsync(member, "chat", new { messageId = first, text = "hello" })).Reason);
        Assert.Equal("too-fast", (await world.RunAsync(member, "chat", new { messageId = Guid.NewGuid(), text = "again" })).Reason);
        world.Clock.Now = world.Clock.Now.AddSeconds(5);
        Assert.Equal("invalid-text", (await world.RunAsync(member, "chat", new { messageId = Guid.NewGuid(), text = new string('a', 201) })).Reason);
        Assert.Equal("sent", (await world.RunAsync(member, "chat", new { messageId = Guid.NewGuid(), text = new string('a', 200) })).Reason);
        var report = await world.RunAsync(leader, "report", new { messageId = first, reason = "spam-ad", note = "ad link" });
        Assert.Equal("reported", report.Reason);
        Assert.Equal(report.ReportId, (await world.RunAsync(leader, "report", new { messageId = first, reason = "spam-ad" })).ReportId);
        world.Clock.Now = world.Clock.Now.AddSeconds(5);
        await world.RunAsync(leader, "chat", new { messageId = Guid.NewGuid(), text = "third" });
        // Retention keeps the newest two; the reported message keeps its evidence snapshot.
        var chat = (await world.Alliances.ViewAsync(leader, CancellationToken.None)).Chat;
        Assert.Equal(2, chat.Length);
        Assert.DoesNotContain(chat, m => m.MessageId == first);
        await using (var evidence = world.Source.CreateCommand("SELECT body_snapshot,author FROM alliance_reports WHERE id=$1"))
        {
            evidence.Parameters.AddWithValue(report.ReportId!.Value);
            await using var reader = await evidence.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(("hello", member), (reader.GetString(0), reader.GetGuid(1)));
        }
        // Snapshots older than the retention period are removed on the next report.
        world.Clock.Now = world.Clock.Now.AddDays(Content.ReportRetentionDays).AddSeconds(1);
        var later = Guid.NewGuid();
        await world.RunAsync(member, "chat", new { messageId = later, text = "later" });
        await world.RunAsync(leader, "report", new { messageId = later, reason = "other" });
        await using (var kept = world.Source.CreateCommand("SELECT count(*) FROM alliance_reports WHERE id=$1"))
        {
            kept.Parameters.AddWithValue(report.ReportId!.Value);
            Assert.Equal(0L, await kept.ExecuteScalarAsync());
        }
        // A personal block hides the author's chat for the blocker only.
        Assert.Equal("blocked", (await world.RunAsync(leader, "block", new { accountId = member })).Reason);
        Assert.DoesNotContain((await world.Alliances.ViewAsync(leader, CancellationToken.None)).Chat, m => m.Author == member);
        Assert.Contains((await world.Alliances.ViewAsync(member, CancellationToken.None)).Chat, m => m.Author == member);
        Assert.Equal(2, (await world.Alliances.ViewAsync(leader, CancellationToken.None)).Members.Length);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ConcurrentApprovalsNeverOverfillAndAnAccountJoinsOnce()
    {
        await using var world = await World.CreateAsync(Content with { MemberLimit = 2 });
        Guid leader = await world.AccountAsync(), a = await world.AccountAsync(), b = await world.AccountAsync();
        var alliance = await world.AllianceAsync(leader, "approval");
        await world.RunAsync(a, "apply", new { allianceId = alliance });
        await world.RunAsync(b, "apply", new { allianceId = alliance });
        var officerCalls = await Task.WhenAll(world.TryAsync(leader, "approve", new { accountId = a }), world.TryAsync(leader, "approve", new { accountId = b }));
        Assert.Single(officerCalls, r => r!.Reason == "approved");
        Assert.Single(officerCalls, r => r!.Reason == "alliance-full");
        Assert.Equal(2, (await world.Alliances.ViewAsync(leader, CancellationToken.None)).Alliance!.MemberCount);

        // One account racing to join two open alliances ends in exactly one.
        Guid x = await world.AccountAsync(), y = await world.AccountAsync(), joiner = await world.AccountAsync();
        var first = await world.AllianceAsync(x);
        var secondAlliance = await world.AllianceAsync(y);
        var joins = await Task.WhenAll(world.TryAsync(joiner, "apply", new { allianceId = first }), world.TryAsync(joiner, "apply", new { allianceId = secondAlliance }));
        Assert.Single(joins, r => r!.Reason == "joined");
        await using var count = world.Source.CreateCommand("SELECT count(*) FROM alliance_members WHERE account_id=$1");
        count.Parameters.AddWithValue(joiner);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task CrossingOperationsDoNotDeadlock()
    {
        await using var world = await World.CreateAsync();
        var leaders = new[] { await world.AccountAsync(), await world.AccountAsync() };
        var alliances = new[] { await world.AllianceAsync(leaders[0]), await world.AllianceAsync(leaders[1]) };
        var people = new List<Guid>();
        for (int i = 0; i < 8; i++) { people.Add(await world.AccountAsync()); }
        // Joins, leaves, chats and blocks that touch the same accounts and both alliances from many tasks at once.
        var work = new List<Task<AllianceReply?>>();
        for (int round = 0; round < 3; round++)
        {
            foreach (var (person, index) in people.Select((p, i) => (p, i)))
            {
                var target = alliances[(index + round) % 2];
                work.Add(world.TryAsync(person, "apply", new { allianceId = target }));
                work.Add(world.TryAsync(leaders[index % 2], "block", new { accountId = person }));
                work.Add(world.TryAsync(person, "leave", new { }));
            }
        }
        var results = await Task.WhenAll(work); // a deadlock would surface here as PostgresException 40P01
        Assert.DoesNotContain(results, r => r is null);
        await using var counts = world.Source.CreateCommand("SELECT a.member_count = (SELECT count(*) FROM alliance_members m WHERE m.alliance_id=a.id) FROM alliances a WHERE a.id = ANY($1)");
        counts.Parameters.AddWithValue(alliances);
        await using var reader = await counts.ExecuteReaderAsync();
        while (await reader.ReadAsync()) { Assert.True(reader.GetBoolean(0)); }
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task HelpIsCappedSingleSettledAndBoundToTheAlliance()
    {
        await using var world = await World.CreateAsync();
        Guid requester = await world.AccountAsync();
        var alliance = await world.AllianceAsync(requester);
        var givers = new List<Guid>();
        for (int i = 0; i < 6; i++)
        {
            var giver = await world.AccountAsync();
            await world.RunAsync(giver, "apply", new { allianceId = alliance });
            givers.Add(giver);
        }
        // A job started before joining is never eligible.
        await world.ResearchJobAsync(requester, 1, world.NowMs - 10_000_000, world.NowMs + 200_000);
        Assert.Equal("job-started-outside-alliance", (await world.RunAsync(requester, "request-help", new { jobSeq = 1 })).Reason);
        await world.ResearchJobAsync(requester, 2, world.NowMs, world.NowMs + 200_000);
        Assert.Equal("requested", (await world.RunAsync(requester, "request-help", new { jobSeq = 2 })).Reason);
        Assert.Equal("already-requested", (await world.RunAsync(requester, "request-help", new { jobSeq = 2 })).Reason);
        Assert.Equal("own-request", (await world.RunAsync(requester, "give-help", new { requester, jobKey = "research:2" })).Reason);
        // Six givers at once: five grants of 4 s (cap 10% of 200 s), the sixth refused.
        var gives = await Task.WhenAll(givers.Select(g => world.TryAsync(g, "give-help", new { requester, jobKey = "research:2" })));
        Assert.Equal(5, gives.Count(r => r!.Reason == "helped"));
        Assert.All(gives.Where(r => r!.Reason == "helped"), r => Assert.Equal(4_000, r!.ShortenedMs));
        Assert.Contains(gives, r => r!.Reason is "help-full" or "no-time-to-shorten");
        var request = (await world.Alliances.ViewAsync(requester, CancellationToken.None)).Help.Single();
        Assert.Equal((20_000L, 5), (request.GivenMs, request.Grants));
        // The same giver twice at once on a fresh job: one grant.
        await world.ResearchJobAsync(requester, 3, world.NowMs, world.NowMs + 200_000);
        await world.RunAsync(requester, "request-help", new { jobSeq = 3 });
        var twice = await Task.WhenAll(world.TryAsync(givers[0], "give-help", new { requester, jobKey = "research:3" }), world.TryAsync(givers[0], "give-help", new { requester, jobKey = "research:3" }));
        Assert.Single(twice, r => r!.Reason == "helped");
        Assert.Single(twice, r => r!.Reason == "already-helped");
        // A finished job or a requester who left closes help.
        world.Clock.Now = world.Clock.Now.AddSeconds(201);
        Assert.Equal("job-not-active", (await world.RunAsync(givers[1], "give-help", new { requester, jobKey = "research:3" })).Reason);
        await world.ResearchJobAsync(requester, 4, world.NowMs, world.NowMs + 200_000);
        await world.RunAsync(requester, "request-help", new { jobSeq = 4 });
        await world.RunAsync(givers[1], "leave", new { });
        Assert.Equal("not-member", (await world.RunAsync(givers[1], "give-help", new { requester, jobKey = "research:4" })).Reason);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task MembershipGrantsNoBonusAndNeverTouchesAccountState()
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), member = await world.AccountAsync();
        async Task<(byte[] Payload, long Domains)> Snapshot(Guid id)
        {
            await using var query = world.Source.CreateCommand("SELECT payload,(SELECT count(*) FROM account_domains WHERE account_id=$1) FROM accounts WHERE id=$1");
            query.Parameters.AddWithValue(id);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return ((byte[])reader[0], reader.GetInt64(1));
        }
        var before = await Snapshot(member);
        var alliance = await world.AllianceAsync(leader);
        await world.RunAsync(member, "apply", new { allianceId = alliance });
        await world.RunAsync(member, "chat", new { messageId = Guid.NewGuid(), text = "hi" });
        var after = await Snapshot(member);
        // GDD19 §1: no combat, production or research bonus. The Core payload and domain states are byte-identical.
        Assert.Equal(before.Payload, after.Payload);
        Assert.Equal(before.Domains, after.Domains);
    }
}
