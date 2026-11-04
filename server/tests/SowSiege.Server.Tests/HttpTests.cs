using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Server.Infrastructure;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class HttpTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AuthenticationAndStrictPayloadProtectServerState()
    {
        using var factory = new PrototypeFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
        var createdResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
        createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var store = factory.Services.GetRequiredService<AccountStore>();
        var source = factory.Services.GetRequiredService<NpgsqlDataSource>();
        Guid account = (await store.AuthenticateAsync(created.Token, CancellationToken.None))!.Value;
        async Task<string> StoredClockAndReceipts()
        {
            await using var query = source.CreateCommand("SELECT version,utc_highwater,logical_seconds,fraction_ms,(SELECT count(*) FROM commands WHERE account_id=$1) FROM accounts WHERE id=$1");
            query.Parameters.AddWithValue(account);
            await using var reader = await query.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return string.Join(":", Enumerable.Range(0, 5).Select(reader.GetValue));
        }
        string before = await StoredClockAndReceipts();
        var missingVersion = new { commandId = Guid.NewGuid(), type = "claim-production" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/estate/commands", missingVersion)).StatusCode);
        Assert.Equal(before, await StoredClockAndReceipts());
        var forged = new { commandId = Guid.NewGuid(), expectedVersion = 0, type = "claim-production", wallet = 999999, now = 999999 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/estate/commands", forged)).StatusCode);
        var missingDomainVersion = new { commandId = Guid.NewGuid(), domain = "test", operation = "start", payload = new { } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", missingDomainVersion)).StatusCode);
        var scalar = new { commandId = Guid.NewGuid(), expectedVersion = 0, domain = "test", operation = "start", payload = 42 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", scalar)).StatusCode);
        var unknown = new { commandId = Guid.NewGuid(), expectedVersion = 0, domain = "test", operation = "start", payload = new { }, wallet = 999 };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", unknown)).StatusCode);
        Assert.Equal(before, await StoredClockAndReceipts());
        var refreshed = await client.GetFromJsonAsync<CommandReply>("/v1/estate");
        Assert.Equal(1, refreshed!.Estate.Version);
        client.DefaultRequestHeaders.Authorization = new("Bearer", new string('0', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/estate")).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RecruitAndResearchRunThroughAccountCommands()
    {
        using var factory = new PrototypeFactory();
        using var client = factory.CreateClient();
        var created = (await (await client.PostAsJsonAsync("/prototype/accounts", new { })).Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        var odds = account.Domains["recruit"];
        Assert.Equal(1.0, odds.GetProperty("next").EnumerateArray().Sum(o => o.GetProperty("probability").GetDouble()), 12);
        Assert.Equal(3000, account.Resources.Single(r => r.Id == "slg:gem").Amount);
        Assert.All(account.Domains["research"].GetProperty("nodes").EnumerateArray(), n => Assert.Equal("locked", n.GetProperty("status").GetString()));
        var forged = new { commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version, domain = "recruit", operation = "recruit",
            payload = new { poolVersion = odds.GetProperty("poolVersion").GetString(), heroId = "slg:ruin_seeker" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", forged)).StatusCode);
        var clientTime = new { commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version, domain = "research", operation = "complete-research",
            payload = new { nodeId = "slg:research-field-yield", completesAtMs = 0 } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", clientTime)).StatusCode);
        var recruit = new { commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version, domain = "recruit", operation = "recruit",
            payload = new { poolVersion = odds.GetProperty("poolVersion").GetString() } };
        var reply = await (await client.PostAsJsonAsync("/v1/account/commands", recruit)).Content.ReadFromJsonAsync<AccountReply>();
        Assert.True(reply!.Done);
        Assert.Equal(2900, reply.Resources.Single(r => r.Id == "slg:gem").Amount);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AllianceRoleChangesAndKickRunThroughAccountCommands()
    {
        using var factory = new PrototypeFactory();
        using var leader = factory.CreateClient();
        using var member = factory.CreateClient();
        foreach (var client in new[] { leader, member })
        {
            var response = await client.PostAsJsonAsync("/prototype/accounts", new { });
            response.EnsureSuccessStatusCode();
            var created = (await response.Content.ReadFromJsonAsync<AccountCreated>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        }
        async Task<AllianceReply> Command(HttpClient client, string operation, object payload)
        {
            var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            var response = await client.PostAsJsonAsync("/v1/account/commands", new
            {
                commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version,
                domain = "alliance", operation, payload,
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AllianceReply>())!;
        }

        var createdAlliance = await Command(leader, "create", new { name = "HTTP Guard", mode = "open" });
        Assert.True(createdAlliance.Done);
        Assert.NotNull(createdAlliance.CreatedId);
        var joined = await Command(member, "apply", new { allianceId = createdAlliance.CreatedId.Value });
        Assert.True(joined.Done);
        Guid target = joined.Alliance.Members.Single(m => m.Role == "member").AccountId;

        foreach (string role in new[] { "officer", "member" })
        {
            var changed = await Command(leader, "role-change", new { accountId = target, role });
            Assert.True(changed.Done);
            Assert.Equal("role-changed", changed.Reason);
            var view = (await member.GetFromJsonAsync<AllianceView>("/v1/alliance"))!;
            Assert.Equal(role, view.Role);
            Assert.Equal(createdAlliance.CreatedId, view.Alliance!.Id);
        }
        var kicked = await Command(leader, "kick", new { accountId = target });
        Assert.True(kicked.Done);
        Assert.Equal("kicked", kicked.Reason);
        Assert.DoesNotContain(kicked.Alliance.Members, m => m.AccountId == target);

        // The account remains authenticated, but its alliance-only information is no longer returned.
        var departed = (await member.GetFromJsonAsync<AllianceView>("/v1/alliance"))!;
        Assert.Null(departed.Alliance);
        Assert.Empty(departed.Members);
        Assert.Empty(departed.Chat);
        Assert.Empty(departed.Help);
        Assert.Empty(departed.Applications);
        Assert.NotNull(departed.RestrictedUntilMs);
        var rejoin = await Command(member, "apply", new { allianceId = createdAlliance.CreatedId.Value });
        Assert.False(rejoin.Done);
        Assert.Equal("rejoin-restricted", rejoin.Reason);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ModerationAndReceiptReplayRespectCurrentAllianceVisibility()
    {
        using var factory = new PrototypeFactory();
        using var leader = factory.CreateClient();
        using var member = factory.CreateClient();
        foreach (var client in new[] { leader, member })
        {
            var created = (await (await client.PostAsJsonAsync("/prototype/accounts", new { })).Content.ReadFromJsonAsync<AccountCreated>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        }
        async Task<(AccountCommand Command, AllianceReply Reply)> Command(HttpClient client, string operation, object payload)
        {
            var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            var command = new AccountCommand(Guid.NewGuid(), account.Estate.Version, "alliance", operation,
                System.Text.Json.JsonSerializer.SerializeToElement(payload));
            var response = await client.PostAsJsonAsync("/v1/account/commands", command);
            response.EnsureSuccessStatusCode();
            return (command, (await response.Content.ReadFromJsonAsync<AllianceReply>())!);
        }
        var createdAlliance = await Command(leader, "create", new { name = "Moderation", mode = "open" });
        Assert.True(createdAlliance.Reply.Done);
        Assert.True((await Command(member, "apply", new { allianceId = createdAlliance.Reply.CreatedId!.Value })).Reply.Done);
        Guid hiddenId = Guid.NewGuid(), deletedId = Guid.NewGuid();
        var original = await Command(member, "chat", new { messageId = hiddenId, text = "original private text" });
        Assert.True(original.Reply.Done);
        Assert.True((await Command(leader, "message-hide", new { messageId = hiddenId })).Reply.Done);
        Assert.True((await Command(leader, "chat", new { messageId = deletedId, text = "self deleted text" })).Reply.Done);
        Assert.True((await Command(leader, "message-delete", new { messageId = deletedId })).Reply.Done);
        var current = (await member.GetFromJsonAsync<AllianceView>("/v1/alliance"))!;
        Assert.Equal(("hidden", ""), (current.Chat.Single(m => m.MessageId == hiddenId).Status, current.Chat.Single(m => m.MessageId == hiddenId).Text));
        Assert.Equal(("deleted", ""), (current.Chat.Single(m => m.MessageId == deletedId).Status, current.Chat.Single(m => m.MessageId == deletedId).Text));
        var replayResponse = await member.PostAsJsonAsync("/v1/account/commands", original.Command);
        replayResponse.EnsureSuccessStatusCode();
        var replay = (await replayResponse.Content.ReadFromJsonAsync<AllianceReply>())!;
        Assert.Equal(original.Reply.AccountVersion, replay.AccountVersion);
        Assert.True(replay.Done);
        Assert.All(replay.Alliance.Chat, message => Assert.Empty(message.Text));
        Assert.True((await Command(member, "leave", new { })).Reply.Done);
        var departedResponse = await member.PostAsJsonAsync("/v1/account/commands", original.Command);
        departedResponse.EnsureSuccessStatusCode();
        var departed = (await departedResponse.Content.ReadFromJsonAsync<AllianceReply>())!;
        Assert.Equal(original.Reply.AccountVersion, departed.AccountVersion);
        Assert.Null(departed.Alliance.Alliance);
        Assert.Empty(departed.Alliance.Chat);
        Assert.Empty(departed.Alliance.Members);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AllianceLeadershipTransferChangesAuthorityThroughHttp()
    {
        using var factory = new PrototypeFactory();
        using var originalLeader = factory.CreateClient();
        using var successor = factory.CreateClient();
        foreach (var client in new[] { originalLeader, successor })
        {
            var response = await client.PostAsJsonAsync("/prototype/accounts", new { });
            response.EnsureSuccessStatusCode();
            var created = (await response.Content.ReadFromJsonAsync<AccountCreated>())!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        }
        async Task<AllianceReply> Command(HttpClient client, string operation, object payload)
        {
            var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            var response = await client.PostAsJsonAsync("/v1/account/commands", new
            {
                commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version,
                domain = "alliance", operation, payload,
            });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AllianceReply>())!;
        }

        var createdAlliance = await Command(originalLeader, "create", new { name = "HTTP Heirs", mode = "open" });
        Assert.True(createdAlliance.Done);
        Assert.NotNull(createdAlliance.CreatedId);
        var joined = await Command(successor, "apply", new { allianceId = createdAlliance.CreatedId.Value });
        Assert.True(joined.Done);
        Guid originalId = joined.Alliance.Members.Single(m => m.Role == "leader").AccountId;
        Guid successorId = joined.Alliance.Members.Single(m => m.Role == "member").AccountId;

        async Task AssertRoles(Guid leaderId, string originalRole, string successorRole)
        {
            foreach (var (client, role) in new[] { (originalLeader, originalRole), (successor, successorRole) })
            {
                var view = (await client.GetFromJsonAsync<AllianceView>("/v1/alliance"))!;
                Assert.Equal(role, view.Role);
                Assert.Equal(createdAlliance.CreatedId, view.Alliance!.Id);
                Assert.Equal(leaderId, view.Alliance.Leader);
                Assert.Equal(2, view.Alliance.MemberCount);
                Assert.Equal(2, view.Members.Length);
                Assert.Equal(leaderId, Assert.Single(view.Members, m => m.Role == "leader").AccountId);
                Assert.Single(view.Members, m => m.Role == "member");
            }
        }

        var transferred = await Command(originalLeader, "transfer", new { accountId = successorId });
        Assert.True(transferred.Done);
        await AssertRoles(successorId, "member", "leader");

        var refused = await Command(originalLeader, "transfer", new { accountId = successorId });
        Assert.False(refused.Done);
        Assert.Equal("not-permitted", refused.Reason);
        await AssertRoles(successorId, "member", "leader");

        var returned = await Command(successor, "transfer", new { accountId = originalId });
        Assert.True(returned.Done);
        await AssertRoles(originalId, "leader", "member");
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ClientsCannotSubmitRunVerdicts()
    {
        using var factory = new PrototypeFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"/v1/runs/{Guid.NewGuid()}/replay", new ByteArrayContent([1]))).StatusCode);
        var created = (await (await client.PostAsJsonAsync("/prototype/accounts", new { })).Content.ReadFromJsonAsync<AccountCreated>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", created.Token);
        var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        var forged = new { commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version, domain = "research", operation = "settle-expedition",
            payload = new { runId = Guid.NewGuid(), verdict = "verified", endKind = "Duration", tick = 1, stateHash = "x", replayHash = "y" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/account/commands", forged)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/v1/runs/{Guid.NewGuid()}/replay", new ByteArrayContent([1, 2, 3]))).StatusCode);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public void ProductionCannotStartPrototypeEndpoints()
    {
        using var factory = new PrototypeFactory("Production");
        var failure = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("requires Development", failure.Message);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task AllianceAccountLifecycleRunsThroughHttp()
    {
        string connection = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
            ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
        string schema = "alliance_lifecycle_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(connection))
        await using (var create = admin.CreateCommand("CREATE SCHEMA " + schema))
        {
            await create.ExecuteNonQueryAsync();
        }
        string isolated = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        var clock = new AllianceLifecycleClock();
        using var originalFactory = new PrototypeFactory();
        using var factory = originalFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = isolated,
            }));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock));
        });
        using var founder = factory.CreateClient();
        using var successor = factory.CreateClient();
        using var officer = factory.CreateClient();
        using var target = factory.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        async Task<AuthTokens> Guest(HttpClient client)
        {
            using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            var response = await client.PostAsJsonAsync("/v1/auth/guest", new
            {
                publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            }, timeout.Token);
            response.EnsureSuccessStatusCode();
            var tokens = (await response.Content.ReadFromJsonAsync<AuthTokens>(cancellationToken: timeout.Token))!;
            client.DefaultRequestHeaders.Authorization = new("Bearer", tokens.AccessToken);
            return tokens;
        }
        async Task<AllianceReply> Command(HttpClient client, string operation, object payload)
        {
            clock.Advance();
            var account = (await client.GetFromJsonAsync<AccountReply>("/v1/account", timeout.Token))!;
            var response = await client.PostAsJsonAsync("/v1/account/commands", new
            {
                commandId = Guid.NewGuid(), expectedVersion = account.Estate.Version,
                domain = "alliance", operation, payload,
            }, timeout.Token);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<AllianceReply>(cancellationToken: timeout.Token))!;
        }
        async Task<AllianceView> View(HttpClient client) =>
            (await client.GetFromJsonAsync<AllianceView>("/v1/alliance", timeout.Token))!;
        static void OneLeader(AllianceView view, Guid expectedLeader, int expectedCount)
        {
            Assert.Equal(expectedLeader, view.Alliance!.Leader);
            Assert.Equal(expectedCount, view.Alliance.MemberCount);
            Assert.Equal(expectedCount, view.Members.Length);
            Assert.Equal(expectedLeader, Assert.Single(view.Members, m => m.Role == "leader").AccountId);
        }

        var founderAuth = await Guest(founder);
        var successorAuth = await Guest(successor);
        var officerAuth = await Guest(officer);
        var targetAuth = await Guest(target);
        var created = await Command(founder, "create", new { name = "Lifecycle", mode = "open" });
        Assert.True(created.Done);
        Guid alliance = created.CreatedId!.Value;
        foreach (var client in new[] { successor, officer, target })
        {
            Assert.True((await Command(client, "apply", new { allianceId = alliance })).Done);
        }
        foreach (string role in new[] { "officer", "member", "officer" })
        {
            var changed = await Command(founder, "role-change", new { accountId = officerAuth.AccountId, role });
            Assert.True(changed.Done);
            Assert.Equal(role, (await View(officer)).Role);
        }

        Guid message = Guid.NewGuid();
        Assert.True((await Command(target, "chat", new { messageId = message, text = "retained moderation evidence" })).Done);
        // Uncontested hierarchy: an officer hides a current ordinary member's message.
        var hidden = await Command(officer, "message-hide", new { messageId = message });
        Assert.True(hidden.Done);
        Assert.Equal("message-hidden", hidden.Reason);
        var hiddenView = (await View(founder)).Chat.Single(m => m.MessageId == message);
        Assert.Equal("hidden", hiddenView.Status);
        Assert.Equal("", hiddenView.Text);
        Assert.True((await Command(officer, "kick", new { accountId = targetAuth.AccountId })).Done);
        var departed = await View(target);
        Assert.Null(departed.Alliance);
        Assert.Empty(departed.Chat);
        Assert.Empty(departed.Members);
        var rejoin = await Command(target, "apply", new { allianceId = alliance });
        Assert.False(rejoin.Done);
        Assert.Equal("rejoin-restricted", rejoin.Reason);

        Assert.True((await Command(founder, "transfer", new { accountId = successorAuth.AccountId })).Done);
        var afterTransfer = await View(founder);
        Assert.Equal("member", afterTransfer.Role);
        OneLeader(afterTransfer, successorAuth.AccountId, 3);
        Assert.Equal("leader", (await View(successor)).Role);
        var refused = await Command(founder, "transfer", new { accountId = officerAuth.AccountId });
        Assert.False(refused.Done);
        Assert.Equal("not-permitted", refused.Reason);
        OneLeader(await View(officer), successorAuth.AccountId, 3);
        // The original founder is an older ordinary member than the officer: chronology alone must not select the heir.
        Assert.True(afterTransfer.Members.Single(m => m.AccountId == founderAuth.AccountId).JoinedAtMs
            < afterTransfer.Members.Single(m => m.AccountId == officerAuth.AccountId).JoinedAtMs);

        var deletion = await successor.PostAsJsonAsync("/v1/account/delete", new { confirm = "delete" }, timeout.Token);
        deletion.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await successor.GetAsync("/v1/alliance", timeout.Token)).StatusCode);
        foreach (var client in new[] { founder, officer })
        {
            var view = await View(client);
            OneLeader(view, officerAuth.AccountId, 2);
            Assert.DoesNotContain(view.Members, m => m.AccountId == successorAuth.AccountId);
            var preserved = view.Chat.Single(m => m.MessageId == message);
            Assert.Equal("hidden", preserved.Status);
            Assert.Equal("", preserved.Text);
        }
        Assert.Equal("leader", (await View(officer)).Role);
        Assert.Equal("member", (await View(founder)).Role);
    }

    private sealed class AllianceLifecycleClock : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance() => Interlocked.Add(ref ticks, TimeSpan.FromSeconds(10).Ticks);
    }

    private sealed class PrototypeFactory(string environment = "Development") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            string postgres = Environment.GetEnvironmentVariable("SERVER_TEST_POSTGRES")
                ?? throw new InvalidOperationException("SERVER_TEST_POSTGRES is required.");
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Prototype:Enabled"] = "true", ["GameContentRoot"] = ContentEstateTests.Root(),
                ["ConnectionStrings:Postgres"] = postgres,
                ["ConnectionStrings:Redis"] = "127.0.0.1:1",
                ["Domains:SeedKey"] = RecruitTests.TestKeyHex, ["Prototype:RecruitGems"] = "3000",
                ["Auth:PseudonymKey"] = RecruitTests.TestKeyHex
            }));
        }
    }
}
