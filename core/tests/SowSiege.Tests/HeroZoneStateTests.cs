using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroZoneStateTests
{
    internal static HeroZoneParameters Parameters(string enemy) => new(1000, 2, 100, 6, 100, 2, 2, 300, 800, 1,
        new[] { new HeroZoneEnemyPolicy(enemy, true) });

    internal sealed class Scenario
    {
        internal ContentCatalog Catalog { get; }
        internal InteractiveOptions Options { get; }
        internal HeroZoneState State { get; }
        internal WorldState World { get; }
        internal long Sequence;
        internal Scenario(ContentCatalog catalog, InteractiveOptions options, WorldState world)
        { Catalog = catalog; Options = options; World = world; State = new(catalog, options, options.HeroActive!); }
        internal void Phase(int tick)
        { World.Tick = tick; State.BeginTick(tick); State.AfterMovement(World, ref Sequence); State.RefreshMembership(World); }
        internal byte[] Bytes()
        {
            using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
            State.Write(writer); writer.Flush(); return bytes.ToArray();
        }
    }

    internal static Scenario Fixture(int definitions = 1, int enemies = 1,
        Func<HeroActiveOptions, HeroActiveOptions>? configure = null,
        Func<WorldCombatSnapshot, WorldCombatSnapshot>? configureWorld = null)
    {
        var catalog = WaveEstateTests.Wave();
        var enemy = catalog.WaveRuntime!.Enemies.Values.Where(e => e.Kind == WaveEnemyKind.Pursuer).OrderBy(e => e.Id, StringComparer.Ordinal).First().Id;
        var basis = HeroActiveCodecTests.Active(catalog.Tuning.DefaultHero, catalog.Tuning.World.Progression.StartingWeapon, enemy);
        var zones = Enumerable.Range(0, definitions).Select(i => new HeroActiveDefinition($"test:zone-{i:D2}", 1,
            new string('A', 64), HeroActiveKind.ZoneSlow, null, Zone: Parameters(enemy))).ToArray();
        var active = basis with { Definitions = zones, Skills = zones.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
        if (configure is not null) { active = configure(active); }
        catalog = catalog with { HeroActive = active };
        var options = WorldCombatRuntimeTests.Options(catalog, 100, 100); var s = options.WorldCombat!.Snapshot;
        var center = new Position(catalog.Tuning.World.Map.Width / 2, catalog.Tuning.World.Map.Height / 2);
        s = s with
        {
            Hero = s.Hero with { Skills = active.Skills.Select(d => new WorldHeroSkill(d.Id, d.Version, d.ContentHash)).ToArray() },
            Objectives = new[] { new WorldObjective("test:defeat", WorldObjectiveKind.DefeatDesignated, "test:cohort", 1, 0, 0), s.Objectives[1] },
            Markers = Array.Empty<WorldMarker>(),
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, enemies, center.X + 50, center.Y, new(10000, 0, 10000, 10, 0, 10)) }
        };
        if (configureWorld is not null) { s = configureWorld(s); }
        options = options with { WorldCombat = new(s), HeroActive = active };
        var world = new WorldState { Lord = center, LordHealth = 100, WaveRuntime = new() };
        var id = 1;
        foreach (var cohort in s.Cohorts)
        {
            for (var count = 0; count < cohort.Count; count++)
            { world.Enemies.Add(new() { Id = id++, Definition = cohort.EnemyId, Health = cohort.Stats.Health, Position = new(cohort.X, cohort.Y) }); }
        }
        return new(catalog, options, world);
    }

    internal static HeroActiveOptions Change(HeroActiveOptions active, int index, Func<HeroZoneParameters, HeroZoneParameters> change)
    {
        var definitions = active.Definitions.ToArray(); definitions[index] = definitions[index] with { Zone = change(definitions[index].Zone!) };
        return active with { Definitions = definitions };
    }

    [Fact]
    public void TellPinsTargetCenterDespiteTargetMovementAndDeath()
    {
        var f = Fixture(enemies: 2); var first = f.World.Enemies[0]; var second = f.World.Enemies[1];
        second.Position = new(first.Position.X + 40, first.Position.Y);
        f.Phase(0); var zone = Assert.Single(f.State.Capture().Zones);
        Assert.Equal(new WorldPoint(first.Position.X, first.Position.Y), zone.Center);
        Assert.False(zone.Active); Assert.Empty(f.State.Capture().Sources);
        first.Position = new(first.Position.X + 1000, first.Position.Y); first.Health = 0; f.State.NotifyEnemyChanged(first);
        f.Phase(1); Assert.Equal(zone.Center, Assert.Single(f.State.Capture().Zones).Center);
        f.Phase(2); Assert.True(Assert.Single(f.State.Capture().Zones).Active);
        Assert.Equal(zone.Center, Assert.Single(f.State.Capture().Zones).Center);
        Assert.Equal(second.Id, Assert.Single(f.State.Capture().Sources).TargetId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ActiveIntervalIncludesFirstTickAndExcludesEnd(int tell)
    {
        var f = Fixture(configure: a => Change(a, 0, p => p with { TellTicks = tell, DurationTicks = 3 }));
        f.Phase(0); Assert.Equal(tell == 0, Assert.Single(f.State.Capture().Zones).Active);
        f.Phase(tell); Assert.Single(f.State.Capture().Sources);
        f.Phase(tell + 2); Assert.Single(f.State.Capture().Sources);
        f.Phase(tell + 3); Assert.Empty(f.State.Capture().Zones); Assert.Empty(f.State.Capture().Sources);
    }

    [Fact]
    public void NoLivingTargetDoesNotSpendCooldown()
    {
        var f = Fixture(); Assert.Single(f.World.Enemies).Health = 0; f.Phase(0);
        Assert.Empty(f.State.Capture().Zones); Assert.Equal(0, Assert.Single(f.State.Capture().Definitions).CooldownReadyTick);
        Assert.Equal(0, f.Sequence);
    }

    [Fact]
    public void ActivationTieUsesEntityIdRegardlessOfEnemyCollectionOrder()
    {
        var f = Fixture(enemies: 2); var center = f.World.Lord;
        f.World.Enemies[0].Position = new(center.X - 50, center.Y); f.World.Enemies[1].Position = new(center.X + 50, center.Y);
        f.World.Enemies.Reverse(); f.Phase(0);
        Assert.Equal(new WorldPoint(center.X - 50, center.Y), Assert.Single(f.State.Capture().Zones).Center);
    }

    [Fact]
    public void TellingZoneConsumesSharedSlotAndDefersLaterDefinitionWithoutCooldown()
    {
        var f = Fixture(2, configure: a =>
        {
            a = Change(a, 0, p => p with { MaximumInstances = 1, TellTicks = 2, DurationTicks = 2 });
            return Change(a, 1, p => p with { MaximumInstances = 1 });
        });
        f.Phase(0); Assert.Equal("test:zone-00", Assert.Single(f.State.Capture().Zones).DefinitionId);
        Assert.False(Assert.Single(f.State.Capture().Zones).Active);
        Assert.Equal(0, Assert.Single(f.State.Capture().Definitions, d => d.DefinitionId == "test:zone-01").CooldownReadyTick);
        f.Phase(3); Assert.Equal("test:zone-00", Assert.Single(f.State.Capture().Zones).DefinitionId);
        f.Phase(4); Assert.Equal("test:zone-01", Assert.Single(f.State.Capture().Zones).DefinitionId);
        Assert.Equal(104, Assert.Single(f.State.Capture().Definitions, d => d.DefinitionId == "test:zone-01").CooldownReadyTick);
    }

    [Fact]
    public void EveryPhaseReselectsCloserEntrantInsteadOfRetainingOldMember()
    {
        var f = Fixture(enemies: 2, configure: a => Change(a, 0, p => p with { TellTicks = 0, MaximumTargets = 1 }));
        var first = f.World.Enemies[0]; var second = f.World.Enemies[1];
        second.Position = new(first.Position.X + 80, first.Position.Y); f.Phase(0);
        Assert.Equal(first.Id, Assert.Single(f.State.Capture().Sources).TargetId);
        var center = first.Position; first.Position = new(center.X + 50, center.Y); second.Position = new(center.X + 1, center.Y);
        f.State.NotifyEnemyChanged(second); Assert.Equal(first.Id, Assert.Single(f.State.Capture().Sources).TargetId);
        f.Phase(1); Assert.Equal(second.Id, Assert.Single(f.State.Capture().Sources).TargetId);
    }

    [Fact]
    public void ExitRemovesImmediatelyButReentryWaitsForNextGlobalPhase()
    {
        var f = Fixture(configure: a => Change(a, 0, p => p with { TellTicks = 0 })); f.Phase(0);
        var enemy = Assert.Single(f.World.Enemies); var center = enemy.Position;
        enemy.Position = new(center.X + 101, center.Y); f.State.NotifyEnemyChanged(enemy);
        Assert.Empty(f.State.Capture().Sources);
        enemy.Position = center; f.State.NotifyEnemyChanged(enemy); Assert.Empty(f.State.Capture().Sources);
        f.Phase(1); Assert.Single(f.State.Capture().Sources);
    }

    [Fact]
    public void DeadMemberLeavesVacancyUntilNextGlobalSelection()
    {
        var f = Fixture(enemies: 2, configure: a => Change(a, 0, p => p with { TellTicks = 0, MaximumTargets = 1 })); f.Phase(0);
        var first = f.World.Enemies[0]; first.Health = 0; f.State.NotifyEnemyChanged(first);
        Assert.Empty(f.State.Capture().Sources); f.Phase(1);
        Assert.Equal(f.World.Enemies[1].Id, Assert.Single(f.State.Capture().Sources).TargetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnerMovementPreservesCenterButOwnerDeathClearsTellAndSources(bool active)
    {
        var f = Fixture(); f.Phase(0); if (active) { f.Phase(2); }
        var center = Assert.Single(f.State.Capture().Zones).Center;
        f.World.Lord = new(f.World.Lord.X + 20, f.World.Lord.Y); f.Phase(active ? 3 : 1);
        Assert.Equal(center, Assert.Single(f.State.Capture().Zones).Center);
        f.World.LordHealth = 0; f.State.AfterMovement(f.World, ref f.Sequence);
        Assert.Empty(f.State.Capture().Zones); Assert.Empty(f.State.Capture().Sources);
    }

    [Fact]
    public void AuthoredImmuneEnemyMayAnchorZoneButReceivesNoSlow()
    {
        var f = Fixture(configure: a => Change(a, 0, p => p with
        { TellTicks = 0, EnemyPolicies = new[] { p.EnemyPolicies[0] with { Eligible = false } } }));
        f.Phase(0); Assert.Single(f.State.Capture().Zones); Assert.Empty(f.State.Capture().Sources);
        Assert.Equal(10, f.State.MovementSpeed(Assert.Single(f.World.Enemies).Id, 10, 10));
    }

    [Fact]
    public void BossMayAnchorZoneDespiteTruePolicyButNeverReceivesSlow()
    {
        var boss = Assert.Single(WaveEstateTests.Wave().WaveRuntime!.Enemies.Values, e => e.Kind == WaveEnemyKind.FloodBoss).Id;
        var f = Fixture(configure: a => Change(a with { EnemyBodies = new[] { new HeroActiveEnemyBody(boss, 0, true) } }, 0,
            p => p with { TellTicks = 0, EnemyPolicies = new[] { new HeroZoneEnemyPolicy(boss, true) } }),
            configureWorld: s => s with { Cohorts = new[] { s.Cohorts[0] with { EnemyId = boss } } });
        f.Phase(0); var enemy = Assert.Single(f.World.Enemies);
        Assert.Equal(new WorldPoint(enemy.Position.X, enemy.Position.Y), Assert.Single(f.State.Capture().Zones).Center);
        Assert.Empty(f.State.Capture().Sources); Assert.Equal(10, f.State.MovementSpeed(enemy.Id, 10, 10));
    }
}
