using System.Text.Json;
using SowSiege.Core;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed partial class TrainingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CombatLossesConserveTroopsAndUseLiveBedsInOrdinalOrder(bool deployed)
    {
        var context = Initial(2);
        var original = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        context = context with { State = Json(original with { Units = original.Units.Select(u =>
            u with { Ready = deployed ? 0 : 10, Deployed = deployed ? 10 : 0, Wounded = 1 }).ToArray() }) };
        WorldTroopResult[] casualties = [new("slg:levy", 10, 5), new("slg:guard", 10, 5)];
        var result = Handler.ApplyCombatLosses(context, casualties, 80, 5, deployed);
        Assert.True(result.Done);
        var units = State(result).Units;
        Assert.All(units, u => Assert.Equal(5, u.Ready));
        Assert.All(units, u => Assert.Equal(0, u.Deployed));
        Assert.Equal(4, units.Single(u => u.UnitId == "slg:guard").Wounded);
        Assert.Equal(1, units.Single(u => u.UnitId == "slg:levy").Wounded);
        Assert.Equal(15, units.Sum(u => u.Ready + u.Deployed + u.Wounded));
        Assert.False(Handler.ApplyCombatLosses(Next(context, result), casualties, 80, 5, deployed).Done);
    }

    [Fact]
    public void CombatCannotSpendOtherDeployedTroopsOrOverfillBeds()
    {
        var context = Initial(2);
        var original = context.State.Deserialize<TrainingState>(TrainingContent.Json)!;
        context = context with { State = Json(original with { Units = original.Units.Select(u =>
            u with { Ready = 2, Deployed = 10, Wounded = 4 }).ToArray() }) };
        var result = Handler.ApplyCombatLosses(context, [new("slg:levy", 5, 5)], 100, 2, true);
        Assert.True(result.Done);
        var levy = State(result).Units.Single(u => u.UnitId == "slg:levy");
        Assert.Equal(5, levy.Deployed);
        Assert.Equal(2, levy.Ready);
        Assert.Equal(4, levy.Wounded);
        Assert.False(Handler.ApplyCombatLosses(context, [new("slg:levy", 3, 1)], 80, 10, false).Done);
    }

    [Fact]
    public void EmptyDefenderRosterSettlesWithoutChangingTraining()
    {
        var context = Initial(2);
        var result = Handler.ApplyCombatLosses(context, [], 60, 0, false);
        Assert.True(result.Done);
        Assert.Equal(context.State.GetRawText(), result.State.GetRawText());
    }

    [Fact]
    public void InvalidCombatResultsLeaveTrainingUnchanged()
    {
        var context = Initial(2);
        foreach (WorldTroopResult[] invalid in new WorldTroopResult[][]
        {
            [new("unknown", 0, 0)], [new("slg:levy", 1, 2)],
            [new("slg:levy", 0, 0), new("slg:levy", 0, 0)]
        })
        {
            var result = Handler.ApplyCombatLosses(context, invalid, 80, 10, true);
            Assert.False(result.Done);
            Assert.Equal(context.State.GetRawText(), result.State.GetRawText());
        }
    }
}
