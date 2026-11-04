using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    private static IReadOnlyList<DomainEffectSnapshot> ResearchTimeline(long completesAt)
    {
        var content = ResearchContent.Load((Environment.GetEnvironmentVariable("ACCOUNT_LOAD_CONTENT_ROOT") ?? ContentEstateTests.Root()));
        var node = content.Nodes.Single(n => n.Domain == "training.speedPercent");
        var state = new ResearchState(2, [new ResearchJob(1, node.Id, node.Level, 0, completesAt)],
            new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
        return new ResearchDomain(content).Effects(JsonSerializer.SerializeToElement(state, TrainingContent.Json));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(1001, true)]
    public void TrainingStartSnapshotsResearchOnlyAtItsCompletionBoundary(long startedAt, bool applied)
    {
        var context = Initial(2) with { UtcMilliseconds = startedAt, Effects = ResearchTimeline(1000) };
        var result = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var active = State(result).Active!;
        int percent = ResearchContent.Load((Environment.GetEnvironmentVariable("ACCOUNT_LOAD_CONTENT_ROOT") ?? ContentEstateTests.Root())).Nodes.Single(n => n.Domain == "training.speedPercent").Value;
        Assert.Equal(applied ? percent * 10 : 0, active.BonusPermille);
        Assert.Equal(startedAt + TrainingHandler.Duration(active.BaseDurationMilliseconds, active.BonusPermille!.Value, Content.Rules.MaximumBonusPermille), active.CompletesAtMilliseconds);
    }

    [Theory]
    [InlineData(40000, true)]
    [InlineData(41000, true)]
    [InlineData(42000, false)]
    public void OfflinePromotionUsesActualStartRatherThanLateReadTime(long researchAt, bool applied)
    {
        var context = Initial(2) with { Effects = ResearchTimeline(researchAt) };
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var queueContext = Next(context, first, 5000) with { Effects = context.Effects };
        var queued = Handler.Execute(queueContext, "start", Json(new TrainingStart("slg:guard", 1)));
        Assert.Null(State(queued).Waiting!.BonusPermille);
        var promoted = Handler.Advance(Next(queueContext, queued, 42000) with { Effects = context.Effects });
        var job = State(promoted).Active!;
        Assert.Equal(41000, job.StartedAtMilliseconds);
        int percent = ResearchContent.Load((Environment.GetEnvironmentVariable("ACCOUNT_LOAD_CONTENT_ROOT") ?? ContentEstateTests.Root())).Nodes.Single(n => n.Domain == "training.speedPercent").Value;
        Assert.Equal(applied ? percent * 10 : 0, job.BonusPermille);
        var late = Handler.Advance(Next(queueContext, queued, 86_400_000) with { Effects = context.Effects });
        Assert.Null(State(late).Active); Assert.Null(State(late).Waiting);
        Assert.Equal(2, late.Events.Length);
        Assert.Equal(job.CompletesAtMilliseconds, late.Events[1].Data.GetProperty("completedAtMilliseconds").GetInt64());
        Assert.Empty(Handler.Advance(Next(context, late, 86_400_000) with { Effects = context.Effects }).Events);
    }

    [Fact]
    public void ExistingActiveDeadlineDoesNotChangeWhenResearchCompletes()
    {
        var context = Initial(2);
        var started = Handler.Execute(context, "start", Json(new TrainingStart("slg:guard", 2)));
        var advanced = Handler.Advance(Next(context, started, 30000) with { Effects = ResearchTimeline(20000) });
        Assert.Equal(Json(State(started).Active).GetRawText(), Json(State(advanced).Active).GetRawText());
        Assert.Empty(advanced.Events);
    }

    [Fact]
    public void QuoteUsesSameScheduledStartSnapshotAsWaitingJobPromotion()
    {
        var context = Initial(2) with { Effects = ResearchTimeline(30000) };
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var next = Next(context, first, 5000) with { Effects = context.Effects };
        var view = Handler.View(next).Deserialize<TrainingView>(TrainingContent.Json)!;
        // Read wire fields to keep this assertion about the quote contract, not helper internals.
        var raw = JsonSerializer.SerializeToElement(view, TrainingContent.Json);
        var unit = raw.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("unitId").GetString() == "slg:guard");
        var quote = unit.GetProperty("quotes").EnumerateArray().Single(q => q.GetProperty("count").GetInt32() == 1);
        var queued = Handler.Execute(next, "start", Json(new TrainingStart("slg:guard", 1)));
        var promoted = Handler.Advance(Next(next, queued, 41000) with { Effects = next.Effects });
        Assert.Equal(State(promoted).Active!.CompletesAtMilliseconds, quote.GetProperty("estimatedCompletesAtMilliseconds").GetInt64());
    }
}
