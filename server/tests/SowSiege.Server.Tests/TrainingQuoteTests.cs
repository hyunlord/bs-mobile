using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Fact]
    public void QuotesMatchActualChargedCostAndServerDeadlineForEveryUnlockedBatch()
    {
        var context = Initial(2);
        var view = Handler.View(context).Deserialize<TrainingView>(TrainingContent.Json)!;
        foreach (var unit in view.Units)
        {
            Assert.Equal(view.BatchLimit, unit.Quotes.Length);
            foreach (var quote in unit.Quotes)
            {
                Assert.True(quote.CanStart, quote.Reason);
                var started = Handler.Execute(context, "start", Json(new TrainingStart(unit.UnitId, quote.Count)));
                Assert.True(started.Done, started.Reason);
                var job = State(started).Active!;
                Assert.Equal(quote.TotalCost.ToDictionary(c => c.MaterialId, c => c.Amount), job.ChargedCost);
                Assert.Equal(quote.EstimatedCompletesAtMilliseconds, job.CompletesAtMilliseconds);
                Assert.Equal(quote.DurationMilliseconds, job.CompletesAtMilliseconds - job.StartedAtMilliseconds);
                Assert.Equal(quote.OccupiedAfter, State(started).Units.Sum(u => u.Ready + u.Deployed + u.Wounded + u.Reserved));
                Assert.Equal(view.Capacity, quote.Capacity);
            }
        }
    }

    [Fact]
    public void WaitingQuoteUsesServerActiveDeadlineAndExistingQueueHasNoEstimatedCompletion()
    {
        var context = Initial(1);
        var started = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var queuedContext = Next(context, started, 5000);
        var quote = Handler.View(queuedContext).Deserialize<TrainingView>(TrainingContent.Json)!.Units
            .Single(u => u.UnitId == "slg:levy").Quotes.Single(q => q.Count == 3);
        Assert.Equal(60000, quote.DurationMilliseconds);
        Assert.Equal(101000, quote.EstimatedCompletesAtMilliseconds);
        Assert.Equal(5, quote.OccupiedAfter);
        var queued = Handler.Execute(queuedContext, "start", Json(new TrainingStart("slg:levy", 3)));
        var full = Handler.View(Next(queuedContext, queued)).Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.All(full.Units.Single(u => u.UnitId == "slg:levy").Quotes, q =>
        {
            Assert.False(q.CanStart);
            Assert.Equal("queue-full", q.Reason);
            Assert.Null(q.EstimatedCompletesAtMilliseconds);
        });
    }

    [Theory]
    [InlineData("unit-locked")]
    [InlineData("insufficient-resources")]
    [InlineData("protected-reserve")]
    [InlineData("capacity")]
    public void QuoteAndExecutionShareEveryResourceAndOccupancyRejection(string reason)
    {
        var context = Initial(1);
        string unit = reason == "unit-locked" ? "slg:guard" : "slg:levy";
        if (reason == "insufficient-resources")
        {
            context = context with { Account = context.Account with { State = context.Account.State with
            { Wallet = context.Account.State.Wallet.ToDictionary(p => p.Key, _ => 0) } } };
        }
        if (reason == "protected-reserve")
        {
            var reserve = SowSiege.Core.AccountEstate.ProtectedReserve(estate.Config, context.Account.State);
            var wallet = new Dictionary<string, int>(context.Account.State.Wallet);
            foreach (var pair in reserve) { wallet[pair.Key] = pair.Value; }
            context = context with { Account = context.Account with { State = context.Account.State with { Wallet = wallet } } };
        }
        if (reason == "capacity")
        {
            var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
            context = context with { State = Json(state with { Units = state.Units.Select(u => u.UnitId == unit ? u with { Ready = 60 } : u).ToArray() }) };
        }
        var quote = Handler.View(context).Deserialize<TrainingView>(TrainingContent.Json)!.Units.Single(u => u.UnitId == unit).Quotes[0];
        var command = Handler.Execute(context, "start", Json(new TrainingStart(unit, quote.Count)));
        Assert.False(quote.CanStart);
        Assert.False(command.Done);
        Assert.Equal(reason, quote.Reason);
        Assert.Equal(command.Reason, quote.Reason);
        Assert.Null(quote.EstimatedCompletesAtMilliseconds);
    }

    [Fact]
    public void UnbuiltBarracksHasNoSelectableQuotes()
    {
        var view = Handler.View(Initial(0)).Deserialize<TrainingView>(TrainingContent.Json)!;
        Assert.All(view.Units, unit => Assert.Empty(unit.Quotes));
    }
}
