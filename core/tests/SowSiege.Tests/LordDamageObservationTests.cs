using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class LordDamageObservationTests
{
    private sealed class Observer : ILordDamageObserver
    {
        internal readonly List<LordDamageObservation> Hits = new();
        public void OnLordDamage(in LordDamageObservation observation) => Hits.Add(observation);
    }
    private static (WaveEnemySystem System, WorldState World, Observer Observer) Arena(string phase, bool invulnerable = false, bool unknownProjectile = false)
    {
        var c = FirstPlayableTests.Catalog("arc");
        var id = c.Enemies.Keys.First();
        var enemies = new Dictionary<string, EnemyDefinition> { [id] = new(id, "lord", 100, 10, 7, 20, 20, 1) };
        var kind = phase == "tell-shot" ? WaveEnemyKind.Ranged : WaveEnemyKind.Charger;
        var rules = new Dictionary<string, WaveEnemyDefinition> { [id] = new(id, "design:test", kind, 3, 10, 4, 30, 10000) };
        c = c with { Enemies = enemies, WaveRuntime = new("test", "test:chapter", "design:test", id, 10000, 3, 2, 10, 5, 10, 20, 10, 10, 20, 5, new(2, 2, 4, 2), new Dictionary<string, WaveGearDefinition>(), new Dictionary<string, WaveItemDefinition>(), new Dictionary<string, WaveEvolutionDefinition>(), rules) };
        var observer = new Observer();
        var world = new WorldState { Lord = new(130, 100), LordHealth = 100, WaveRuntime = new(), NextId = 10, LordDamageObserver = observer };
        world.Enemies.Add(new EnemyState { Id = 1, Definition = id, Position = phase == "tell-strike" ? world.Lord : new(100, 100), Health = 100 });
        world.WaveRuntime.EnemyActions[1] = new() { Phase = phase, Origin = new(100, 100), Target = new(200, 100), TargetId = -1, UntilTick = phase.StartsWith("tell-") ? 0 : 100 };
        if (unknownProjectile)
        {
            world.Enemies.Clear();
            world.WaveRuntime.Projectiles.Add(new() { Id = 42, Source = id, Position = new(100, 100), Direction = new(200, 100), Damage = 7, Speed = 30, ExpireTick = 100, Hostile = true });
        }
        return (new(c, world, new TrackedRandom(1, true), new InteractiveState { Invulnerable = invulnerable }), world, observer);
    }
    [Theory]
    [InlineData("tell-strike", LordDamageKind.Strike)]
    [InlineData("charge", LordDamageKind.Charge)]
    [InlineData("water", LordDamageKind.Water)]
    [InlineData("tell-shot", LordDamageKind.Projectile)]
    public void ActualDamagePreservesSourceInstanceAndExistingEvent(string phase, LordDamageKind expected)
    {
        var (system, world, observer) = Arena(phase);
        system.Tick();
        var hit = Assert.Single(observer.Hits);
        Assert.Equal(1, hit.AttackerId); Assert.Equal(expected, hit.Kind); Assert.Equal(7, hit.Damage);
        Assert.Equal(93, world.LordHealth);
        Assert.Equal(expected == LordDamageKind.Projectile ? 10 : -1, hit.ProjectileId);
        var oldEvent = Assert.Single(world.WaveRuntime!.Events, e => e.Kind == "lord-hit");
        Assert.Equal(0, oldEvent.SubjectId); Assert.Equal(hit.DefinitionId, oldEvent.Source); Assert.Equal(hit.Damage, oldEvent.Amount);
    }
    [Fact]
    public void MissingProjectileOwnerIsUnknownNotGuessed()
    {
        var (system, _, observer) = Arena("charge", unknownProjectile: true); system.Tick();
        var hit = Assert.Single(observer.Hits); Assert.Equal(-1, hit.AttackerId); Assert.Equal(42, hit.ProjectileId);
    }
    [Fact]
    public void InvulnerabilityAndZeroHealthDoNotGeneratePositiveDamage()
    {
        var (system, world, observer) = Arena("charge", invulnerable: true); system.Tick();
        Assert.Empty(observer.Hits); Assert.Equal(100, world.LordHealth);
        (system, world, observer) = Arena("charge"); world.LordHealth = 0; system.Tick(); Assert.Empty(observer.Hits);
    }
    [Fact]
    public void ImmediateContactReportsTheTouchingInstance()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
        var options = new InteractiveOptions(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64), new(ImmediateContact: true));
        var session = new InteractiveSession(catalog, options); var observer = new Observer(); session.LordDamageObserver = observer;
        var world = session.Simulation.World;
        var id = catalog.WaveRuntime!.Enemies.Values.First(e => e.Kind == WaveEnemyKind.Pursuer).Id;
        var enemy = new EnemyState { Id = world.NextId++, Definition = id, Position = world.Lord, Health = 1000 };
        world.Enemies.Add(enemy);
        session.Apply(new(0, 0, ReplayCommandKind.Advance));
        var hit = Assert.Single(observer.Hits);
        Assert.Equal(enemy.Id, hit.AttackerId); Assert.Equal(LordDamageKind.Contact, hit.Kind); Assert.True(hit.Damage > 0);
    }
    [Fact]
    public void ObserverAndDiagnosticProjectileOwnerDoNotChangeStateHashOrEvents()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
        InteractiveSession Create()
        {
            var session = new InteractiveSession(catalog, new(new(30000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64)));
            var world = session.Simulation.World; var id = catalog.WaveRuntime!.Enemies.Keys.First();
            world.WaveRuntime!.Projectiles.Add(new() { Id = world.NextId++, Source = id, Position = world.Lord, Previous = world.Lord, Direction = world.Lord, Damage = 7, Speed = 30, ExpireTick = 100, Hostile = true });
            return session;
        }
        var plain = Create(); var observed = Create(); var observer = new Observer();
        observed.LordDamageObserver = observer;
        observed.Simulation.World.WaveRuntime!.Projectiles[0].DiagnosticAttackerId = 4321;
        Assert.Equal(plain.ComputeStateHash(), observed.ComputeStateHash());
        var command = new ReplayCommand(0, 0, ReplayCommandKind.Advance);
        plain.Apply(command); observed.Apply(command);
        Assert.Equal(plain.ComputeStateHash(), observed.ComputeStateHash());
        Assert.Equal(((IWaveRunView)plain.View).CaptureWaveRuntime()!.Events, ((IWaveRunView)observed.View).CaptureWaveRuntime()!.Events);
        Assert.Equal(4321, Assert.Single(observer.Hits).AttackerId);
    }
}
