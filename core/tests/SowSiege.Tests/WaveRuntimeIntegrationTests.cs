using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;

public sealed class WaveRuntimeIntegrationTests
{
    private static InteractiveSession Session()
    {
        var c=ContentLoader.Load(Path.Combine(AppContext.BaseDirectory,"data"),false,"wave-1a");
        return FirstPlayableTests.Session(c);
    }
    [Fact]
    public void HiddenWorkReservationsAndCompletionHistoryChangeHash()
    {
        var s=Session();var w=s.Simulation.World.WaveRuntime!;
        var baseline=s.ComputeStateHash();w.Completed.Add("actual:cycle");Assert.NotEqual(baseline,s.ComputeStateHash());w.Completed.Clear();Assert.Equal(baseline,s.ComputeStateHash());
        w.CarriedWater++;Assert.NotEqual(baseline,s.ComputeStateHash());w.CarriedWater--;Assert.Equal(baseline,s.ComputeStateHash());
        w.Work.Add(new(){Id=1,Kind="grain",Source=s.Catalog.Tools.Keys.First(),Health=1});var original=s.ComputeStateHash();w.Work[0].DormantUntil=10;Assert.NotEqual(original,s.ComputeStateHash());
    }
    [Fact]
    public void EvolutionIsOfferedOnlyAfterActualHistoryAndRequiresSelection()
    {
        var s=Session();var c=s.Catalog;var w=s.Simulation.World;var recipe=c.WaveRuntime!.Evolutions.Values.Single(e=>e.Kind==WaveEvolutionKind.PlantingArc);
        w.Equipment.Clear();foreach(var id in recipe.InputIds)w.Equipment.Add(new(){Id=id});
        Assert.DoesNotContain(recipe.Id,s.Simulation.Wave!.ExtraCards());
        w.WaveRuntime!.BladePlotKill=true;Assert.Contains(recipe.Id,s.Simulation.Wave.ExtraCards());Assert.Empty(w.WaveRuntime.Evolutions);
        w.PendingCards=new[]{recipe.Id};s.Simulation.ChooseCard(recipe.Id);Assert.Contains(recipe.Id,w.WaveRuntime.Evolutions);
        s.Simulation.Tick();Assert.DoesNotContain(w.WaveRuntime.Events,e=>e.Kind=="attack"&&e.Source==recipe.InputIds[0]);Assert.Contains(w.WaveRuntime.Events,e=>e.Kind=="attack"&&e.Source==recipe.Id);
    }
    [Fact]
    public void ShelteredEvolutionSuppressesBothInputsAndCreatesLinkedWork()
    {
        var s=Session();var w=s.Simulation.World;var recipe=s.Catalog.WaveRuntime!.Evolutions.Values.Single(e=>e.Kind==WaveEvolutionKind.ShelteredPlot);
        w.Equipment.Clear();foreach(var id in recipe.InputIds)w.Equipment.Add(new(){Id=id});w.WaveRuntime!.HarvestNearBuilding=true;w.PendingCards=new[]{recipe.Id};s.Simulation.ChooseCard(recipe.Id);
        s.Simulation.Tick();Assert.DoesNotContain(w.WaveRuntime.Events,e=>e.Kind=="attack"&&recipe.InputIds.Contains(e.Source));Assert.Contains(w.WaveRuntime.Events,e=>e.Kind=="attack"&&e.Source==recipe.Id);
        var plot=Assert.Single(w.WaveRuntime.Work,e=>e.Kind=="grain");Assert.True(plot.ParentId>=0);Assert.False(plot.Complete);
    }
    [Fact]
    public void MovingOrbitHitsBetweenActivationsAndLeavesCenterUnharmed()
    {
        var s=Session();var c=s.Catalog;var w=s.Simulation.World;var gear=c.WaveRuntime!.Gear.Values.Single(g=>g.Kind==WaveAttackKind.Orbit);var enemy=c.WaveRuntime.Enemies.Values.First(e=>e.Kind==WaveEnemyKind.Pursuer);
        w.Equipment.Clear();w.Equipment.Add(new(){Id=gear.Id});var center=new EnemyState{Id=100,Definition=enemy.Id,Position=w.Lord,Health=1000};w.Enemies.Add(center);
        s.Simulation.Wave!.Tick(w.Lord);Assert.Equal(1000,center.Health);Assert.NotEmpty(w.WaveRuntime!.Attacks);
        w.Tick++;var fragment=w.WaveRuntime.Attacks[0];var edge=new EnemyState{Id=101,Definition=enemy.Id,Position=new(fragment.Position.X,fragment.Position.Y),Health=1000};w.Enemies.Add(edge);
        s.Simulation.Wave.Tick(w.Lord);Assert.True(edge.Health<1000);Assert.Equal(1000,center.Health);
    }
    [Fact]
    public void FrontOrbitItemDoesNotReshapeWithoutVisibleFrontalEnemy()
    {
        static WorldPoint[] Points(bool item, bool front)
        {
            var s=Session();var w=s.Simulation.World;var d=s.Catalog.WaveRuntime!;var gear=d.Gear.Values.Single(g=>g.Kind==WaveAttackKind.Orbit);
            w.Equipment.Clear();w.Equipment.Add(new(){Id=gear.Id});
            if(item)w.WaveRuntime!.Items.Add(d.Items.Values.Single(x=>x.Kind==WaveItemKind.FrontOrbit).Id);
            if(front){var e=d.Enemies.Values.First(x=>x.Kind==WaveEnemyKind.Pursuer);w.Enemies.Add(new(){Id=100,Definition=e.Id,Health=100,Position=new(w.Lord.X+gear.Range/2,w.Lord.Y)});}
            s.Simulation.Wave!.Tick(w.Lord);return w.WaveRuntime!.Attacks.Select(a=>a.Position).ToArray();
        }
        Assert.Equal(Points(false,false),Points(true,false));Assert.NotEqual(Points(false,true),Points(true,true));
    }
}
