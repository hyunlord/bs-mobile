using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;
public sealed class WaveRuntimeMetricTests
{
    [Fact]
    public void MultipleProjectilesResolveOneActivationOnlyAfterLastChild()
    {
        var (c, system, w, first) = WaveRuntimeTests.Arena(WaveAttackKind.Homing);
        var second = new EnemyState { Id = 11, Definition = first.Definition, Position = new(650, 500), Health = 100 }; w.Enemies.Add(second);
        system.Tick(w.Lord); var state = w.WaveRuntime!;
        Assert.Equal(1, state.Counters["activation-started"]); Assert.Equal(1, state.Counters["activation-pending"]); Assert.Equal(2, state.Projectiles.Count);
        first.Health = 0; w.Tick++; system.Tick(w.Lord); Assert.Equal(1, state.Counters["activation-pending"]); Assert.False(state.Counters.ContainsKey("empty-activation"));
        second.Health = 0; w.Tick++; system.Tick(w.Lord); Assert.Equal(0, state.Counters["activation-pending"]); Assert.Equal(1, state.Counters["empty-activation"]); Assert.Equal(1, state.Counters["activation-resolved"]);
    }
    [Fact]
    public void LateOrbitHitCountsSuccessAtExpiryNotAtLaunch()
    {
        var (_, system, w, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Orbit); enemy.Position = new(1000, 1000); system.Tick(w.Lord);
        var state = w.WaveRuntime!; Assert.Equal(1, state.Counters["activation-pending"]);
        enemy.Position = new(700, 700); w.Tick = 1; system.Tick(w.Lord);
        w.Equipment[0].ReadyTick = 100; w.Tick = 10; system.Tick(w.Lord);
        Assert.Equal(1, state.Counters["successful-activation"]); Assert.Equal(0, state.Counters["activation-pending"]); Assert.Empty(state.Activations);
    }
    [Fact]
    public void OrbitWithNoHitsResolvesEmptyOnceAndPendingIsSeparate()
    {
        var (_, system, w, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Orbit); enemy.Position = new(1000, 1000); system.Tick(w.Lord);
        var state = w.WaveRuntime!; Assert.False(state.Counters.ContainsKey("activation-resolved")); Assert.Equal(1, state.Counters["activation-pending"]);
        w.Equipment[0].ReadyTick = 100; w.Tick = 10; system.Tick(w.Lord); Assert.Equal(1, state.Counters["empty-activation"]); Assert.Equal(1, state.Counters["activation-resolved"]);
    }
    [Fact]
    public void ImmediateAttackTracksRequestedActualAndOverkillPerSource()
    {
        var (_, system, w, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc); enemy.Health = 3; system.Tick(w.Lord);
        var state = w.WaveRuntime!; var source = w.Equipment[0].Id;
        Assert.Equal(10, state.Counters["damage-requested:" + source]); Assert.Equal(3, state.Counters["damage-dealt:" + source]); Assert.Equal(7, state.Counters["overkill-waste:" + source]); Assert.Equal(1, state.Counters["successful-activation"]);
    }
    [Fact]
    public void ChildHitAndOtherChildLossStillResolveOneSuccess()
    {
        var state = new WaveRuntimeState(); var id = state.BeginActivation("test:weapon", 2, 100);
        state.RecordDamage(id, 10, 3); state.ResolveChild(id); Assert.Equal(1, state.Counters["activation-pending"]);
        state.ResolveChild(id); state.ResolveChild(id);
        Assert.Equal(1, state.Counters["activation-resolved"]); Assert.Equal(1, state.Counters["successful-activation"]); Assert.Equal(7, state.Counters["overkill-waste"]);
    }
    [Fact]
    public void ConstructionSlamDoesNotDamageBehindLord()
    {
        var (_, system, w, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.ConstructionSlam); enemy.Position = new(400, 500); system.Tick(w.Lord); Assert.Equal(100, enemy.Health);
    }
    [Fact]
    public void ShieldInterceptionResolvesTheActualProjectileAsEmpty()
    {
        var (c, _, w, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Homing);
        var rules = c.WaveRuntime!.Enemies.ToDictionary(p => p.Key, p => p.Value with { Kind = WaveEnemyKind.Shield });
        c = c with { WaveRuntime = c.WaveRuntime with { Enemies = rules } };
        var system = new WaveRuntimeSystem(c, w, new(1, true), null);
        system.Tick(w.Lord); w.Equipment[0].ReadyTick = 100;
        for (var tick = 1; tick <= 12; tick++) { w.Tick = tick; system.Tick(w.Lord); }
        Assert.Empty(w.WaveRuntime!.Projectiles); Assert.Equal(100, enemy.Health);
        Assert.Equal(1, w.WaveRuntime.Counters["empty-activation"]); Assert.Equal(0, w.WaveRuntime.Counters["activation-pending"]);
    }
    [Fact]
    public void LevelTwoDamageUsesAuthoredRowWithoutMultiplyingByLevel()
    {
        var (c, _, w, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var id = w.Equipment[0].Id;
        var gear = c.WaveRuntime!.Gear[id] with { Levels = new[] { new WaveGearLevel(10, 200, 10, 10, 1, 100, 0), new WaveGearLevel(17, 200, 10, 10, 1, 100, 0) } };
        c = c with { WaveRuntime = c.WaveRuntime with { Gear = new Dictionary<string, WaveGearDefinition> { { id, gear } } } };
        w.Equipment[0].Level = 2;
        var system = new WaveRuntimeSystem(c, w, new(1, true), null);
        system.Tick(w.Lord);
        Assert.Equal(83, enemy.Health);
        Assert.Equal(17, w.WaveRuntime!.Counters["damage-requested:" + id]);
    }
}
