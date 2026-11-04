using System.Reflection;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroTauntDamageTests
{
    [Theory]
    [InlineData(10, 0, 4, false)]
    [InlineData(-10, 0, 7, true)]
    [InlineData(0, 10, 4, false)]
    [InlineData(0, 0, 7, true)]
    public void ShieldChecksDirectionAndCoincidentOrigin(int x, int y, int expected, bool charge)
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture()); run.Activate(); var hero = run.World.Lord;
        var remaining = run.Session.State.HeroActive!.ReduceDamage(7, hero, new(hero.X + x, hero.Y + y), out var prevented);
        Assert.Equal(expected, remaining); Assert.Equal(7 - expected, prevented);
        Assert.Equal(charge, Assert.Single(run.Frame.Taunts).ShieldAvailable);
    }

    [Theory]
    [InlineData(3, 4, true)]
    [InlineData(2, 4, false)]
    [InlineData(3, -4, true)]
    public void ConeCosineBoundaryIsInclusiveAndUsesIntegerDirection(int x, int y, bool covered)
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture(configure: a =>
            HeroTauntCodecTests.Change(a, 0, p => p with { ShieldHalfAngleCosPermille = 600 })));
        run.Activate(); var hero = run.World.Lord;
        Assert.Equal(covered ? 6 : 10, run.Session.State.HeroActive!.ReduceDamage(10, hero, new(hero.X + x, hero.Y + y), out _));
        Assert.Equal(!covered, Assert.Single(run.Frame.Taunts).ShieldAvailable);
    }

    [Fact]
    public void UnknownOriginAndZeroIncomingDamageDoNotConsumeCharge()
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture()); run.Activate(); var hero = run.World.Lord;
        Assert.Equal(7, run.Session.State.HeroActive!.ReduceDamage(7, hero, null, out var prevented)); Assert.Equal(0, prevented);
        Assert.Equal(0, run.Session.State.HeroActive.ReduceDamage(0, hero, new(hero.X + 10, hero.Y), out prevented)); Assert.Equal(0, prevented);
        Assert.True(Assert.Single(run.Frame.Taunts).ShieldAvailable);
    }

    [Fact]
    public void LargeIntegerConeComparisonAvoidsOverflowAndRejectsZeroDirection()
    {
        Assert.True(HeroTauntState.InsideCone(new(1000000, 1000000), new(1000000, 1000000), 1000));
        Assert.False(HeroTauntState.InsideCone(new(1000000, 1000000), new(-1000000, -1000000), 0));
        Assert.True(HeroTauntState.InsideCone(new(1000000, 1000000), new(-1000000, -1000000), -1000));
        Assert.False(HeroTauntState.InsideCone(new(0, 0), new(1000000, 0), -1000));
    }

    [Fact]
    public void ApplicableRawValuesSumThenCapOnceAndConsumeAllContributors()
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture(2, configure: a =>
        {
            a = HeroTauntCodecTests.Change(a, 0, p => p with { DamageReductionCapPermille = 500 });
            return HeroTauntCodecTests.Change(a, 1, p => p with { DamageReductionCapPermille = 500 });
        }));
        run.Activate(2); var hero = run.World.Lord; var origin = new Position(hero.X + 10, hero.Y);
        Assert.Equal(3, run.Session.State.HeroActive!.ReduceDamage(7, hero, origin, out var prevented)); Assert.Equal(4, prevented);
        Assert.All(run.Frame.Taunts, t => Assert.False(t.ShieldAvailable));
        Assert.Equal(7, run.Session.State.HeroActive.ReduceDamage(7, hero, origin, out prevented)); Assert.Equal(0, prevented);
    }

    [Fact]
    public void ZeroRawShieldDoesNotContributeOrConsumeCharge()
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture(2,
            configure: a => HeroTauntCodecTests.Change(a, 0, p => p with { DamageReductionPermille = 0 })));
        run.Activate(2); var hero = run.World.Lord;
        Assert.Equal(4, run.Session.State.HeroActive!.ReduceDamage(7, hero, new(hero.X + 10, hero.Y), out _));
        Assert.True(Assert.Single(run.Frame.Taunts, t => t.DefinitionId == "test:taunt-00").ShieldAvailable);
        Assert.False(Assert.Single(run.Frame.Taunts, t => t.DefinitionId == "test:taunt-01").ShieldAvailable);
    }

    private static (HeroTauntRuntimeTests.Run Run, WaveEnemySystem Enemies) ContactRun(int reduction)
    {
        var fixture = HeroTauntRuntimeTests.Fixture(configure: a => HeroTauntCodecTests.Change(a, 0, p => p with
        { DamageReductionPermille = reduction, DamageReductionCapPermille = 1000 }));
        fixture = (fixture.Catalog, fixture.Options with { Feel = new(ImmediateContact: true) });
        var run = new HeroTauntRuntimeTests.Run(fixture); run.Activate();
        return (run, new(fixture.Catalog, run.World, new(30001, true), run.Session.State));
    }

    private static void Contact(WaveEnemySystem system, HeroTauntRuntimeTests.Run run)
    {
        var enemy = Assert.Single(run.World.Enemies); var rule = run.Session.Catalog.WaveRuntime!.Enemies[enemy.Definition];
        typeof(WaveEnemySystem).GetMethod("Contact", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(system, new object[] { enemy, rule });
    }

    [Fact]
    public void FullyReducedActualContactCreatesNoImmunityOrKnockback()
    {
        var (run, enemies) = ContactRun(1000); var health = run.World.LordHealth; var position = run.World.Lord;
        Contact(enemies, run);
        Assert.Equal(health, run.World.LordHealth); Assert.Equal(position, run.World.Lord);
        Assert.Equal(0, run.Session.State.Feel!.InvulnerableUntil);
        Assert.False(Assert.Single(run.Frame.Taunts).ShieldAvailable);
        Contact(enemies, run);
        Assert.True(run.World.LordHealth < health); Assert.NotEqual(position, run.World.Lord);
        Assert.Empty(run.Frame.Taunts);
    }

    [Fact]
    public void ExistingImmunityRejectsDamageBeforeShieldChargeIsConsumed()
    {
        var (run, enemies) = ContactRun(400); var health = run.World.LordHealth;
        run.Session.State.Feel!.InvulnerableUntil = run.World.Tick + 1;
        Assert.False(enemies.DamageWorldHero(Assert.Single(run.World.Enemies), 10));
        Assert.Equal(health, run.World.LordHealth); Assert.True(Assert.Single(run.Frame.Taunts).ShieldAvailable);
    }

    [Fact]
    public void LowPositiveDamageFloorsToZeroWithoutStartingImmunity()
    {
        var (run, enemies) = ContactRun(400); var health = run.World.LordHealth;
        Assert.False(enemies.DamageWorldHero(Assert.Single(run.World.Enemies), 1));
        Assert.Equal(health, run.World.LordHealth); Assert.Equal(0, run.Session.State.Feel!.InvulnerableUntil);
        Assert.False(Assert.Single(run.Frame.Taunts).ShieldAvailable);
    }

    [Fact]
    public void ActualContactKnockbackCancelsUnconsumedShieldsBeforeAnotherHit()
    {
        var (run, enemies) = ContactRun(0); var health = run.World.LordHealth; var position = run.World.Lord;
        Contact(enemies, run);
        Assert.True(run.World.LordHealth < health); Assert.NotEqual(position, run.World.Lord);
        Assert.Empty(run.Frame.Taunts);
        Assert.Equal(10, run.Session.State.HeroActive!.ReduceDamage(10, run.World.Lord, new(position.X + 50, position.Y), out var prevented));
        Assert.Equal(0, prevented);
    }

    [Fact]
    public void FatalDamageImmediatelyClearsAllInstancesAndClaims()
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture()); run.Activate();
        var enemy = Assert.Single(run.World.Enemies); enemy.Position = new(run.World.Lord.X - 50, run.World.Lord.Y);
        var system = new WaveEnemySystem(run.Session.Catalog, run.World, new(30001, true), run.Session.State);
        Assert.True(system.DamageWorldHero(enemy, run.World.LordHealth));
        Assert.Equal(0, run.World.LordHealth); Assert.Empty(run.Frame.Taunts);
    }

    [Fact]
    public void EstateDarkModifierRunsBeforeReductionAndKeepsItsOwnObservedDelta()
    {
        var fixture = HeroTauntRuntimeTests.Fixture();
        var catalog = fixture.Catalog with { WaveRuntime = fixture.Catalog.WaveRuntime! with { EstateLight = WaveEstateTests.Light(darkPermille: 500) } };
        var hash = PortableStateCodec.HashCatalog(catalog); var snapshot = fixture.Options.WorldCombat!.Snapshot;
        var options = fixture.Options with
        {
            Estate = new(1, "L1", hash),
            WorldCombat = new(snapshot with
            { Provenance = snapshot.Provenance with { ProfileHash = hash } })
        };
        var run = new HeroTauntRuntimeTests.Run((catalog, options)); run.Activate();
        var enemy = Assert.Single(run.World.Enemies);
        enemy.Position = new(catalog.Tuning.World.Map.Width - 1, catalog.Tuning.World.Map.Height - 1);
        Assert.False(WaveEstateSystem.Lit(catalog.WaveRuntime!, run.World.WaveRuntime!.Estate!, enemy.Position));
        var health = run.World.LordHealth;
        var system = new WaveEnemySystem(catalog, run.World, new(30001, true), run.Session.State);
        Assert.True(system.DamageWorldHero(enemy, 10)); Assert.Equal(health - 9, run.World.LordHealth);
        Assert.Equal(5, Assert.Single(run.World.WaveRuntime.Events, e => e.Tick == 2 && e.Kind == "estate-dark-hit").Amount);
        Assert.Equal(6, Assert.Single(run.World.WaveRuntime.Events, e => e.Tick == 2 && e.Kind == "hero-active-reduced").Amount);
    }
}
