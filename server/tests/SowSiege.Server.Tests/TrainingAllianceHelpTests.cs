using SowSiege.Server.Application;
using SowSiege.Server.Contracts;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Fact]
    public void HelpPreservesOriginalDurationAndPassesStoredStateValidation()
    {
        var context = Initial(2) with { Membership = new(Guid.NewGuid(), 1000) };
        var started = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 10)));
        var original = Assert.Single(Handler.HelpJobs(started.State));
        var rules = AllianceContent.Load((Environment.GetEnvironmentVariable("ACCOUNT_LOAD_CONTENT_ROOT") ?? ContentEstateTests.Root())).Help;
        long grant = AllianceRules.HelpShortening(rules, original.InitialDuration, original.CompletesAt - 1000, 0);
        Assert.True(grant > 0);
        var changed = Handler.ShortenHelpJob(started.State, original.Key, grant);
        var second = Assert.Single(Handler.HelpJobs(changed));
        Assert.Equal(original.InitialDuration, second.InitialDuration);
        Assert.Equal(original.CompletesAt - grant, second.CompletesAt);
        Assert.Equal(grant, second.HelpMilliseconds);
        Assert.Equal(context.Membership.AllianceId, second.StartAllianceId);
        var advanced = Handler.Advance(Next(context, started) with { State = changed, Membership = context.Membership });
        Assert.Empty(advanced.Events);
        Assert.Equal(State(started).Units, State(advanced).Units);
    }

    [Fact]
    public void HelpCompletionPromotesWaitingAtShortenedDeadlineExactlyOnce()
    {
        var context = Initial(2) with { Membership = new(Guid.NewGuid(), 1000) };
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 2)));
        var queueContext = Next(context, first, 5000) with { Membership = context.Membership };
        var queued = Handler.Execute(queueContext, "start", Json(new TrainingStart("slg:guard", 1)));
        var active = Assert.Single(Handler.HelpJobs(queued.State));
        var changed = Handler.ShortenHelpJob(queued.State, active.Key, 1000);
        var at = Next(queueContext, queued, active.CompletesAt - 1000) with { State = changed, Membership = context.Membership };
        var done = Handler.Advance(at);
        Assert.Single(done.Events);
        Assert.Equal(at.UtcMilliseconds, State(done).Active!.StartedAtMilliseconds);
        Assert.Equal(context.Membership.AllianceId, State(done).Active!.StartAllianceId);
        Assert.Equal(0, State(done).Active!.HelpMilliseconds);
        Assert.Null(State(done).Waiting);
        Assert.Empty(Handler.Advance(Next(at, done)).Events);
    }

    [Fact]
    public void OfflineActivationBeforeCurrentJoinCannotClaimCurrentAlliance()
    {
        var context = Initial(2);
        var first = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var queued = Handler.Execute(Next(context, first), "start", Json(new TrainingStart("slg:guard", 1)));
        var advanced = Handler.Advance(Next(context, queued, 22000) with { Membership = new(Guid.NewGuid(), 22000) });
        Assert.Null(State(advanced).Active!.StartAllianceId);
        Assert.Equal(21000, State(advanced).Active!.StartedAtMilliseconds);
    }

    [Fact]
    public void DeadlineTamperWithoutCorrespondingHelpSumIsRejected()
    {
        var context = Initial(2);
        var start = Handler.Execute(context, "start", Json(new TrainingStart("slg:levy", 1)));
        var state = State(start);
        var invalid = state with { Active = state.Active! with { CompletesAtMilliseconds = state.Active.CompletesAtMilliseconds - 1000 } };
        Assert.Throws<InvalidDataException>(() => Handler.Advance(Next(context, start) with { State = Json(invalid) }));
    }
}
