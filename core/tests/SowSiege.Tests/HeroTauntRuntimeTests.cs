using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroTauntRuntimeTests
{
    internal static (ContentCatalog Catalog, InteractiveOptions Options) Fixture(int definitions = 1,
        Func<HeroActiveOptions, HeroActiveOptions>? configure = null,
        Func<WorldCombatSnapshot, WorldCombatSnapshot>? configureWorld = null)
    {
        var catalog = WaveEstateTests.Wave();
        catalog = catalog with { Tuning = catalog.Tuning with { World = catalog.Tuning.World with { Map = catalog.Tuning.World.Map with { LordSpeed = 10 } } } };
        var enemy = catalog.WaveRuntime!.Enemies.Values.Where(e => e.Kind == WaveEnemyKind.Pursuer).OrderBy(e => e.Id, StringComparer.Ordinal).First().Id;
        var basis = HeroActiveCodecTests.Active(catalog.Tuning.DefaultHero, catalog.Tuning.World.Progression.StartingWeapon, enemy);
        var taunts = HeroTauntCodecTests.Options(definitions).Definitions.Select(d => d with
        {
            Taunt = d.Taunt! with
            { EnemyPolicies = new[] { new HeroTauntEnemyPolicy(enemy, true) } }
        }).ToArray();
        var active = basis with { Definitions = taunts, Skills = taunts.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
        if (configure is not null) { active = configure(active); }
        catalog = catalog with { HeroActive = active };
        var options = WorldCombatRuntimeTests.Options(catalog, 100, 100); var s = options.WorldCombat!.Snapshot;
        var center = new Position(catalog.Tuning.World.Map.Width / 2, catalog.Tuning.World.Map.Height / 2);
        s = s with
        {
            Hero = s.Hero with { Skills = active.Skills.Select(d => new WorldHeroSkill(d.Id, d.Version, d.ContentHash)).ToArray() },
            Objectives = new[] { new WorldObjective("test:defeat", WorldObjectiveKind.DefeatDesignated, "test:cohort", 1, 0, 0), s.Objectives[1] },
            Markers = Array.Empty<WorldMarker>(),
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, 1, center.X + 50, center.Y, new(10000, 0, 10000, 1, 0, 10)) }
        };
        if (configureWorld is not null) { s = configureWorld(s); }
        return (catalog, options with { WorldCombat = new(s), HeroActive = active });
    }

    internal sealed class Run
    {
        internal InteractiveSession Session { get; }
        internal WorldState World => Session.Simulation.World;
        internal HeroActiveEffectsFrame Frame => Session.CaptureHeroActiveEffects();
        internal List<ReplayCommand> Commands { get; } = new();
        internal Run((ContentCatalog Catalog, InteractiveOptions Options) fixture) { Session = new(fixture.Catalog, fixture.Options); }
        internal void Step(short x = 0, short y = 0)
        {
            var command = new ReplayCommand(Session.NextSequence, World.Tick, ReplayCommandKind.Advance, new(x, y));
            Session.Apply(command); Commands.Add(command);
        }
        internal void Activate(int count = 1)
        {
            Step(); Assert.Empty(Frame.Taunts); Step(); Assert.Equal(count, Frame.Taunts.Count);
        }
    }

    [Fact]
    public void StationaryPreparationPinsCenterAndMovementCancelsBeforeEnemyAttack()
    {
        var run = new Run(Fixture()); run.Activate(); var instance = Assert.Single(run.Frame.Taunts);
        Assert.Equal(new WorldPoint(run.World.Lord.X, run.World.Lord.Y), instance.Center);
        Assert.True(instance.ShieldAvailable); Assert.Single(instance.Claims);
        var ready = Assert.Single(run.Frame.Definitions).CooldownReadyTick;
        run.Step(1000, 0); Assert.Empty(run.Frame.Taunts);
        Assert.Equal(0, Assert.Single(run.Frame.Definitions).PreparationTicks);
        Assert.Equal(ready, Assert.Single(run.Frame.Definitions).CooldownReadyTick);
        run.Step(); Assert.Empty(run.Frame.Taunts); Assert.Equal(0, Assert.Single(run.Frame.Definitions).PreparationTicks);
    }

    [Fact]
    public void SharedOwnerBudgetDefersLaterDefinitionWithoutSpendingItsCooldown()
    {
        var run = new Run(Fixture(2, configure: a =>
        {
            a = HeroTauntCodecTests.Change(a, 0, p => p with { MaximumInstances = 1, DurationTicks = 2 });
            return HeroTauntCodecTests.Change(a, 1, p => p with { MaximumInstances = 1, DurationTicks = 5 });
        }));
        run.Activate(); Assert.Equal("test:taunt-00", Assert.Single(run.Frame.Taunts).DefinitionId);
        var deferred = Assert.Single(run.Frame.Definitions, d => d.DefinitionId == "test:taunt-01");
        Assert.Equal(1, deferred.PreparationTicks); Assert.Equal(0, deferred.CooldownReadyTick);
        run.Step(); Assert.Equal("test:taunt-00", Assert.Single(run.Frame.Taunts).DefinitionId);
        run.Step(); Assert.Equal("test:taunt-01", Assert.Single(run.Frame.Taunts).DefinitionId);
        Assert.True(Assert.Single(run.Frame.Definitions, d => d.DefinitionId == "test:taunt-01").CooldownReadyTick > 0);
    }

    [Fact]
    public void LosingClaimsCountAndBecomeEffectiveImmediatelyAfterOldestExpires()
    {
        var run = new Run(Fixture(2, configure: a =>
        {
            a = HeroTauntCodecTests.Change(a, 0, p => p with { MaximumTargets = 1, StopRadius = 100, DurationTicks = 2 });
            return HeroTauntCodecTests.Change(a, 1, p => p with { MaximumTargets = 1, StopRadius = 0, DurationTicks = 5 });
        }));
        run.Activate(2); var frames = run.Frame.Taunts;
        Assert.True(frames[0].Sequence < frames[1].Sequence);
        Assert.Equal(Assert.Single(frames[0].Claims), Assert.Single(frames[1].Claims));
        var enemy = Assert.Single(run.World.Enemies); var held = enemy.Position;
        run.Step(); Assert.Equal(held, enemy.Position);
        run.Step(); Assert.Equal("test:taunt-01", Assert.Single(run.Frame.Taunts).DefinitionId);
        Assert.Equal(held.X - 1, enemy.Position.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClaimsRetainTargetsAndFillTiesByStableEntityId(bool completesObjective)
    {
        var run = new Run(Fixture(configure: a => HeroTauntCodecTests.Change(a, 0, p => p with { MaximumTargets = 1 }),
            configureWorld: s => s with
            {
                Cohorts = new[] { s.Cohorts[0] with { Count = 2 } },
                Objectives = new[] { s.Objectives[0] with { RequiredCount = completesObjective ? 1 : 2 }, s.Objectives[1] }
            }));
        run.Activate(); var selected = run.World.Enemies.Min(e => e.Id);
        Assert.Equal(selected, Assert.Single(Assert.Single(run.Frame.Taunts).Claims));
        var retained = Assert.Single(run.World.Enemies, e => e.Id == selected);
        var newcomer = Assert.Single(run.World.Enemies, e => e.Id != selected);
        newcomer.Position = new(run.World.Lord.X + 1, run.World.Lord.Y);
        run.Session.State.HeroActive!.RefreshMembership(run.World);
        Assert.Equal(retained.Id, Assert.Single(Assert.Single(run.Frame.Taunts).Claims));
        retained.Health = 0; run.Session.State.HeroActive.NotifyEnemyChanged(retained);
        Assert.Empty(Assert.Single(run.Frame.Taunts).Claims);
        run.Step();
        if (completesObjective)
        { Assert.Equal(WorldTerminalReason.ObjectivesCompleted, run.Session.CaptureWorldCombat().Terminal); Assert.Empty(run.Frame.Taunts); }
        else
        { Assert.Null(run.Session.CaptureWorldCombat().Terminal); Assert.Equal(newcomer.Id, Assert.Single(Assert.Single(run.Frame.Taunts).Claims)); }
    }

    [Fact]
    public void TauntApproachDoesNotRedirectAlreadySelectedTroopDamage()
    {
        var run = new Run(Fixture(configureWorld: s => s with
        {
            Troops = new[] { new WorldTroopType("test:troop", 1, new(100, 0, 0, 0, 0, 10), new[] { new WorldTroopSlot(0, s.Cohorts[0].X, s.Cohorts[0].Y) }) },
            Cohorts = new[] { s.Cohorts[0] with { Stats = s.Cohorts[0].Stats with { Attack = 7, Range = 100, CooldownTicks = 1 } } }
        }));
        var health = run.World.LordHealth; run.Activate();
        Assert.Equal(health, run.World.LordHealth);
        Assert.Equal(86, run.Session.State.WorldCombat!.Slots[0].Health);
    }

    [Fact]
    public void CommittedPhaseAndStopUntilAreNotOverwrittenByTaunt()
    {
        var run = new Run(Fixture(configure: a => HeroTauntCodecTests.Change(a, 0, p => p with { StopRadius = 0 })));
        run.Activate(); var enemy = Assert.Single(run.World.Enemies); var action = run.World.WaveRuntime!.EnemyActions[enemy.Id];
        action.Phase = "tell"; action.Target = new(123, 456); action.TargetId = 42; action.UntilTick = 30; action.StopUntil = 20;
        var position = enemy.Position; var attackTick = enemy.AttackTick;
        run.Step(); Assert.Equal(position, enemy.Position); Assert.Equal(attackTick, enemy.AttackTick);
        Assert.Equal("tell", action.Phase); Assert.Equal(new Position(123, 456), action.Target);
        Assert.Equal(42, action.TargetId); Assert.Equal(30, action.UntilTick);
    }

    [Fact]
    public void CommittedPhaseWithoutStopKeepsOriginalApproachAndTargetFields()
    {
        var run = new Run(Fixture()); run.Activate();
        var enemy = Assert.Single(run.World.Enemies); var action = run.World.WaveRuntime!.EnemyActions[enemy.Id];
        var before = enemy.Position;
        action.Phase = "tell"; action.StopUntil = 0; action.Target = new(123, 456); action.TargetId = 42; action.UntilTick = 30;
        run.Step();
        Assert.Equal(before.X - 1, enemy.Position.X);
        Assert.Equal(before.Y, enemy.Position.Y);
        Assert.Equal("tell", action.Phase); Assert.Equal(new Position(123, 456), action.Target);
        Assert.Equal(42, action.TargetId); Assert.Equal(30, action.UntilTick); Assert.Equal(0, action.StopUntil);
        Assert.Single(Assert.Single(run.Frame.Taunts).Claims);
    }

    [Fact]
    public void ExplicitlyIneligibleOrImmobileEnemyCannotActivateTaunt()
    {
        var excluded = new Run(Fixture(configure: a => HeroTauntCodecTests.Change(a, 0, p => p with
        { EnemyPolicies = new[] { p.EnemyPolicies[0] with { Eligible = false } } })));
        excluded.Step(); excluded.Step(); Assert.Empty(excluded.Frame.Taunts);
        Assert.Equal(0, Assert.Single(excluded.Frame.Definitions).CooldownReadyTick);
        var immobile = new Run(Fixture(configureWorld: s => s with { Cohorts = new[] { s.Cohorts[0] with { Stats = s.Cohorts[0].Stats with { Speed = 0 } } } }));
        immobile.Step(); immobile.Step(); Assert.Empty(immobile.Frame.Taunts);
    }

    [Fact]
    public void BuildingBraceReleasesSurvivingClaimImmediatelyAfterKnockback()
    {
        var fixture = Fixture(); var wave = fixture.Catalog.WaveRuntime!;
        const string hammer = "core:carpenter_hammer";
        var gear = wave.Gear.ToDictionary(x => x.Key, x => x.Value);
        gear[hammer] = gear[hammer] with
        {
            Range = 100,
            Knockback = 2000,
            Levels = gear[hammer].Levels!.Select(level => level with { Range = 100, Knockback = 2000 }).ToArray()
        };
        var catalog = fixture.Catalog with { WaveRuntime = wave with { Gear = gear } };
        var snapshot = fixture.Options.WorldCombat!.Snapshot;
        var options = fixture.Options with
        {
            WorldCombat = new(snapshot with
            { Provenance = snapshot.Provenance with { ProfileHash = PortableStateCodec.HashCatalog(catalog) } })
        };
        var run = new Run((catalog, options)); run.Activate(); var enemy = Assert.Single(run.World.Enemies);
        Assert.Equal(enemy.Id, Assert.Single(Assert.Single(run.Frame.Taunts).Claims));
        run.World.WaveRuntime!.Work.Add(new()
        {
            Id = run.World.AllocateId(),
            Source = hammer,
            Kind = "building",
            Position = run.World.Lord,
            Complete = true,
            Health = 100,
            ReadyTick = run.World.Tick
        });
        var before = enemy.Position; var tick = run.World.Tick;
        new WaveWorkSystem(catalog, run.World, run.Session.State).Tick();
        Assert.Equal(tick, run.World.Tick); Assert.True(enemy.Health > 0); Assert.NotEqual(before, enemy.Position);
        Assert.True(enemy.Position.DistanceSquared(run.World.Lord) > 1000L * 1000);
        Assert.Contains(run.World.WaveRuntime.Events, e => e.Kind == "building-brace-hit" && e.SubjectId == enemy.Id);
        Assert.Empty(Assert.Single(run.Frame.Taunts).Claims);
    }
}
