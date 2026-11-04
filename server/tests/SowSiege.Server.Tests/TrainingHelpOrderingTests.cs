using System.Text.Json;
using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Fact]
    public void SameUtcHelpCannotRetroactivelyChangeAlreadyPromotedTraining()
    {
        var context = Initial(2);
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var queuedContext = Next(context, first);
        var queued = Handler.Execute(queuedContext, "start", Json(new TrainingStart("slg:guard", 1)));
        long boundary = State(queued).Active!.CompletesAtMilliseconds!.Value;
        var beforeHelp = ResearchTimeline(boundary + 1000);
        var natural = Handler.Advance(Next(queuedContext, queued, boundary) with { Effects = beforeHelp });
        Assert.Equal(boundary, State(natural).Active!.StartedAtMilliseconds);
        Assert.Equal(0, State(natural).Active!.BonusPermille);
        // Help moves the research completion to T only after natural settlement at T.
        var afterHelp = ResearchTimeline(boundary);
        var after = Handler.Advance(Next(context, natural, boundary) with { Effects = afterHelp });
        Assert.Equal(Json(State(natural)).GetRawText(), Json(State(after)).GetRawText());
        Assert.Empty(after.Events);
    }

    [Fact]
    public void NewTrainingAfterSameUtcResearchHelpReceivesCompletedEffect()
    {
        var content = ResearchContent.Load(ContentEstateTests.Root());
        var node = content.Nodes.Single(n => n.Domain == "training.speedPercent");
        var research = new ResearchDomain(content);
        var state = new ResearchState(2, [new ResearchJob(1, node.Id, node.Level, 0, 2000)],
            new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
        var shortened = research.ShortenHelpJob(Json(state), "research:1", 1000);
        var context = Initial(2);
        var completed = research.Advance(context with { State = shortened });
        var start = Handler.Execute(context with { Effects = research.Effects(completed.State) }, "start", Json(new TrainingStart("slg:levy", 1)));
        Assert.Equal(node.Value * 10, State(start).Active!.BonusPermille);
        Assert.Empty(completed.State.Deserialize<ResearchState>(TrainingContent.Json)!.Active);
    }
}
