using System.Text.Json;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void DeploymentRejectsInvalidOrUnavailableReadyCount(int count)
    {
        var context = Initial(2);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        state = state with { Units = state.Units.Select(u => u with { Ready = 10, Wounded = 2 }).ToArray() };
        context = context with { State = Json(state) };
        var deployed = Handler.Deploy(context, [new("slg:levy", count)], 40);
        Assert.False(deployed.Done);
        Assert.Equal(context.State.GetRawText(), deployed.State.GetRawText());
    }

    [Fact]
    public void DeploymentAndSafeReturnConserveAllOccupancyAndDoNotSpendWounded()
    {
        var context = Initial(2);
        var state = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        state = state with { Units = state.Units.Select(u => u with { Ready = 10, Wounded = 3 }).ToArray() };
        context = context with { State = Json(state) };
        var deployment = new MarchTroops[] { new("slg:levy", 8), new("slg:guard", 4) };
        var deployed = Handler.Deploy(context, deployment, 40);
        Assert.True(deployed.Done);
        Assert.Equal(12, State(deployed).Units.Sum(u => u.Deployed));
        Assert.Equal(6, State(deployed).Units.Sum(u => u.Wounded));
        var returned = Handler.ReturnUnharmed(Next(context, deployed), deployment);
        Assert.True(returned.Done);
        Assert.Equal(state.Units, State(returned).Units);
        Assert.False(Handler.ReturnUnharmed(Next(context, returned), deployment).Done);
    }

    [Fact]
    public void DuplicateUnitAndUnknownUnitCannotDeploy()
    {
        var context = Initial(2);
        Assert.False(Handler.Deploy(context, [new("slg:levy", 1), new("slg:levy", 1)], 40).Done);
        Assert.False(Handler.Deploy(context, [new("unknown", 1)], 40).Done);
        Assert.False(Handler.Deploy(context, [], 40).Done);
    }

    [Fact]
    public void TrainingCompletionMustBeAdvancedBeforeExactDeadlineDeparture()
    {
        var context = Initial(1);
        var started = Handler.Execute(context, "start", Json(new SowSiege.Server.Contracts.TrainingStart("slg:levy", 1)));
        var before = Next(context, started, 20999);
        Assert.False(Handler.Deploy(before, [new("slg:levy", 1)], 20).Done);
        var completed = Handler.Advance(before with { UtcMilliseconds = 21000 });
        Assert.True(Handler.Deploy(Next(context, completed, 21000), [new("slg:levy", 1)], 20).Done);
    }
}
