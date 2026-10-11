using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class RuntimeDiagnosticsTests
{
    [Fact]
    public void CapabilityMatchesCompiledFineStageVariant()
    {
#if SOWSIEGE_FINE_DIAGNOSTICS
        Assert.True(RuntimeDiagnosticsCapabilities.FineStagesEnabled);
#else
        Assert.False(RuntimeDiagnosticsCapabilities.FineStagesEnabled);
#endif
    }

    private sealed class Observer : IRuntimeDiagnostics
    {
        internal readonly Stack<RuntimeStage> Active = new();
        internal readonly List<(RuntimeStage? Parent, RuntimeStage Stage)> Calls = new();
        public void Begin(RuntimeStage stage)
        {
            Calls.Add((Active.Count == 0 ? null : Active.Peek(), stage));
            Active.Push(stage);
        }
        public void End(RuntimeStage stage) => Assert.Equal(stage, Active.Pop());
    }

    [Fact]
    public void AppendedStagesPreserveIndexesAndObservedTickState()
    {
        Assert.Equal(new[] { RuntimeStage.Enemy, RuntimeStage.Separation, RuntimeStage.Residual, RuntimeStage.Work,
            RuntimeStage.Attack, RuntimeStage.Movement, RuntimeStage.Collect, RuntimeStage.Harvest, RuntimeStage.Spawn, RuntimeStage.Clear },
            Enumerable.Range(0, 10).Select(value => (RuntimeStage)value));
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
        InteractiveSession Create()
        {
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64)));
            var world = session.Simulation.World;
            var enemyId = catalog.WaveRuntime!.EnemySeparation!.BodyWidths.Keys.First();
            var crowdedPosition = new Position(world.Lord.X + 1000, world.Lord.Y);
            world.Enemies.Add(new() { Id = world.NextId++, Definition = enemyId, Position = crowdedPosition, Health = 1000 });
            world.Enemies.Add(new() { Id = world.NextId++, Definition = enemyId, Position = crowdedPosition, Health = 1000 });
            world.WaveRuntime!.Work.Add(new() { Id = world.NextId++, Source = "core:seed_bag", Kind = "grain", Position = world.Lord, Complete = true, Health = 100 });
            return session;
        }
        var original = Create(); var observed = Create(); var observer = new Observer();
        Assert.Null(original.Diagnostics);
        observed.Diagnostics = observer;
        Assert.Equal(original.ComputeStateHash(), observed.ComputeStateHash());
        var command = new ReplayCommand(0, 0, ReplayCommandKind.Advance);
        original.Apply(command); observed.Apply(command);
        Assert.Equal(original.ComputeStateHash(), observed.ComputeStateHash());
        Assert.Empty(observer.Active);
        Assert.Contains(((RuntimeStage?)RuntimeStage.Enemy, RuntimeStage.Separation), observer.Calls);
        Assert.Contains(((RuntimeStage?)RuntimeStage.Separation, RuntimeStage.Residual), observer.Calls);
#if SOWSIEGE_FINE_DIAGNOSTICS
        Assert.Contains(((RuntimeStage?)RuntimeStage.Enemy, RuntimeStage.Spawn), observer.Calls);
        Assert.Contains(((RuntimeStage?)RuntimeStage.Separation, RuntimeStage.Collect), observer.Calls);
        Assert.Contains(((RuntimeStage?)RuntimeStage.Residual, RuntimeStage.Clear), observer.Calls);
        Assert.Contains(((RuntimeStage?)RuntimeStage.Work, RuntimeStage.Harvest), observer.Calls);
#else
        Assert.DoesNotContain(observer.Calls, call => call.Stage is RuntimeStage.Collect or RuntimeStage.Harvest or RuntimeStage.Spawn or RuntimeStage.Clear);
#endif
        Assert.Equal(1, observed.Simulation.World.Harvests);
    }
}
