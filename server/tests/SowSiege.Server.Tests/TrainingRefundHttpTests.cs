using System.Net.Http.Json;
using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using SowSiege.Server.Infrastructure;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingHttpTests
{
    [Fact]
    [Trait("Category", "Postgres")]
    public async Task CancelReceiptReplaysExactlyAndReleasedQueueSurvivesRestart()
    {
        var time = new TrainingClock();
        string token;
        int grainAfterActive;
        using (var factory = new TrainingFactory(time))
        using (var client = factory.CreateClient())
        {
            var createdResponse = await client.PostAsJsonAsync("/prototype/accounts", new { });
            createdResponse.EnsureSuccessStatusCode();
            var created = (await createdResponse.Content.ReadFromJsonAsync<AccountCreated>())!;
            token = created.Token;
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
            var cancel = new AccountCommand(Guid.NewGuid(), queued.Estate.Version, "training", "cancel-waiting",
                JsonSerializer.SerializeToElement(new TrainingCancel(waiting.CommandId), TrainingContent.Json));
            var cancelled = await client.PostAsJsonAsync("/v1/account/commands", cancel);
            cancelled.EnsureSuccessStatusCode();
            string receipt = await cancelled.Content.ReadAsStringAsync();
            Assert.True(JsonSerializer.Deserialize<AccountReply>(receipt, AccountStore.Json)!.Done);
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
        Assert.Empty(view.PendingRefunds);
        Assert.Equal(grainAfterActive, account.Estate.Wallet["meta:grain"]);
    }
}
