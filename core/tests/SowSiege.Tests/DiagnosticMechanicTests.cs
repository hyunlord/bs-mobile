using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class DiagnosticMechanicTests
{
    private static (ContentCatalog Catalog, RunOptions Options, Simulation Sim, DiagnosticObserver Observer) Fixture(DiagnosticVariant variant)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "s4b-02");
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "people", "C", Movement: "circuit");
        var observer = new DiagnosticObserver(new(variant));
        var sim = SimulationFactory.Create(catalog, options, observer);
        sim.World.People.Clear(); sim.World.Farms.Clear(); sim.World.Buildings.Clear(); sim.World.Enemies.Clear(); sim.World.Tick = 1;
        return (catalog, options, sim, observer);
    }

    [Theory]
    [InlineData("peasant", false)]
    [InlineData("militia", true)]
    [InlineData("vassal", false)]
    [InlineData("guard", true)]
    [InlineData("returning", true)]
    public void OffenseSuppressionKeepsPeopleCooldownsAndExcludesActualLedgers(string role, bool toolSource)
    {
        foreach (var variant in new[] { DiagnosticVariant.Control, DiagnosticVariant.OffenseOff, DiagnosticVariant.BothOff })
        {
            var (catalog, options, sim, observer) = Fixture(variant); var world = sim.World;
            var source = toolSource ? catalog.Tools.Keys.First() : "";
            var person = new PersonState { Id = world.AllocateId(), Role = role, Source = source, Position = world.Estate, Destination = world.Estate, Health = 100, Members = 1, DutyUntil = 100 };
            world.People.Add(person);
            if (role == "returning") { sim.Runtime!.Entity(person.Id).HoldUntil = 100; }
            var enemy = new EnemyState { Id = world.AllocateId(), Definition = catalog.Enemies.Keys.First(), Position = world.Estate, Health = 1 };
            world.Enemies.Add(enemy); var spatial = new SpatialHash(catalog.Tuning.World.Map.CellSize); spatial.Rebuild(world.Enemies);
            new EstateSystem(catalog, options, world, "C", spatial, sim.Runtime, sim.Experiment, observer).Tick();
            var applied = variant == DiagnosticVariant.Control ? 1 : 0;
            Assert.Equal(1 - applied, enemy.Health);
            Assert.Equal(world.Tick + catalog.Tuning.World.People.AttackCooldownTicks, person.AttackTick);
            var attack = Assert.Single(observer.Result().AttackSources, row => row.ActorKind == "person");
            Assert.Equal(role, attack.Role); Assert.Equal(source, attack.SourceId);
            Assert.Equal(applied, attack.AppliedHpDamage); Assert.Equal(1 - applied, attack.SuppressedHpDamage);
            Assert.Equal(applied, world.AllyDamage + world.Tools.Values.Sum(tool => tool.GrowthDamage));
        }
    }

    [Fact]
    public void SharedBuildingAttackHelperIsNotSuppressed()
    {
        var (catalog, options, sim, observer) = Fixture(DiagnosticVariant.BothOff); var world = sim.World;
        var source = catalog.Tools.Keys.First();
        world.Buildings.Add(new() { Id = world.AllocateId(), Position = world.Estate, Source = source, Built = true, Health = 100 });
        var enemy = new EnemyState { Id = world.AllocateId(), Definition = catalog.Enemies.Keys.First(), Position = world.Estate, Health = 1 };
        world.Enemies.Add(enemy); var spatial = new SpatialHash(catalog.Tuning.World.Map.CellSize); spatial.Rebuild(world.Enemies);
        new EstateSystem(catalog, options, world, "C", spatial, sim.Runtime, sim.Experiment, observer).Tick();
        Assert.Equal(0, enemy.Health);
        Assert.Equal(1, Assert.Single(observer.Result().AttackSources).AppliedHpDamage);
        Assert.Equal("building", observer.Result().AttackSources[0].ActorKind);
        Assert.Equal(1, world.Tools[source].GrowthDamage);
    }

    [Fact]
    public void RuntimePulseRemainsSeparateAndAppliesOnlyActualEnemyHealth()
    {
        var catalog = RuntimeTests.Catalog();
        var effect = new RuntimeEffectDefinition("test:pulse", "harvest", "damage-pulse", "weapon-front", 100, 1000, 1, 0, []);
        catalog = catalog with { Runtime = catalog.Runtime! with { Items = new Dictionary<string, ItemDefinition> { ["test:item"] = new("test:item", [], [effect]) } } };
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "people");
        var observer = new DiagnosticObserver(new(DiagnosticVariant.BothOff));
        var sim = SimulationFactory.Create(catalog, options, observer); var world = sim.World;
        world.Runtime!.Items.Add("test:item", 1); world.Enemies.Clear();
        world.Destination = new(world.Lord.X + 100, world.Lord.Y);
        var enemy = new EnemyState { Id = world.AllocateId(), Definition = catalog.Enemies.Keys.First(), Position = new(world.Lord.X + 10, world.Lord.Y), Health = 3 };
        world.Enemies.Add(enemy);
        var spatial = new SpatialHash(catalog.Tuning.World.Map.CellSize); spatial.Rebuild(world.Enemies);
        var runtime = new RuntimeSystem(catalog, world, new(9000), spatial, observer);
        world.Runtime!.Items.Add("test:item", 1);
        runtime.Emit("harvest", new(world.Lord));
        Assert.Equal(0, enemy.Health); Assert.Equal(3, world.WeaponDamage);
        var source = Assert.Single(observer.Result().AttackSources);
        Assert.Equal("runtime-pulse", source.ActorKind); Assert.Equal(3, source.AppliedHpDamage); Assert.Equal(0, source.SuppressedHpDamage);
    }

    [Theory]
    [InlineData("lord")]
    [InlineData("seed")]
    [InlineData("building")]
    public void InterceptionOffDoesNotHitOutOfRangeOriginalTarget(string target)
    {
        var (catalog, options, sim, observer) = Fixture(DiagnosticVariant.InterceptionOff); var world = sim.World;
        var definition = catalog.Enemies.Values.First(enemy => enemy.Target == target);
        var far = new Position(world.Lord.X + definition.Range + 2000, world.Lord.Y);
        world.Farms.Add(new() { Id = world.AllocateId(), Position = world.Lord, Stage = 0 });
        world.Buildings.Add(new() { Id = world.AllocateId(), Position = world.Lord, Built = true, Health = 100 });
        var person = new PersonState { Id = world.AllocateId(), Position = far, Health = 100 }; world.People.Add(person);
        var enemy = new EnemyState { Id = world.AllocateId(), Definition = definition.Id, Position = far, Health = 100 }; world.Enemies.Add(enemy);
        var health = world.LordHealth;
        new CombatSystem(catalog, options, world, new(9000), new(catalog.Tuning.World.Map.CellSize), sim.Runtime, sim.Experiment, observer).ResolveEnemyAttacks();
        Assert.Equal(health, world.LordHealth); Assert.Equal(100, person.Health); Assert.Equal(100, world.Buildings[0].Health);
        Assert.Equal(0, enemy.AttackTick); Assert.Equal(0, observer.Result().Interception.InterceptCount);
        Assert.Equal(0, observer.Result().TargetDamage.Sum(row => row.AppliedTargetDamage));
    }

    [Fact]
    public void ControlGuardZeroAndExpiredTargetCacheAreObservedWithoutMutation()
    {
        var (catalog, options, sim, observer) = Fixture(DiagnosticVariant.Control); var world = sim.World;
        var person = new PersonState { Id = world.AllocateId(), Position = world.Lord, Role = "returning", Health = 100 }; world.People.Add(person);
        var state = sim.Runtime!.Entity(person.Id); state.ArrivalGuardUsed = true; state.HoldUntil = 100;
        var definition = catalog.Enemies.Values.First(enemy => enemy.Target == "lord");
        var enemy = new EnemyState { Id = world.AllocateId(), Definition = definition.Id, Position = world.Lord, Health = 100, LastTarget = "lord", TargetPosition = new(12, 34), TargetRefreshTick = 0 }; world.Enemies.Add(enemy);
        var food = world.Food;
        new CombatSystem(catalog, options, world, new(9000), new(catalog.Tuning.World.Map.CellSize), sim.Runtime, sim.Experiment, observer).ResolveEnemyAttacks();
        Assert.Equal(100, person.Health); Assert.Equal(food, world.Food); Assert.Equal(new Position(12, 34), enemy.TargetPosition); Assert.Equal(0, enemy.TargetRefreshTick);
        Assert.Equal(1, observer.Result().Interception.GuardZeroDamageCount); Assert.Equal(1, observer.Result().Interception.InterceptCount);
    }
}
