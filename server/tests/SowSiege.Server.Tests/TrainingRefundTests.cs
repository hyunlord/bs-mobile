using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Fact]
    public void ExistingVersionOneStateWithoutRefundFieldPreservesTroopsAndJobs()
    {
        var context = Initial(1);
        var active = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var persisted = State(active);
        persisted = persisted with { Units = persisted.Units.Select(u => u.UnitId == "slg:levy" ? u with { Ready = 1 } : u).ToArray() };
        var legacy = System.Text.Json.Nodes.JsonNode.Parse(Json(persisted).GetRawText())!.AsObject();
        Assert.True(legacy.Remove("pendingRefunds"));
        using var document = JsonDocument.Parse(legacy.ToJsonString());
        var reopened = Handler.Advance(Next(context, active) with { State = document.RootElement });
        Assert.Equal(1, Handler.StateVersion);
        Assert.Empty(State(reopened).PendingRefunds);
        Assert.Equal(persisted.Units, State(reopened).Units);
        Assert.Equal(persisted.Active!.JobId, State(reopened).Active!.JobId);
        Assert.Equal(persisted.Active.CompletesAtMilliseconds, State(reopened).Active!.CompletesAtMilliseconds);
        Assert.Null(State(reopened).Waiting);
        Assert.Equal(active.AccountState.Wallet, reopened.AccountState.Wallet);
        Assert.Empty(reopened.Events);
        Assert.Empty(reopened.Credits ?? []);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 2)]
    [InlineData(16, 6)]
    public void CancellationReleasesReservationAndCreditsTheExactChargedCost(int grainRoom, int timberRoom)
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
        Assert.Equal((grainRoom, 16 - grainRoom), Split(preview, "meta:grain"));
        Assert.Equal((timberRoom, 6 - timberRoom), Split(preview, "meta:timber"));
        var cancelled = Handler.Execute(cancelContext, "cancel-waiting", Json(new TrainingCancel(queueContext.CommandId!.Value)));
        Assert.True(cancelled.Done);
        Assert.Null(State(cancelled).Waiting);
        Assert.Equal(1, State(cancelled).Units.Sum(u => u.Reserved));
        // The domain hands the whole charged cost to the shared writer; it does not touch the wallet itself.
        Assert.Equal(wallet, cancelled.AccountState.Wallet);
        Assert.Equal(new[] { new DomainCredit("meta:grain", 16), new DomainCredit("meta:timber", 6) }, cancelled.Credits);
        Assert.Empty(State(cancelled).PendingRefunds);
        var repeatContext = Next(cancelContext, cancelled);
        var repeated = Handler.Execute(repeatContext, "cancel-waiting", Json(new TrainingCancel(queueContext.CommandId.Value)));
        Assert.False(repeated.Done);
        Assert.Empty(repeated.Credits ?? []);
        Assert.Equal(cancelled.State.GetRawText(), repeated.State.GetRawText());
    }

    [Fact]
    public void RefundPreviewCountsThisDomainsHeldMaterialAsTaken()
    {
        var context = Initial(1);
        var active = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var queueContext = Next(context, active);
        var queue = Handler.Execute(queueContext, "start", Json(new TrainingStart("slg:levy", 2)));
        var wallet = new Dictionary<string, int>(queue.AccountState.Wallet) { ["meta:grain"] = estate.Config.WalletCaps["meta:grain"] - 10 };
        var cancelContext = Next(queueContext, queue);
        cancelContext = cancelContext with
        {
            Account = cancelContext.Account with { State = cancelContext.Account.State with { Wallet = wallet } },
            Held = new Dictionary<string, int> { ["meta:grain"] = 4 }
        };
        var preview = Handler.View(cancelContext).Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.Equal((6, 10), Split(preview, "meta:grain"));
    }

    [Fact]
    public void LegacyPendingRefundsMoveToTheSharedHeldQueueOnce()
    {
        var context = Initial(1);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        state = state with { PendingRefunds = new() { ["meta:grain"] = 16, ["meta:timber"] = 6 } };
        context = context with { State = Json(state) };
        var moved = Handler.Advance(context);
        Assert.Equal(new[] { new DomainCredit("meta:grain", 16), new DomainCredit("meta:timber", 6) }, moved.Credits);
        Assert.Empty(State(moved).PendingRefunds);
        Assert.Equal(context.Account.State.Wallet, moved.AccountState.Wallet);
        Assert.Contains(moved.Events, e => e.Kind == "training-refunds-moved-to-held");
        var again = Handler.Advance(Next(context, moved));
        Assert.Empty(again.Credits ?? []);
        Assert.Empty(again.Events);
    }

    [Theory]
    [InlineData("meta:grain", 81)]
    [InlineData("meta:iron", 1)]
    [InlineData("meta:grain", -1)]
    public void LegacyPendingBalanceCannotExceedOneUnlockedWaitingBatch(string material, int amount)
    {
        var context = Initial(1);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        context = context with { State = Json(state with { PendingRefunds = new() { [material] = amount } }) };
        Assert.Throws<InvalidDataException>(() => Handler.Advance(context));
    }

    private static (int Immediate, int Pending) Split(TrainingView view, string material)
    {
        var row = view.WaitingCancellationRefunds.Single(x => x.MaterialId == material);
        return (row.ImmediateAmount, row.PendingAmount);
    }
}
