using System.Text.Json;
using Npgsql;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class AllianceTests
{
    // Frozen upgrade fixtures must not create the current moderation columns before migration.
    private const string LegacyAllianceSql = """
        CREATE TABLE IF NOT EXISTS alliance_schema(version integer PRIMARY KEY);
        INSERT INTO alliance_schema VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS alliances(
          id uuid PRIMARY KEY, name text NOT NULL, name_key text NOT NULL, mode text NOT NULL CHECK (mode IN ('open','approval')),
          notice text NOT NULL, status text NOT NULL CHECK (status IN ('active','dissolved')), leader uuid NOT NULL,
          member_count integer NOT NULL CHECK (member_count >= 0), created_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_members(
          account_id uuid PRIMARY KEY REFERENCES accounts(id), alliance_id uuid NOT NULL REFERENCES alliances(id),
          role text NOT NULL CHECK (role IN ('leader','officer','member')), joined_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_applications(
          account_id uuid PRIMARY KEY REFERENCES accounts(id), alliance_id uuid NOT NULL REFERENCES alliances(id),
          created_ms bigint NOT NULL, expires_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_restrictions(account_id uuid PRIMARY KEY REFERENCES accounts(id), until_ms bigint NOT NULL);
        CREATE TABLE IF NOT EXISTS alliance_messages(
          message_id uuid PRIMARY KEY, alliance_id uuid NOT NULL REFERENCES alliances(id), author uuid NOT NULL REFERENCES accounts(id),
          body text NOT NULL, at_ms bigint NOT NULL, seq bigserial UNIQUE);
        CREATE TABLE IF NOT EXISTS alliance_reports(
          id uuid PRIMARY KEY, reporter uuid NOT NULL REFERENCES accounts(id), message_id uuid NOT NULL, alliance_id uuid NOT NULL,
          author uuid NOT NULL, body_snapshot text NOT NULL, message_at_ms bigint NOT NULL, reason text NOT NULL, note text NOT NULL,
          created_ms bigint NOT NULL, UNIQUE(reporter,message_id,reason));
        CREATE TABLE IF NOT EXISTS alliance_blocks(
          account_id uuid NOT NULL REFERENCES accounts(id), blocked uuid NOT NULL REFERENCES accounts(id), PRIMARY KEY(account_id,blocked));
        CREATE TABLE IF NOT EXISTS alliance_help_requests(
          requester uuid NOT NULL REFERENCES accounts(id), job_key text NOT NULL, alliance_id uuid NOT NULL REFERENCES alliances(id),
          node_id text NOT NULL, duration_ms bigint NOT NULL, completes_ms bigint NOT NULL, given_ms bigint NOT NULL, grants integer NOT NULL,
          status text NOT NULL CHECK (status IN ('open','closed')), PRIMARY KEY(requester,job_key));
        CREATE TABLE IF NOT EXISTS alliance_help_grants(
          requester uuid NOT NULL, job_key text NOT NULL, giver uuid NOT NULL REFERENCES accounts(id), shortened_ms bigint NOT NULL,
          at_ms bigint NOT NULL, PRIMARY KEY(requester,job_key,giver), FOREIGN KEY(requester,job_key) REFERENCES alliance_help_requests(requester,job_key));
        """;
    private const string LegacyAllianceV2 = """
        ALTER TABLE alliance_messages ALTER COLUMN author DROP NOT NULL, ADD COLUMN IF NOT EXISTS author_ref text;
        ALTER TABLE alliance_reports ALTER COLUMN reporter DROP NOT NULL, ALTER COLUMN author DROP NOT NULL,
          ADD COLUMN IF NOT EXISTS reporter_ref text, ADD COLUMN IF NOT EXISTS author_ref text;
        ALTER TABLE alliances ALTER COLUMN leader DROP NOT NULL;
        INSERT INTO alliance_schema VALUES (2) ON CONFLICT DO NOTHING;
        """;

    [Theory]
    [InlineData("leader", "officer", true)]
    [InlineData("leader", "member", true)]
    [InlineData("officer", "member", true)]
    [InlineData("officer", "officer", false)]
    [InlineData("officer", "leader", false)]
    [InlineData("member", "member", false)]
    [InlineData("member", "officer", false)]
    [InlineData("member", "leader", false)]
    [Trait("Category", "Postgres")]
    public async Task MessageHideUsesCurrentRoleHierarchy(string actorRole, string authorRole, bool allowed)
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), actor = leader, author = leader;
        Guid alliance = await world.AllianceAsync(leader);
        if (actorRole != "leader")
        {
            actor = await world.AccountAsync();
            await world.RunAsync(actor, "apply", new { allianceId = alliance });
            if (actorRole == "officer") { await world.RunAsync(leader, "role-change", new { accountId = actor, role = "officer" }); }
        }
        if (authorRole != "leader")
        {
            author = await world.AccountAsync();
            await world.RunAsync(author, "apply", new { allianceId = alliance });
            if (authorRole == "officer") { await world.RunAsync(leader, "role-change", new { accountId = author, role = "officer" }); }
        }
        Guid messageId = Guid.NewGuid();
        await world.RunAsync(author, "chat", new { messageId, text = "original" });
        Assert.Equal(allowed, (await world.RunAsync(actor, "message-hide", new { messageId })).Done);
        Assert.False((await world.RunAsync(actor, "message-delete", new { messageId })).Done);
        Assert.False((await world.RunAsync(author, "message-hide", new { messageId })).Done);
        Assert.True((await world.RunAsync(author, "message-delete", new { messageId })).Done);
        var message = Assert.Single((await world.Alliances.ViewAsync(author, CancellationToken.None)).Chat);
        Assert.Equal(allowed ? "hidden" : "deleted", message.Status);
        Assert.Equal("", message.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task OnlyLeaderCanHideDepartedOrPseudonymisedAuthors(bool purged)
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), officer = await world.AccountAsync(), author = await world.AccountAsync(), stranger = await world.AccountAsync();
        Guid alliance = await world.AllianceAsync(leader);
        await world.RunAsync(officer, "apply", new { allianceId = alliance });
        await world.RunAsync(author, "apply", new { allianceId = alliance });
        await world.RunAsync(leader, "role-change", new { accountId = officer, role = "officer" });
        await world.AllianceAsync(stranger);
        Guid messageId = Guid.NewGuid();
        await world.RunAsync(author, "chat", new { messageId, text = "departing" });
        await world.RunAsync(author, "leave", new { });
        if (purged)
        {
            await using var pseudonym = world.Source.CreateCommand("UPDATE alliance_messages SET author=NULL,author_ref='fixture' WHERE message_id=$1");
            pseudonym.Parameters.AddWithValue(messageId); await pseudonym.ExecuteNonQueryAsync();
        }
        Assert.Equal("not-member", (await world.RunAsync(author, "message-delete", new { messageId })).Reason);
        Assert.Equal("unknown-message", (await world.RunAsync(stranger, "message-hide", new { messageId })).Reason);
        Assert.Equal("not-permitted", (await world.RunAsync(officer, "message-hide", new { messageId })).Reason);
        Assert.True((await world.RunAsync(leader, "message-hide", new { messageId })).Done);
        Assert.Equal("hidden", Assert.Single((await world.Alliances.ViewAsync(officer, CancellationToken.None)).Chat).Status);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ModerationAuditIsImmutableAndReportsKeepOriginalBody()
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), officer = await world.AccountAsync(), author = await world.AccountAsync();
        Guid alliance = await world.AllianceAsync(leader);
        await world.RunAsync(officer, "apply", new { allianceId = alliance });
        await world.RunAsync(author, "apply", new { allianceId = alliance });
        await world.RunAsync(leader, "role-change", new { accountId = officer, role = "officer" });
        Guid messageId = Guid.NewGuid();
        await world.RunAsync(author, "chat", new { messageId, text = "retained evidence" });
        long deleted = world.NowMs;
        await world.RunAsync(author, "message-delete", new { messageId });
        world.Clock.Now = world.Clock.Now.AddSeconds(1);
        long hidden = world.NowMs;
        await world.RunAsync(officer, "message-hide", new { messageId });
        world.Clock.Now = world.Clock.Now.AddSeconds(1);
        await world.RunAsync(author, "message-delete", new { messageId });
        await world.RunAsync(leader, "message-hide", new { messageId });
        var report = await world.RunAsync(leader, "report", new { messageId, reason = "other" });
        await using var check = world.Source.CreateCommand("SELECT m.body,m.deleted_by,m.deleted_ms,m.hidden_by,m.hidden_ms,r.body_snapshot FROM alliance_messages m JOIN alliance_reports r ON r.message_id=m.message_id WHERE r.id=$1");
        check.Parameters.AddWithValue(report.ReportId!.Value);
        await using var reader = await check.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("retained evidence", reader.GetString(0));
        Assert.Equal(author, reader.GetGuid(1)); Assert.Equal(deleted, reader.GetInt64(2));
        Assert.Equal(officer, reader.GetGuid(3)); Assert.Equal(hidden, reader.GetInt64(4));
        Assert.Equal("retained evidence", reader.GetString(5));
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task ReceiptReplayUsesCurrentVisibilityWithoutRepeatingEffects()
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), author = await world.AccountAsync();
        Guid alliance = await world.AllianceAsync(leader);
        await world.RunAsync(author, "apply", new { allianceId = alliance });
        Guid messageId = Guid.NewGuid();
        var command = new AccountCommand(Guid.NewGuid(), await world.VersionAsync(author), AllianceStore.Domain, "chat", JsonSerializer.SerializeToElement(new { messageId, text = "secret original" }));
        var initial = Parse(await world.Alliances.ExecuteAsync(author, command, CancellationToken.None));
        await world.RunAsync(leader, "message-hide", new { messageId });
        long version = await world.VersionAsync(author);
        await using var outbox = world.Source.CreateCommand("SELECT count(*) FROM outbox WHERE account_id=$1");
        outbox.Parameters.AddWithValue(author);
        long count = (long)(await outbox.ExecuteScalarAsync())!;
        var replay = Parse(await world.Alliances.ExecuteAsync(author, command, CancellationToken.None));
        Assert.Equal(initial.AccountVersion, replay.AccountVersion);
        Assert.Equal("hidden", Assert.Single(replay.Alliance.Chat).Status);
        Assert.Equal("", Assert.Single(replay.Alliance.Chat).Text);
        Assert.Equal(version, await world.VersionAsync(author));
        Assert.Equal(count, await outbox.ExecuteScalarAsync());
        await using var receipt = world.Source.CreateCommand("SELECT response FROM commands WHERE account_id=$1 AND command_id=$2");
        receipt.Parameters.AddWithValue(author); receipt.Parameters.AddWithValue(command.CommandId);
        string stored = (string)(await receipt.ExecuteScalarAsync())!;
        Assert.DoesNotContain("secret original", stored);
        using var storedJson = JsonDocument.Parse(stored);
        Assert.False(storedJson.RootElement.TryGetProperty("alliance", out _));
        await world.RunAsync(author, "leave", new { });
        var left = Parse(await world.Alliances.ExecuteAsync(author, command, CancellationToken.None));
        Assert.Null(left.Alliance.Alliance); Assert.Empty(left.Alliance.Chat);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task RetentionEvictsModeratedOriginalAndReceiptCannotResurrectIt()
    {
        await using var world = await World.CreateAsync(isolatedSchema: true);
        Guid leader = await world.AccountAsync(), author = await world.AccountAsync();
        Guid alliance = await world.AllianceAsync(leader);
        await world.RunAsync(author, "apply", new { allianceId = alliance });
        Guid messageId = Guid.NewGuid();
        var command = new AccountCommand(Guid.NewGuid(), await world.VersionAsync(author), AllianceStore.Domain, "chat", JsonSerializer.SerializeToElement(new { messageId, text = "expired original" }));
        await world.Alliances.ExecuteAsync(author, command, CancellationToken.None);
        await world.RunAsync(author, "message-delete", new { messageId });
        var reported = await world.RunAsync(leader, "report", new { messageId, reason = "other" });
        await using (var fill = world.Source.CreateCommand("INSERT INTO alliance_messages(message_id,alliance_id,author,body,at_ms) SELECT gen_random_uuid(),$1,$2,'newer',$3 FROM generate_series(1,$4)"))
        {
            fill.Parameters.AddWithValue(alliance); fill.Parameters.AddWithValue(author); fill.Parameters.AddWithValue(world.NowMs);
            fill.Parameters.AddWithValue(Content.Chat.RetainedPerAlliance); await fill.ExecuteNonQueryAsync();
        }
        world.Clock.Now = world.Clock.Now.AddSeconds(Content.Chat.IntervalSeconds);
        await world.RunAsync(author, "chat", new { messageId = Guid.NewGuid(), text = "newest" });
        var replay = Parse(await world.Alliances.ExecuteAsync(author, command, CancellationToken.None));
        Assert.Equal(Content.Chat.RetainedPerAlliance, replay.Alliance.Chat.Length);
        Assert.DoesNotContain(replay.Alliance.Chat, m => m.MessageId == messageId);
        await using var evidence = world.Source.CreateCommand("SELECT body_snapshot FROM alliance_reports WHERE id=$1");
        evidence.Parameters.AddWithValue(reported.ReportId!.Value);
        Assert.Equal("expired original", await evidence.ExecuteScalarAsync());
        world.Clock.Now = world.Clock.Now.AddDays(Content.ReportRetentionDays).AddMilliseconds(1);
        await world.Alliances.PruneReportsAsync(CancellationToken.None);
        Assert.Null(await evidence.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Category", "Postgres")]
    public async Task ModerationMigrationPreservesEvidenceAndStripsOnlyAllianceSnapshots(int legacyVersion)
    {
        await using var world = await World.CreateAsync(legacySchema: legacyVersion);
        Guid owner = await world.AccountAsync(), alliance = Guid.NewGuid(), message = Guid.NewGuid(), receiptId = Guid.NewGuid(), otherId = Guid.NewGuid();
        async Task Seed(string sql, params object[] values)
        {
            await using var query = world.Source.CreateCommand(sql);
            foreach (var value in values) { query.Parameters.AddWithValue(value); }
            await query.ExecuteNonQueryAsync();
        }
        await Seed("INSERT INTO alliances VALUES($1,'Legacy','LEGACY','open','','active',$2,1,$3)", alliance, owner, world.NowMs);
        await Seed("INSERT INTO alliance_members VALUES($1,$2,'leader',$3)", owner, alliance, world.NowMs);
        await Seed("INSERT INTO alliance_messages(message_id,alliance_id,author,body,at_ms) VALUES($1,$2,$3,'evidence',$4)", message, alliance, owner, world.NowMs);
        await Seed("INSERT INTO commands VALUES($1,$2,'legacy',$3),($1,$4,'other',$5)", owner, receiptId,
            """{"done":true,"reason":"sent","accountVersion":7,"alliance":{"role":"leader","members":[],"chat":[{"text":"old private text"}],"help":[]},"createdId":null,"reportId":null,"shortenedMs":null}""",
            otherId, """{"done":true,"alliance":"unrelated","body":"preserve"}""");
        await world.Alliances.InitializeAsync(CancellationToken.None);
        await world.Alliances.InitializeAsync(CancellationToken.None);
        await using (var version = world.Source.CreateCommand("SELECT max(version) FROM alliance_schema")) { Assert.Equal(3, await version.ExecuteScalarAsync()); }
        await using (var saved = world.Source.CreateCommand("SELECT response FROM commands WHERE command_id=$1"))
        {
            saved.Parameters.AddWithValue(receiptId);
            string response = (string)(await saved.ExecuteScalarAsync())!;
            using var json = JsonDocument.Parse(response);
            Assert.False(json.RootElement.TryGetProperty("alliance", out _));
            Assert.Equal(7, json.RootElement.GetProperty("accountVersion").GetInt64());
            Assert.Equal("sent", json.RootElement.GetProperty("reason").GetString());
            saved.Parameters.Clear(); saved.Parameters.AddWithValue(otherId);
            Assert.Equal("{\"done\":true,\"alliance\":\"unrelated\",\"body\":\"preserve\"}", await saved.ExecuteScalarAsync());
        }
        var chat = Assert.Single((await world.Alliances.ViewAsync(owner, CancellationToken.None)).Chat);
        Assert.Equal("evidence", chat.Text); Assert.Equal("visible", chat.Status);
        Assert.True((await world.RunAsync(owner, "message-delete", new { messageId = message })).Done);
    }

    [Fact]
    [Trait("Category", "Postgres")]
    public async Task FutureAllianceSchemaRefusesMigrationWithoutChangingTablesOrReceipts()
    {
        await using var world = await World.CreateAsync(legacySchema: 2);
        Guid owner = await world.AccountAsync(), receipt = Guid.NewGuid();
        const string response = "{\"done\":true,\"reason\":\"sent\",\"accountVersion\":1,\"alliance\":{\"role\":\"member\",\"members\":[],\"chat\":[],\"help\":[]}}";
        await using (var version = world.Source.CreateCommand("INSERT INTO alliance_schema VALUES(99)")) { await version.ExecuteNonQueryAsync(); }
        await using (var future = world.Source.CreateCommand("INSERT INTO commands VALUES($1,$2,'legacy',$3)"))
        {
            future.Parameters.AddWithValue(owner); future.Parameters.AddWithValue(receipt); future.Parameters.AddWithValue(response);
            await future.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => world.Alliances.InitializeAsync(CancellationToken.None));
        await using var check = world.Source.CreateCommand("SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='alliance_messages' AND column_name='hidden_ms'");
        Assert.Equal(0L, await check.ExecuteScalarAsync());
        await using var saved = world.Source.CreateCommand("SELECT response FROM commands WHERE command_id=$1");
        saved.Parameters.AddWithValue(receipt); Assert.Equal(response, await saved.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task MessageHideRechecksRolesAfterActualAllianceLockWait(bool promoteAuthor)
    {
        await using var world = await World.CreateAsync();
        Guid leader = await world.AccountAsync(), officer = await world.AccountAsync(), author = await world.AccountAsync();
        Guid alliance = await world.AllianceAsync(leader);
        await world.RunAsync(officer, "apply", new { allianceId = alliance });
        await world.RunAsync(author, "apply", new { allianceId = alliance });
        await world.RunAsync(leader, "role-change", new { accountId = officer, role = "officer" });
        Guid messageId = Guid.NewGuid();
        await world.RunAsync(author, "chat", new { messageId, text = "unchanged" });
        var command = new AccountCommand(Guid.NewGuid(), await world.VersionAsync(officer), AllianceStore.Domain, "message-hide", JsonSerializer.SerializeToElement(new { messageId }));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = await world.Source.OpenConnectionAsync(timeout.Token);
        await using var transaction = await connection.BeginTransactionAsync(timeout.Token);
        await using (var hold = new NpgsqlCommand("SELECT 1 FROM alliances WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            hold.Parameters.AddWithValue(alliance); await hold.ExecuteScalarAsync(timeout.Token);
        }
        var hiding = world.Alliances.ExecuteAsync(officer, command, timeout.Token);
        await using (var waiting = world.Source.CreateCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE $1=ANY(pg_blocking_pids(pid)))"))
        {
            waiting.Parameters.AddWithValue(connection.ProcessID);
            while (await waiting.ExecuteScalarAsync(timeout.Token) is not true)
            {
                Assert.False(hiding.IsCompleted); await Task.Delay(10, timeout.Token);
            }
        }
        // Complete a role transition while holding the same alliance/account lock order as role-change.
        await using (var accountLock = new NpgsqlCommand("SELECT 1 FROM accounts WHERE id=$1 FOR UPDATE", connection, transaction))
        {
            accountLock.Parameters.AddWithValue(promoteAuthor ? author : officer);
            await accountLock.ExecuteScalarAsync(timeout.Token);
        }
        await using (var change = new NpgsqlCommand("UPDATE alliance_members SET role=$2 WHERE account_id=$1", connection, transaction))
        {
            change.Parameters.AddWithValue(promoteAuthor ? author : officer);
            change.Parameters.AddWithValue(promoteAuthor ? "officer" : "member");
            await change.ExecuteNonQueryAsync(timeout.Token);
        }
        await transaction.CommitAsync(timeout.Token);
        Assert.Equal("not-permitted", Parse(await hiding).Reason);
        Assert.Equal("visible", Assert.Single((await world.Alliances.ViewAsync(leader, timeout.Token)).Chat).Status);
    }
}
