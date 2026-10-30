using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 2)]
    [InlineData(16, 6)]
    public void CancellationReleasesReservationAndPreservesExactRefundAcrossRestart(int grainRoom, int timberRoom)
    {
        var context = Initial(1);
        var active = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var queueContext = Next(context, active);
        var queue = Handler.Execute(queueContext, "start", Json(new TrainingStart("slg:levy", 2)));
        var cancelContext = Next(queueContext, queue);
        var wallet = new Dictionary<string, int>(cancelContext.Account.State.Wallet)
        {
            ["meta:grain"] = estate.Config.WalletCaps["meta:grain"] - grainRoom,
            ["meta:timber"] = estate.Config.WalletCaps["meta:timber"] - timberRoom
        };
        cancelContext = cancelContext with { Account = cancelContext.Account with { State = cancelContext.Account.State with { Wallet = wallet } } };
        var preview = Handler.View(cancelContext).Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.Equal(16 - grainRoom, preview.WaitingCancellationRefunds.Single(x => x.MaterialId == "meta:grain").PendingAmount);
        var cancelled = Handler.Execute(cancelContext, "cancel-waiting", Json(new TrainingCancel(queueContext.CommandId!.Value)));
        Assert.True(cancelled.Done);
        Assert.Null(State(cancelled).Waiting);
        Assert.Equal(1, State(cancelled).Units.Sum(u => u.Reserved));
        Assert.Equal(estate.Config.WalletCaps["meta:grain"], cancelled.AccountState.Wallet["meta:grain"]);
        Assert.Equal(16 - grainRoom, State(cancelled).PendingRefunds.GetValueOrDefault("meta:grain"));
        Assert.Equal(6 - timberRoom, State(cancelled).PendingRefunds.GetValueOrDefault("meta:timber"));
        var repeatContext = Next(cancelContext, cancelled);
        var repeated = Handler.Execute(repeatContext, "cancel-waiting", Json(new TrainingCancel(queueContext.CommandId.Value)));
        Assert.False(repeated.Done);
        Assert.Equal(cancelled.State.GetRawText(), repeated.State.GetRawText());
        Assert.Equal(cancelled.AccountState.Wallet, repeated.AccountState.Wallet);

        // A fresh handler reads the serialized DB-domain shape; no in-memory job reference survives.
        var restarted = new TrainingHandler(estate, Content);
        var freeWallet = new Dictionary<string, int>(cancelled.AccountState.Wallet)
        {
            ["meta:grain"] = cancelled.AccountState.Wallet["meta:grain"] - 16,
            ["meta:timber"] = cancelled.AccountState.Wallet["meta:timber"] - 6
        };
        var reconnect = repeatContext with { Account = repeatContext.Account with { State = repeatContext.Account.State with { Wallet = freeWallet } } };
        var settled = restarted.Advance(reconnect);
        Assert.Empty(State(settled).PendingRefunds);
        Assert.Equal(estate.Config.WalletCaps["meta:grain"] - grainRoom, settled.AccountState.Wallet["meta:grain"]);
        Assert.Equal(estate.Config.WalletCaps["meta:timber"] - timberRoom, settled.AccountState.Wallet["meta:timber"]);
        Assert.Empty(restarted.Advance(Next(reconnect, settled)).Events);
    }

    [Fact]
    public void SpendingImmediatelyRefillsPendingBalancesAndRepeatedCancellationCannotGrowTheirBound()
    {
        var context = Initial(1);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        state = state with { PendingRefunds = new() { ["meta:grain"] = 80, ["meta:timber"] = 30 } };
        context = context with { State = Json(state) };
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.Equal(72, State(first).PendingRefunds["meta:grain"]);
        Assert.Equal(context.Account.State.Wallet, first.AccountState.Wallet);
        var queueContext = Next(context, first);
        var queued = Handler.Execute(queueContext, "start", Json(new TrainingStart("slg:levy", 10)));
        Assert.Empty(State(queued).PendingRefunds);
        var cancelled = Handler.Execute(Next(queueContext, queued), "cancel-waiting", Json(new TrainingCancel(queueContext.CommandId!.Value)));
        Assert.Equal(72, State(cancelled).PendingRefunds["meta:grain"]);
        Assert.Equal(27, State(cancelled).PendingRefunds["meta:timber"]);
    }

    [Fact]
    public void ExistingPendingRefundFillsWalletBeforeLaterProductionAndAfterUnrelatedSpending()
    {
        var context = Initial(1);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        state = state with { PendingRefunds = new() { ["meta:grain"] = 16 } };
        var wallet = new Dictionary<string, int>(context.Account.State.Wallet) { ["meta:grain"] = estate.Config.WalletCaps["meta:grain"] - 5 };
        context = context with { State = Json(state), Account = context.Account with { State = context.Account.State with { Wallet = wallet } } };
        var beforeClaim = Handler.Advance(context);
        Assert.Equal(estate.Config.WalletCaps["meta:grain"], beforeClaim.AccountState.Wallet["meta:grain"]);
        Assert.Equal(11, State(beforeClaim).PendingRefunds["meta:grain"]);
        var afterSpend = Next(context, beforeClaim);
        var spent = new Dictionary<string, int>(afterSpend.Account.State.Wallet) { ["meta:grain"] = estate.Config.WalletCaps["meta:grain"] - 4 };
        afterSpend = afterSpend with { Account = afterSpend.Account with { State = afterSpend.Account.State with { Wallet = spent } } };
        var result = Handler.AfterCommand(afterSpend);
        Assert.Equal(7, State(result).PendingRefunds["meta:grain"]);
        Assert.Equal(estate.Config.WalletCaps["meta:grain"], result.AccountState.Wallet["meta:grain"]);
    }

    [Theory]
    [InlineData("meta:grain", 81)]
    [InlineData("meta:iron", 1)]
    [InlineData("meta:grain", -1)]
    public void PendingBalanceCannotExceedOneUnlockedWaitingBatch(string material, int amount)
    {
        var context = Initial(1);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        context = context with { State = Json(state with { PendingRefunds = new() { [material] = amount } }) };
        Assert.Throws<InvalidDataException>(() => Handler.Advance(context));
    }
}
