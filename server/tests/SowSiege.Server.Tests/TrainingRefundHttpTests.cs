using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Postgres")]
    public async Task CancelReceiptReplaysExactlyAndReleasedQueueSurvivesRestart(bool walletFull)
    {
        var time = new TrainingClock();
        string token;
        int grainAfterActive;
        Guid accountId;
        using (var factory = new TrainingFactory(time))
        using (var client = factory.CreateClient())
        {
            var createdResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
            createdResponse.EnsureSuccessStatusCode();
            var created = (await createdResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
            token = created.Token;
            accountId = (await factory.Services.GetRequiredService<AccountStore>().AuthenticateAsync(token, CancellationToken.None))!.Value;
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            int barracks = created.Estate.Buildings.Single(b => b.Kind == "meta:barracks").Id;
            var buildResponse = await client.PostAsJsonAsync("/v1/estate/commands",
                new EstateCommand(Guid.NewGuid(), created.Estate.Version, "start-construction", barracks));
            buildResponse.EnsureSuccessStatusCode();
            var build = (await buildResponse.Content.ReadFromJsonAsync<CommandReply>())!;
            Assert.True(build.Done, build.Reason);
            time.Now = time.Now.AddSeconds(build.Estate.Construction!.RemainingSeconds);
            var built = (await client.GetFromJsonAsync<AccountReply>("/v1/account"))!;
            var command = new AccountCommand(Guid.NewGuid(), built.Estate.Version, "training", "start",
                JsonSerializer.SerializeToElement(new TrainingStart("slg:levy", 1), TrainingContent.Json));
            var firstResponse = await client.PostAsJsonAsync("/v1/account/commands", command);
            firstResponse.EnsureSuccessStatusCode();
            var first = (await firstResponse.Content.ReadFromJsonAsync<AccountReply>())!;
            Assert.True(first.Done, first.Reason);
            grainAfterActive = first.Estate.Wallet["meta:grain"];
            var waiting = command with { CommandId = Guid.NewGuid(), ExpectedVersion = first.Estate.Version };
            var queueResponse = await client.PostAsJsonAsync("/v1/account/commands", waiting);
            queueResponse.EnsureSuccessStatusCode();
            var queued = (await queueResponse.Content.ReadFromJsonAsync<AccountReply>())!;
            Assert.True(queued.Done, queued.Reason);
            if (walletFull)
            {
                // Test-only fixture: isolate a full wallet after reservation without changing production rules.
                var source = factory.Services.GetRequiredService<NpgsqlDataSource>();
                await using var read = source.CreateCommand("SELECT payload FROM accounts WHERE id=$1");
                read.Parameters.AddWithValue(accountId);
                var saved = MetaSaveCodec.Decode((byte[])(await read.ExecuteScalarAsync())!).State!;
                var content = EstateContent.Load(ContentEstateTests.Root());
                var filled = saved with { Wallet = saved.Wallet.ToDictionary(p => p.Key, p => content.Config.WalletCaps[p.Key]) };
                await using var update = source.CreateCommand("UPDATE accounts SET payload=$1 WHERE id=$2");
                update.Parameters.AddWithValue(MetaSaveCodec.Encode(filled));
                update.Parameters.AddWithValue(accountId);
                Assert.Equal(1, await update.ExecuteNonQueryAsync());
                grainAfterActive = filled.Wallet["meta:grain"];
            }
            var cancel = new AccountCommand(Guid.NewGuid(), queued.Estate.Version, "training", "cancel-waiting",
                JsonSerializer.SerializeToElement(new TrainingCancel(waiting.CommandId), TrainingContent.Json));
            var cancelled = await client.PostAsJsonAsync("/v1/account/commands", cancel);
            cancelled.EnsureSuccessStatusCode();
            string receipt = await cancelled.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<AccountReply>(receipt, AccountStore.Json)!;
            Assert.True(result.Done);
            var pending = result.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!.PendingRefunds;
            Assert.Equal(walletFull ? 8 : 0, pending.SingleOrDefault(p => p.MaterialId == "meta:grain")?.Amount ?? 0);
            Assert.Equal(walletFull ? 3 : 0, pending.SingleOrDefault(p => p.MaterialId == "meta:timber")?.Amount ?? 0);
            var retry = await client.PostAsJsonAsync("/v1/account/commands", cancel);
            retry.EnsureSuccessStatusCode();
            Assert.Equal(receipt, await retry.Content.ReadAsStringAsync());
        }
        using var restarted = new TrainingFactory(time);
        using var reconnected = restarted.CreateClient();
        reconnected.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var account = (await reconnected.GetFromJsonAsync<AccountReply>("/v1/account"))!;
        var view = account.Domains["training"].Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.Null(view.Waiting);
        Assert.Equal(1, view.Occupied);
        Assert.Equal(walletFull ? 8 : 0, view.PendingRefunds.SingleOrDefault(p => p.MaterialId == "meta:grain")?.Amount ?? 0);
        Assert.Equal(walletFull ? 3 : 0, view.PendingRefunds.SingleOrDefault(p => p.MaterialId == "meta:timber")?.Amount ?? 0);
        var database = restarted.Services.GetRequiredService<NpgsqlDataSource>();
        await using var persisted = database.CreateCommand("SELECT state_json::text FROM account_domains WHERE account_id=$1 AND domain='training'");
        persisted.Parameters.AddWithValue(accountId);
        var state = JsonSerializer.Deserialize<TrainingState>((string)(await persisted.ExecuteScalarAsync())!, TrainingContent.Json)!;
        Assert.Equal(walletFull ? 8 : 0, state.PendingRefunds.GetValueOrDefault("meta:grain"));
        Assert.Equal(walletFull ? 3 : 0, state.PendingRefunds.GetValueOrDefault("meta:timber"));
        Assert.Null(state.Waiting);
        Assert.Equal(1, state.Units.Sum(u => u.Reserved));
        await using var counts = database.CreateCommand("SELECT count(*) FROM domain_events WHERE account_id=$1 AND domain='training' AND kind='training-cancelled'");
        counts.Parameters.AddWithValue(accountId);
        Assert.Equal(1L, (long)(await counts.ExecuteScalarAsync())!);
        Assert.Equal(grainAfterActive, account.Estate.Wallet["meta:grain"]);
    }
}
