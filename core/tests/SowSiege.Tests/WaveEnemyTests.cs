using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;

public sealed class WaveEnemyTests
{
    private static (WaveEnemySystem System, WorldState World, EnemyState Enemy, ContentCatalog Catalog) Arena(WaveEnemyKind kind)
    {
        var c = FirstPlayableTests.Catalog("arc");
        var id = c.Enemies.Keys.First();
        var enemies = new Dictionary<string, EnemyDefinition> { [id] = new(id, "lord", 100, 10, 7, 20, 20, 1) };
        var rules = new Dictionary<string, WaveEnemyDefinition> { [id] = new(id, "design:test", kind, 3, 10, 4, 30, 10000) };
        c = c with { Enemies = enemies, WaveRuntime = new("test", "test:chapter", "design:test", id, 10000, 3, 2, 10, 5, 10, 20, 10, 10, 20, 5, new Dictionary<string, WaveGearDefinition>(), new Dictionary<string, WaveItemDefinition>(), new Dictionary<string, WaveEvolutionDefinition>(), rules) };
        var world = new WorldState { Lord = new(200, 100), LordHealth = 100, WaveRuntime = new(), NextId = 10 };
        var enemy = new EnemyState { Id = 1, Definition = id, Position = new(100, 100), Health = 100 };
        world.Enemies.Add(enemy);
        return (new(c, world, new TrackedRandom(1, true), null), world, enemy, c);
    }
    private static void Tick(WaveEnemySystem system, WorldState world, int count = 1)
    { for (var i = 0; i < count; i++) { system.Tick(); world.Tick++; } }

    [Fact]
    public void ChargeLocksItsTellAndMissesASidestep()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.Charger);
        Tick(system, world);
        var state = world.WaveRuntime!.EnemyActions[enemy.Id];
        var locked = state.Target;
        world.Lord = new(200, 200);
        Tick(system, world, 3);
        Assert.Equal(locked, state.Target);
        Assert.Equal(new Position(100, 100), enemy.Position);
        Assert.Equal(100, world.LordHealth);
        Tick(system, world, 6);
        Assert.Equal(locked, enemy.Position);
        Assert.Equal(100, world.LordHealth);
        Assert.Equal("recovery", state.Phase);
    }

    [Fact]
    public void ChargeSweepsItsPathAndHurtsOnlyOnce()
    {
        var (system, world, _, _) = Arena(WaveEnemyKind.Charger);
        world.Lord = new(130, 100);
        Tick(system, world, 7);
        Assert.Equal(93, world.LordHealth);
    }

    [Fact]
    public void MissingCropDuringTellDoesNotDamageLordOrConsumeAnotherCrop()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.SeedThief);
        var crop = new WaveWork { Id = 0, Kind = "grain", Position = enemy.Position, Health = 1 };
        world.WaveRuntime!.Work.Add(crop);
        Tick(system, world);
        Assert.Equal(0, world.WaveRuntime.EnemyActions[enemy.Id].TargetId);
        world.WaveRuntime.Work.Clear(); world.Lord = enemy.Position;
        Tick(system, world, 3);
        Assert.Equal(100, world.LordHealth);
        Assert.DoesNotContain(world.WaveRuntime.Events, e => e.Kind == "seed-theft");
        Tick(system, world, 5);
        Assert.Equal(-1, world.WaveRuntime.EnemyActions[enemy.Id].TargetId);
    }

    [Fact]
    public void RipeGrazerWaitsThenConsumesOnlyTheLockedRipeCrop()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.RipeGrazer);
        var ripe = new WaveWork { Id = 2, Kind = "grain", Position = enemy.Position, Health = 1, Complete = true };
        var seed = new WaveWork { Id = 3, Kind = "grain", Position = enemy.Position, Health = 1 };
        world.WaveRuntime!.Work.AddRange(new[] { ripe, seed });
        Tick(system, world, 3);
        Assert.Equal(1, ripe.Health);
        Tick(system, world);
        Assert.Equal(0, ripe.Health); Assert.Equal(1, seed.Health);
    }

    [Fact]
    public void ShieldBlocksFrontButExposesRearAndTurningBody()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.Shield);
        Tick(system, world);
        Assert.True(system.Intercepts(new(200, 100), new(90, 100)));
        Assert.False(system.Intercepts(new(0, 100), new(120, 100)));
        world.Lord = new(0, 100); Tick(system, world);
        Assert.Equal("turn", world.WaveRuntime!.EnemyActions[enemy.Id].Phase);
        Assert.False(system.Intercepts(new(200, 100), new(90, 100)));
    }

    [Fact]
    public void RangedShotFollowsLockedAimWhileShooterRetreats()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.Ranged);
        world.Lord = new(160, 100);
        world.WaveRuntime!.Work.Add(new WaveWork { Id = 2, Kind = "grain", Position = world.Lord, Health = 1, Complete = true });
        Tick(system, world);
        world.Lord = new(160, 200); Tick(system, world, 3);
        var before = enemy.Position;
        Tick(system, world, 3);
        Assert.NotEqual(before, enemy.Position);
        Assert.Equal(100, world.LordHealth);
        Assert.Empty(world.WaveRuntime!.Projectiles);
    }

    [Fact]
    public void BossLaneHasARealEscapeAndDormancyExcludesRipeAndOffLaneCrops()
    {
        var (system, world, enemy, c) = Arena(WaveEnemyKind.FloodBoss);
        Tick(system, world);
        var action = world.WaveRuntime!.EnemyActions[enemy.Id];
        var middle = new Position((action.Origin.X + action.Target.X) / 2, action.Origin.Y);
        var escape = new Position(action.Target.X + c.Enemies[enemy.Definition].Range + 1, action.Target.Y);
        world.Lord = escape;
        var young = new WaveWork { Id = 2, Kind = "grain", Position = middle, Health = 1 };
        var ripe = new WaveWork { Id = 3, Kind = "grain", Position = middle, Health = 1, Complete = true };
        var safe = new WaveWork { Id = 4, Kind = "grain", Position = escape, Health = 1 };
        world.WaveRuntime.Work.AddRange(new[] { young, ripe, safe });
        Tick(system, world, 4);
        Assert.True(young.DormantUntil > world.Tick);
        Assert.Equal(0, ripe.DormantUntil); Assert.Equal(0, safe.DormantUntil);
        Assert.Equal(100, world.LordHealth);
        world.Lord = middle; Tick(system, world);
        Assert.Equal(93, world.LordHealth);
        Tick(system, world, 3); Assert.Equal(93, world.LordHealth);
    }
    [Fact]
    public void BossCyclesTwoSequentialLanesBeforeItsLockedCharge()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.FloodBoss);
        var tells = new List<(string Phase, Position Target)>();
        for (var tick = 0; tick < 40; tick++)
        {
            Tick(system, world);
            var action = world.WaveRuntime!.EnemyActions[enemy.Id];
            if (action.Phase.StartsWith("tell-", StringComparison.Ordinal) && (tells.Count == 0 || tells[^1].Target != action.Target || tells[^1].Phase != action.Phase))
                tells.Add((action.Phase, action.Target));
        }
        Assert.Equal("tell-water", tells[0].Phase);
        Assert.Equal("tell-water", tells[1].Phase);
        Assert.NotEqual(tells[0].Target.Y, tells[1].Target.Y);
        Assert.Equal("tell-charge", tells[2].Phase);
    }

    [Fact]
    public void PursuerTellHurtsAnInterceptingGroupInsteadOfLord()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.Pursuer);
        world.Lord = enemy.Position;
        var group = new WaveGroup { Id = 3, Position = enemy.Position, Health = 10, Phase = "engaging" };
        world.WaveRuntime!.Groups.Add(group);
        Tick(system, world, 3); Assert.Equal(10, group.Health);
        Tick(system, world); Assert.Equal(3, group.Health); Assert.Equal(100, world.LordHealth);
    }

    [Fact]
    public void BossChargeDamagesBuildingsWithoutResettingShipmentIdentity()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.FloodBoss);
        var building = new WaveWork { Id = 2, Kind = "building", Position = new(130, 100), Health = 10, Complete = true, Cycle = 42 };
        world.WaveRuntime!.Work.Add(building);
        world.WaveRuntime.EnemyActions[enemy.Id] = new WaveEnemyAction { Phase = "charge", Origin = enemy.Position, Target = new(200, 100), UntilTick = 20 };
        Tick(system, world);
        Assert.Equal(3, building.Health); Assert.False(building.Complete); Assert.Equal(42, building.Cycle); Assert.Equal(-1, building.ReadyTick);
    }

    [Fact]
    public void RoofStopsOneRaidThenLosesProtectionUntilRepair()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.SeedThief);
        var roof = new WaveWork { Id = 2, Kind = "building", Position = enemy.Position, Health = 20, Complete = true };
        var crop = new WaveWork { Id = 3, Kind = "grain", Position = enemy.Position, Health = 1, ParentId = 2, Protected = true };
        world.WaveRuntime!.Work.AddRange(new[] { roof, crop });
        Tick(system, world, 4);
        Assert.Equal(1, crop.Health); Assert.False(crop.Protected); Assert.False(roof.Complete);
        Assert.Contains("roof-used:2", world.WaveRuntime.Completed);
        Tick(system, world, 8); Assert.Equal(0, crop.Health);
    }

    [Fact]
    public void SeedTheftCreatesLinkedDetourAndFollowingEnemyWalksAroundIt()
    {
        var (system, world, enemy, catalog) = Arena(WaveEnemyKind.SeedThief);
        var gear = new WaveGearDefinition("test:seed", "design:test", WaveAttackKind.SeedFan, 1, 100, 10, 10, 1, 20, 1, 50, 30, 1, 5);
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime!.Gear).Add(gear.Id, gear);
        ((Dictionary<string, WaveItemDefinition>)catalog.WaveRuntime.Items).Add("test:detour", new("test:detour", "design:test", WaveItemKind.SeedDetour, new[] { gear.Id }, 1));
        world.WaveRuntime!.Items.Add("test:detour");
        world.WaveRuntime.Work.Add(new WaveWork { Id = 3, Source = gear.Id, Kind = "grain", Position = enemy.Position, Health = 1 });
        Tick(system, world, 4); Assert.Single(world.WaveRuntime.Detours);
        enemy.Position = new(40, 100); world.WaveRuntime.EnemyActions[enemy.Id].Phase = "approach";
        var before = enemy.Position; Tick(system, world);
        Assert.True(enemy.Position.Y < before.Y);
        Assert.InRange(Math.Abs(enemy.Position.X - before.X), 0, 10);
        Assert.InRange(Math.Abs(enemy.Position.Y - before.Y), 0, 10);
    }

    [Fact]
    public void ConnectedWorkTraceChangesPursuerApproach()
    {
        var (system, world, enemy, _) = Arena(WaveEnemyKind.Pursuer);
        world.WaveRuntime!.Paths.AddRange(new[] { new Position(150, 130), new Position(180, 130) });
        Tick(system, world);
        Assert.True(enemy.Position.Y > 100);
    }

    [Theory]
    [InlineData(110, 100, true)]
    [InlineData(110, 130, false)]
    [InlineData(125, 100, false)]
    public void CoverOnlyInterceptsWhenPhysicallyBetweenEnemyAndReturningGroup(int x, int y, bool intercepted)
    {
        var (system, world, _, _) = Arena(WaveEnemyKind.Pursuer);
        var returning = new WaveGroup { Id = 3, Health = 20, Phase = "returning", Position = new(120, 100) };
        var cover = new WaveGroup { Id = 4, Health = 20, Phase = "engaging", Formation = "cover", Training = 1, Position = new(x, y) };
        world.WaveRuntime!.Groups.AddRange(new[] { returning, cover });
        Tick(system, world, 4);
        Assert.Equal(intercepted ? 20 : 13, returning.Health);
        Assert.Equal(intercepted ? 13 : 20, cover.Health);
        Assert.Equal(new Position(x, y), cover.Position);
        Assert.Equal(intercepted, world.WaveRuntime.Events.Any(e => e.Kind == "group-cover-intercept"));
    }

    [Fact]
    public void CoverMovingAwayDuringTellCannotRemotelyProtectReturner()
    {
        var (system, world, _, _) = Arena(WaveEnemyKind.Pursuer);
        var returning = new WaveGroup { Id = 3, Health = 20, Phase = "returning", Position = new(120, 100) };
        var cover = new WaveGroup { Id = 4, Health = 20, Phase = "engaging", Formation = "cover", Training = 1, Position = new(110, 100) };
        world.WaveRuntime!.Groups.AddRange(new[] { returning, cover });
        Tick(system, world); cover.Position = new(110, 130); Tick(system, world, 3);
        Assert.Equal(13, returning.Health); Assert.Equal(20, cover.Health);
        Assert.DoesNotContain(world.WaveRuntime.Events, e => e.Kind == "group-cover-intercept");
    }

    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, true)]
    public void RangedEnemyCannotShootWithoutNearbyLivingRipePlot(bool complete, int health, bool distant)
    {
        var (system, world, enemy, catalog) = Arena(WaveEnemyKind.Ranged);
        world.Lord = new(110, 100);
        world.WaveRuntime!.Work.Add(new WaveWork { Id = 2, Kind = "grain", Position = distant ? new Position(world.Lord.X + catalog.Tuning.World.Farms.Spacing + 1, world.Lord.Y) : world.Lord, Health = health, Complete = complete });
        Tick(system, world, 10);
        Assert.Equal(100, world.LordHealth);
        Assert.Empty(world.WaveRuntime.Projectiles);
        Assert.Equal("approach", world.WaveRuntime.EnemyActions[enemy.Id].Phase);
        Assert.DoesNotContain(world.WaveRuntime.Events, e => e.Kind == "enemy-tell");
    }

    [Fact]
    public void RangedTellRequiresRipePlotButCommittedShotSurvivesHarvest()
    {
        var (system, world, _, _) = Arena(WaveEnemyKind.Ranged);
        world.Lord = new(160, 100);
        Tick(system, world); Assert.Empty(world.WaveRuntime!.Events);
        var crop = new WaveWork { Id = 2, Kind = "grain", Position = world.Lord, Health = 1, Complete = true };
        world.WaveRuntime.Work.Add(crop); Tick(system, world);
        Assert.Contains(world.WaveRuntime.Events, e => e.Kind == "enemy-tell");
        crop.Health = 0;
        Tick(system, world, 4);
        Assert.Equal(93, world.LordHealth);
    }

}
