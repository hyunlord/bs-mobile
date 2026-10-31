using System.Text.Json;
using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    private readonly EstateContent estate = EstateContent.Load(ContentEstateTests.Root());
    private TrainingContent Content => TrainingContent.Load(ContentEstateTests.Root(), estate);
    private TrainingHandler Handler => new(estate, Content);
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, TrainingContent.Json);
    private static TrainingState State(DomainResult result) => result.State.Deserialize<TrainingState>(TrainingContent.Json)!;

    private StoredAccount Account(int level, bool funded = true)
    {
        var account = new EstateApplication(estate).Create(1000);
        var map = account.State.Estate!;
        var state = account.State with
        {
            Estate = map with { Buildings = map.Buildings.Select(b => b.Kind == Content.Rules.BarracksKind ? b with { Level = level } : b).ToArray() },
            Wallet = funded ? account.State.Wallet.ToDictionary(x => x.Key, x => estate.Config.WalletCaps[x.Key]) : account.State.Wallet
        };
        return account with { State = state };
    }

    private static DomainContext Next(DomainContext previous, DomainResult result, long? now = null) =>
        new(previous.Account with { State = result.AccountState }, result.State, now ?? previous.UtcMilliseconds, Guid.NewGuid());

    private DomainContext Initial(int level, bool funded = true) => new(Account(level, funded), Handler.InitialState, 1000, Guid.NewGuid());

    [Theory]
    [InlineData(0, "slg:levy", 1, "barracks-required")]
    [InlineData(1, "slg:guard", 1, "unit-locked")]
    [InlineData(1, "bad", 1, "unknown-unit")]
    [InlineData(1, "slg:levy", 0, "invalid-count")]
    [InlineData(1, "slg:levy", -1, "invalid-count")]
    [InlineData(1, "slg:levy", 11, "batch-limit")]
    [InlineData(2, "slg:guard", 21, "batch-limit")]
    public void InvalidRequestsLeaveWalletAndReservationUnchanged(int level, string unit, int count, string reason)
    {
        var context = Initial(level);
        byte[] before = MetaSaveCodec.Encode(context.Account.State);
        var result = Handler.Execute(context, "start", Json(new TrainingStart(unit, count)));
        Assert.False(result.Done);
        Assert.Equal(reason, result.Reason);
        Assert.Equal(before, MetaSaveCodec.Encode(result.AccountState));
        Assert.Equal(context.State.GetRawText(), result.State.GetRawText());
        Assert.Empty(result.Events);
    }

    [Fact]
    public void CostsReserveOnceAndTwoBatchesFinishAtOriginalDeadlines()
    {
        var handler = Handler;
        var context = Initial(2);
        int grain = context.Account.State.Wallet["meta:grain"];
        var first = handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        Assert.True(first.Done);
        Assert.Equal(grain - 16, first.AccountState.Wallet["meta:grain"]);
        Assert.Equal(2, State(first).Units.Single(u => u.UnitId == "slg:levy").Reserved);
        Assert.Equal(41000, State(first).Active!.CompletesAtMilliseconds);
        var queuedContext = Next(context, first, 5000);
        var queued = handler.Execute(queuedContext, "start", Json(new TrainingStart("slg:guard", 1)));
        Assert.True(queued.Done);
        Assert.Null(State(queued).Waiting!.BonusPermille);
        Assert.Null(State(queued).Waiting!.StartedAtMilliseconds);
        var exact = handler.Advance(Next(queuedContext, queued, 41000));
        Assert.Equal(2, State(exact).Units.Single(u => u.UnitId == "slg:levy").Ready);
        Assert.Equal(41000, State(exact).Active!.StartedAtMilliseconds);
        Assert.Equal(81000, State(exact).Active!.CompletesAtMilliseconds);
        Assert.Null(State(exact).Waiting);
        var lateContext = Next(queuedContext, queued, 86_400_000);
        var late = handler.Advance(lateContext);
        Assert.Null(State(late).Active);
        Assert.Null(State(late).Waiting);
        Assert.Equal(3, State(late).Units.Sum(u => u.Ready));
        Assert.Equal(0, State(late).Units.Sum(u => u.Reserved));
        Assert.Equal(2, late.Events.Length);
        Assert.Equal(new[] { context.CommandId, queuedContext.CommandId }, late.Events.Select(e => (Guid?)e.Data.GetProperty("jobId").GetGuid()));
        var repeated = handler.Advance(Next(lateContext, late));
        Assert.Equal(late.State.GetRawText(), repeated.State.GetRawText());
        Assert.Empty(repeated.Events);
        Assert.Equal(MetaSaveCodec.Encode(queued.AccountState), MetaSaveCodec.Encode(late.AccountState));
    }

    [Fact]
    public void CompletionBoundaryIsInclusiveAndBeforeItDoesNotCredit()
    {
        var context = Initial(1);
        var start = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var before = Handler.Advance(Next(context, start, 20999));
        Assert.NotNull(State(before).Active);
        Assert.Empty(before.Events);
        var at = Handler.Advance(Next(context, start, 21000));
        Assert.Null(State(at).Active);
        Assert.Single(at.Events);
    }

    [Fact]
    public void ActiveCannotCancelAndWaitingRefundUsesChargedCostExactly()
    {
        var context = Initial(2);
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var queuedContext = Next(context, first);
        var queued = Handler.Execute(queuedContext, "start", Json(new TrainingStart("slg:guard", 2)));
        var cancelContext = Next(queuedContext, queued);
        var activeCancel = Handler.Execute(cancelContext, "cancel-waiting", Json(new TrainingCancel(context.CommandId!.Value)));
        Assert.False(activeCancel.Done);
        Assert.Equal("unknown-waiting-job", activeCancel.Reason);
        var cancel = Handler.Execute(cancelContext, "cancel-waiting", Json(new TrainingCancel(queuedContext.CommandId!.Value)));
        Assert.True(cancel.Done);
        // The charged cost returns as shared-writer credits; wallet plus credits restores the pre-queue balance exactly.
        Assert.Equal(queued.AccountState.Wallet, cancel.AccountState.Wallet);
        var credited = cancel.Credits!.ToDictionary(c => c.Material, c => c.Amount);
        Assert.Equal(State(queued).Waiting!.ChargedCost.OrderBy(c => c.Key), credited.OrderBy(c => c.Key));
        Assert.Equal(first.AccountState.Wallet, cancel.AccountState.Wallet.ToDictionary(p => p.Key, p => p.Value + credited.GetValueOrDefault(p.Key)));
        Assert.Null(State(cancel).Waiting);
        Assert.Equal(1, State(cancel).Units.Sum(u => u.Reserved));
        var again = Handler.Execute(Next(cancelContext, cancel), "cancel-waiting", Json(new TrainingCancel(queuedContext.CommandId.Value)));
        Assert.False(again.Done);
        Assert.Equal(cancel.AccountState.Wallet, again.AccountState.Wallet);
    }

    [Fact]
    public void PendingAndDeployedAndWoundedAllConsumeCapacity()
    {
        var context = Initial(1);
        var initial = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        initial = initial with { Units = initial.Units.Select(u => u.UnitId == "slg:levy" ? u with { Ready = 20, Deployed = 20, Wounded = 19 } : u).ToArray() };
        context = context with { State = Json(initial) };
        var full = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        Assert.False(full.Done);
        Assert.Equal("capacity", full.Reason);
        var last = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.True(last.Done);
        var after = Handler.Execute(Next(context, last), "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.Equal("capacity", after.Reason);
        var finished = Handler.Advance(Next(context, last, 21000));
        Assert.Equal(60, finished.View.GetProperty("occupied").GetInt32());
    }

    [Fact]
    public void FullQueueAndInsufficientResourcesDoNotSpend()
    {
        var context = Initial(1);
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var second = Handler.Execute(Next(context, first), "start", Json(new TrainingStart("slg:levy", 1)));
        var third = Handler.Execute(Next(context, second), "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.Equal("queue-full", third.Reason);
        Assert.Equal(second.AccountState.Wallet, third.AccountState.Wallet);
        var empty = context.Account.State with { Wallet = context.Account.State.Wallet.ToDictionary(x => x.Key, _ => 0) };
        var denied = Handler.Execute(context with { Account = context.Account with { State = empty } }, "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.Equal("insufficient-resources", denied.Reason);
        Assert.All(denied.AccountState.Wallet.Values, amount => Assert.Equal(0, amount));
    }

    [Theory]
    [InlineData("{\"unitId\":\"slg:levy\",\"count\":1,\"bonusPermille\":300}")]
    [InlineData("{\"unitId\":\"slg:levy\"}")]
    [InlineData("{\"unitId\":\"slg:levy\",\"count\":1,\"count\":2}")]
    [InlineData("{\"unitId\":\"slg:levy\",\"count\":1,\"Count\":2}")]
    [InlineData("{\"unitId\":null,\"count\":1}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"start\"")]
    public void UnknownMissingDuplicateOrNullPayloadCannotInjectTrainingState(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var result = Handler.Execute(Initial(1), "start", json.RootElement);
        Assert.False(result.Done);
        Assert.Equal("invalid-payload", result.Reason);
    }

    [Fact]
    public void DurationUsesCeilingAndServerCap()
    {
        Assert.Equal(16000, TrainingHandler.Duration(20000, 300, 300));
        Assert.Equal(16000, TrainingHandler.Duration(20000, 900, 300));
        Assert.Equal(20000, TrainingHandler.Duration(20000, 0, 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => TrainingHandler.Duration(20000, -1, 300));
    }

    [Fact]
    public void ReservationMismatchFailsClosed()
    {
        var context = Initial(1);
        var invalid = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        invalid = invalid with { Units = invalid.Units.Select(u => u with { Reserved = 1 }).ToArray() };
        Assert.Throws<InvalidDataException>(() => Handler.Advance(context with { State = Json(invalid) }));
    }

    [Fact]
    public void TroopOccupancyAboveCompletedCapacityFailsClosed()
    {
        var context = Initial(1);
        var invalid = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        invalid = invalid with { Units = invalid.Units.Select(u => u.UnitId == "slg:levy" ? u with { Ready = 61 } : u).ToArray() };
        Assert.Throws<InvalidDataException>(() => Handler.Advance(context with { State = Json(invalid) }));
    }

    [Fact]
    public void TrainingDoesNotSpendTheExistingProtectedProductionReserve()
    {
        var context = Initial(1);
        var reserve = AccountEstate.ProtectedReserve(estate.Config, context.Account.State);
        Assert.NotEmpty(reserve);
        var wallet = new Dictionary<string, int>(context.Account.State.Wallet);
        foreach (var material in reserve) { wallet[material.Key] = material.Value; }
        context = context with { Account = context.Account with { State = context.Account.State with { Wallet = wallet } } };
        var result = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.False(result.Done);
        Assert.Equal("protected-reserve", result.Reason);
        Assert.Equal(wallet, result.AccountState.Wallet);
        Assert.Equal(context.State.GetRawText(), result.State.GetRawText());
    }

    [Fact]
    public void ContentHashPinsSeparateTrainingValuesAndRejectsUnknownMaterial()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(ContentEstateTests.Root(), "data", "opt-in", "slg-training-v1.json"));
        var source = JsonNode.Parse(bytes)!;
        source["units"]![0]!["secondsPerUnit"] = 21;
        var modified = TrainingContent.Parse(System.Text.Encoding.UTF8.GetBytes(source.ToJsonString()), estate);
        Assert.NotEqual(Content.Hash, modified.Hash);
        Assert.Equal(estate.Hash, EstateContent.Load(ContentEstateTests.Root()).Hash);
        source["units"]![0]!["costPerUnit"]!["unknown"] = 1;
        Assert.Throws<InvalidDataException>(() => TrainingContent.Parse(System.Text.Encoding.UTF8.GetBytes(source.ToJsonString()), estate));
    }
}
