using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;

public sealed class InteractiveMechanicTests
{
    [Fact]
    public void AimUsesNearestEnemyThenStableIdAndMovementClampsToMap()
    {
        var s = InteractiveTests.Session(); var w = s.Simulation.World; var c = s.Catalog; var definition = c.Enemies.Keys.First();
        s.Apply(new(0, 0, ReplayCommandKind.SetSpawnPermille, Value: 0));
        s.Apply(new(1, 0, ReplayCommandKind.SetAimMode, Value: (int)AimMode.NearestEnemy));
        w.Enemies.Add(new() { Id = 99, Definition = definition, Position = new(w.Lord.X + 200, w.Lord.Y), Health = 100000 });
        w.Enemies.Add(new() { Id = 98, Definition = definition, Position = new(w.Lord.X - 200, w.Lord.Y), Health = 100000 });
        foreach (var e in w.Equipment) { e.ReadyTick = int.MaxValue; }
        s.Apply(new(2, 0, ReplayCommandKind.Advance));
        Assert.True(s.View.CaptureFrame().Lord.Facing.X < 0);
        w.Lord = new(0, 0); s.Apply(new(3, 1, ReplayCommandKind.Advance, new(-1000, -1000)));
        Assert.Equal(0, w.Lord.X); Assert.Equal(0, w.Lord.Y);
        s.Apply(new(4, 2, ReplayCommandKind.Advance, new(1000, 1000)));
        Assert.Equal(c.Tuning.World.Map.LordSpeed, w.Lord.X); Assert.Equal(w.Lord.X, w.Lord.Y);
    }
    [Fact]
    public void InvulnerabilityPreventsDamageAndDisablingItAllowsDeath()
    {
        var s = InteractiveTests.Session(); var w = s.Simulation.World; var c = s.Catalog;
        var definition = c.Enemies.Values.First(e => e.Target == "lord");
        w.People.Clear(); w.LordHealth = 1; foreach (var e in w.Equipment) { e.ReadyTick = int.MaxValue; }
        w.Enemies.Add(new() { Id = 999, Definition = definition.Id, Position = w.Lord, Health = 100000 });
        s.Apply(new(0, 0, ReplayCommandKind.SetSpawnPermille, Value: 0)); s.Apply(new(1, 0, ReplayCommandKind.SetInvulnerable, Value: 1)); s.Apply(new(2, 0, ReplayCommandKind.Advance));
        Assert.Equal(1, w.LordHealth);
        s.Apply(new(3, 1, ReplayCommandKind.SetInvulnerable, Value: 0)); w.Enemies[0].AttackTick = 0;
        s.Apply(new(4, 1, ReplayCommandKind.Advance)); Assert.Equal(RunStatus.Completed, s.View.Status); Assert.Equal("death", s.GetSummary().EndReason);
    }
    [Fact]
    public void AttackMissesAreVisibleAndReadingFramesDoesNotConsumeEvents()
    {
        var s = InteractiveTests.Session(); s.Apply(new(0, 0, ReplayCommandKind.SetSpawnPermille, Value: 0)); s.Apply(new(1, 0, ReplayCommandKind.Advance));
        var first = s.View.CaptureFrame(); var hash = s.ComputeStateHash(); var second = s.View.CaptureFrame();
        Assert.Contains(first.Events, e => e.Kind == PresentationKind.Attack && e.HitEntityIds.Count == 0);
        Assert.Equal(first.Events.Select(e => e.Id), second.Events.Select(e => e.Id)); Assert.Equal(hash, s.ComputeStateHash());
    }
    [Fact]
    public void HiddenRuntimeStateAndListOrderAreBoundByFullHash()
    {
        var s = InteractiveTests.Session(); var w = s.Simulation.World; var hash = s.ComputeStateHash();
        w.Runtime!.Entities[999] = new() { ShieldUntil = 100 }; Assert.NotEqual(hash, s.ComputeStateHash());
        hash = s.ComputeStateHash(); w.Runtime.Entities[999].ShieldUntil++; Assert.NotEqual(hash, s.ComputeStateHash());
        hash = s.ComputeStateHash(); w.Equipment.Reverse(); Assert.NotEqual(hash, s.ComputeStateHash());
        hash = s.ComputeStateHash(); w.Remains!.FarmCredits[999] = 100; Assert.NotEqual(hash, s.ComputeStateHash());
    }
    [Fact]
    public void RayMissHasCoreAuthoredGeometryAndToolHitPositionPrecedesKnockback()
    {
        var s = InteractiveTests.Session(); var w = s.Simulation.World; var c = s.Catalog;
        var ray = c.WeaponCombat!.Weapons.First(pair => pair.Value.AttackModel == "rays");
        w.Equipment.Clear(); w.Equipment.Add(new() { Id = ray.Key });
        s.Apply(new(0, 0, ReplayCommandKind.SetSpawnPermille, Value: 0)); s.Apply(new(1, 0, ReplayCommandKind.Advance));
        var e = s.View.CaptureFrame().Events.Single(e => e.SourceId == ray.Key);
        Assert.NotNull(e.Geometry); Assert.Single(e.Geometry.RayEnds); Assert.Equal(ray.Value.BeamHalfWidth, e.Geometry.BeamHalfWidth);
        Assert.True(e.Geometry.RayEnds[0].X > e.Origin.X); Assert.Empty(e.HitEntityIds);
        var tool = c.Tools.Values.First(t => t.Activation.Knockback > 0); w.Equipment.Clear(); w.Equipment.Add(new() { Id = tool.Id });
        var enemy = c.Enemies.Values.First(); w.Enemies.Add(new() { Id = 999, Definition = enemy.Id, Position = new(w.Lord.X + tool.Activation.Range - 1, w.Lord.Y), Health = 100000 });
        s.Apply(new(2, 1, ReplayCommandKind.Advance));
        e = s.View.CaptureFrame().Events.First(e => e.SourceId == tool.Id && e.Kind == PresentationKind.Attack);
        Assert.Single(e.Endpoints); Assert.NotEqual(w.Enemies[0].Position.X, e.Endpoints[0].X);
    }

    [Fact]
    public void RuntimeDamagePulsePublishesItsActualFrontGeometry()
    {
        var s = InteractiveTests.Session(); var w = s.Simulation.World; var c = s.Catalog;
        var charter = c.Runtime!.Charters.Values.First(ch => ch.Effects.Any(effect => effect.Operation == "damage-pulse"));
        var effect = charter.Effects.First(effect => effect.Operation == "damage-pulse");
        w.Runtime!.Charters[charter.Id] = 1;
        foreach (var equipment in w.Equipment) { equipment.ReadyTick = int.MaxValue; }
        var enemy = c.Enemies.Values.First(); w.Enemies.Add(new() { Id = 999, Definition = enemy.Id, Position = new(w.Lord.X, w.Lord.Y + effect.Radius - 1), Health = 100000 });
        s.Apply(new(0, 0, ReplayCommandKind.SetSpawnPermille, Value: 0)); s.Apply(new(1, 0, ReplayCommandKind.Advance));
        w.Destination = new(w.Lord.X, w.Lord.Y + 1);
        s.Simulation.Runtime!.Emit(effect.Trigger, new(w.Lord));
        var e = s.View.CaptureFrame().Events.Single(e => e.SourceId == charter.Id && e.Kind == PresentationKind.Attack);
        Assert.Equal("sector180", e.Shape); Assert.Equal(0, e.Direction.X); Assert.Equal(1, e.Direction.Y); Assert.Contains(999, e.HitEntityIds); Assert.NotNull(e.Geometry);
    }

}
