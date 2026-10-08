using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WeaponCombatTests
{
    private sealed class Arena
    {
        public ContentCatalog Catalog;
        public WorldState World;
        public CombatSystem Combat;
        public EquipmentState Weapon;
        public DiagnosticObserver Observer = new(new(DiagnosticVariant.Control));
        public TrackedRandom Random = new(9000);
        public SpatialHash Spatial;
        public Arena(string model = "sector90", int count = 2, int pierce = 0, int width = 0)
        {
            Catalog = RuntimeTests.Catalog();
            var id = Catalog.Tuning.World.Progression.StartingWeapon;
            var rows = Enumerable.Range(1, 12).Select(level => new WeaponLevelDefinition(level, 10 + level, 100, 20, count, pierce, 0)).ToArray();
            Catalog = Catalog with { Tuning = Catalog.Tuning with { DamageRollMax = 1 }, WeaponCombat = new(1, new Dictionary<string, WeaponCombatWeaponDefinition> { [id] = new(model, width, rows) }) };
            var options = new RunOptions(9000, Catalog.Tuning.DefaultHero, Catalog.Tuning.DefaultEstate, "weapon");
            var sim = SimulationFactory.Create(Catalog, options, Observer);
            World = sim.World;
            World.Enemies.Clear(); World.People.Clear(); World.Farms.Clear(); World.Buildings.Clear();
            World.Lord = new(1000, 1000); World.Destination = new(1000, 1100);
            World.WeaponCombat = new() { Facing = new(0, 1) };
            Spatial = new(Catalog.Tuning.World.Map.CellSize);
            Combat = new(Catalog, options, World, Random, Spatial, diagnostics: Observer);
            Weapon = new() { Id = id, Level = 3 };
        }
        public EnemyState Enemy(int x, int y, int health = 100)
        {
            var enemy = new EnemyState { Id = World.AllocateId(), Position = new(1000 + x, 1000 + y), Health = health };
            World.Enemies.Add(enemy); Spatial.Rebuild(World.Enemies); return enemy;
        }
        public void Fire() => Combat.Activate(Weapon, Catalog.Weapons[Weapon.Id].Activation, null);
        public DiagnosticAttackSource Attack => Assert.Single(Observer.Result().AttackSources);
    }

    [Fact]
    public void SectorUsesFacingAndAbsoluteLevelDamage()
    {
        var a = new Arena();
        var ahead = a.Enemy(0, 50); var right = a.Enemy(50, 0); var behind = a.Enemy(0, -50);
        a.Fire();
        Assert.Equal(87, ahead.Health); Assert.Equal(100, right.Health); Assert.Equal(100, behind.Health);
        Assert.Equal(20, a.Weapon.ReadyTick); Assert.Equal(1, a.Random.Draws);
        Assert.Equal(1, a.Attack.ShapeEligibleCount); Assert.Equal(1, a.Attack.EligibleBeforeCap);
    }

    [Theory]
    [InlineData("sector90", 1, 1, 0, 50, true)]
    [InlineData("sector90", 1, 1, -1, 50, false)]
    [InlineData("sector90", 0, 1, 50, 50, true)]
    [InlineData("sector90", 0, 1, 51, 50, false)]
    [InlineData("sector180", 0, 1, -50, 0, true)]
    [InlineData("sector180", 0, 1, 0, -1, false)]
    [InlineData("sector90", 0, 1, 0, 0, true)]
    [InlineData("sector90", 0, 1, 0, 100, true)]
    [InlineData("sector90", 0, 1, 0, 101, false)]
    public void SectorBoundariesAreInclusiveAndRespectSpatialRange(string model, int fx, int fy, int x, int y, bool hit)
    {
        var a = new Arena(model); a.World.WeaponCombat!.Facing = new(fx, fy);
        var enemy = a.Enemy(x, y); a.Fire(); Assert.Equal(hit ? 87 : 100, enemy.Health);
    }

    [Fact]
    public void CountCapsEligibleTargetsWhileEmptyActivationDiffersFromEmptyCandidates()
    {
        var a = new Arena(count: 1); a.Enemy(0, 10); var second = a.Enemy(0, 20); a.Enemy(0, 30);
        a.Fire(); Assert.Equal(100, second.Health); Assert.Equal(1, a.Attack.HitCount);
        Assert.Equal(3, a.Attack.EligibleBeforeCap); Assert.Equal(0, a.Attack.EmptyActivations);
        var b = new Arena(); b.Enemy(0, -10); b.Fire();
        Assert.Equal(0, b.Attack.NoTargetAttempts); Assert.Equal(1, b.Attack.EmptyActivations); Assert.Equal(0, b.Random.Draws);
        var c = new Arena(); c.Fire(); Assert.Equal(1, c.Attack.NoTargetAttempts); Assert.Equal(1, c.Attack.EmptyActivations);
    }

    [Fact]
    public void DiskIncludesInnerContactAndNearestCountWithIdTieBreak()
    {
        var a = new Arena("disk", count: 2);
        var coincident = a.Enemy(0, 0); var firstTie = a.Enemy(-1, 0); var secondTie = a.Enemy(1, 0);
        a.Fire(); Assert.Equal(87, coincident.Health); Assert.Equal(87, firstTie.Health); Assert.Equal(100, secondTie.Health);
        Assert.Equal(3, a.Attack.EligibleBeforeCap);
    }

    [Fact]
    public void RaysPierceCorridorAndDeduplicateAcrossDistinctPrimaryRays()
    {
        var a = new Arena("rays", count: 2, pierce: 1, width: 1);
        var first = a.Enemy(10, 0); var second = a.Enemy(20, 0); var third = a.Enemy(30, 1); var outside = a.Enemy(40, 2); var behind = a.Enemy(-50, 0);
        a.Fire();
        Assert.Equal(87, first.Health); Assert.Equal(87, second.Health); Assert.Equal(87, third.Health);
        Assert.Equal(100, outside.Health); Assert.Equal(100, behind.Health);
        Assert.Equal(3, a.Attack.HitCount); Assert.Equal(3, a.Attack.EligibleBeforeCap); Assert.Equal(3, a.Random.Draws);
    }

    [Fact]
    public void RayWithoutPierceStopsAtOneAndIgnoresDeadInitialTargets()
    {
        var a = new Arena("rays", count: 1, width: 1);
        a.Enemy(1, 0, 0); var first = a.Enemy(10, 0, 2); var second = a.Enemy(20, 0);
        a.Fire(); Assert.Equal(0, first.Health); Assert.Equal(100, second.Health);
        Assert.Equal(13, a.Attack.RequestedDamage); Assert.Equal(2, a.Attack.AppliedHpDamage); Assert.Equal(2, a.World.WeaponDamage);
        Assert.Equal(2, a.Attack.EligibleBeforeCap); Assert.Equal(2, a.Attack.CandidateCount);
    }

    [Fact]
    public void LargeCoordinatesUseExactRayCorridorArithmetic()
    {
        var definition = new WeaponCombatWeaponDefinition("rays", 1, []);
        var row = new WeaponLevelDefinition(1, 1, 1000000, 1, 1, 1, 0);
        var first = new EnemyState { Id = 1, Health = 1, Position = new(500000, 500000) };
        var edge = new EnemyState { Id = 2, Health = 1, Position = new(600000, 600001) };
        var outside = new EnemyState { Id = 3, Health = 1, Position = new(600000, 600002) };
        var result = WeaponTargets.Select(definition, row, new(0, 0), new(1, 0), [first, edge, outside]);
        Assert.Equal(new[] { 1, 2 }, result.Hits.Select(enemy => enemy.Id)); Assert.Equal(2, result.Eligible);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void RarityLeapClampsWeaponAndExcludesCappedCardWithoutCappingTools(int upgradeAmount)
    {
        var a = new Arena(); var catalog = a.Catalog;
        catalog = catalog with { Tuning = catalog.Tuning with { World = catalog.Tuning.World with { Progression = catalog.Tuning.World.Progression with { Rarities = [new("test:rare", 1, upgradeAmount)] } } } };
        var options = new RunOptions(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "weapon", ManualCards: true);
        var simulation = SimulationFactory.Create(catalog, options); var world = simulation.World;
        var weapon = world.Equipment.Single(e => e.Id == a.Weapon.Id); weapon.Level = 11;
        world.PendingCards = [weapon.Id]; simulation.ChooseCard(weapon.Id); Assert.Equal(12, weapon.Level);
        world.Experience = catalog.Tuning.World.Progression.BaseExperience;
        new ProgressionSystem(catalog, options, world, new(9000), simulation.Runtime).Tick();
        Assert.DoesNotContain(weapon.Id, world.PendingCards);
        var tool = world.Equipment.First(e => catalog.Tools.ContainsKey(e.Id)); tool.Level = 12;
        world.PendingCards = [tool.Id]; simulation.ChooseCard(tool.Id); Assert.Equal(12 + upgradeAmount, tool.Level);
    }

    [Fact]
    public void ActualMovementUpdatesHeadingAndStationaryTicksRetainIt()
    {
        var a = new Arena(); var catalog = a.Catalog;
        var simulation = SimulationFactory.Create(catalog, new(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "weapon"));
        var world = simulation.World; world.Equipment.Clear(); world.Enemies.Clear(); world.People.Clear(); world.Buildings.Clear();
        Assert.Equal(new Position(1, 0), world.WeaponCombat!.Facing);
        world.Tick = 1; world.Destination = new(world.Lord.X, world.Lord.Y + 1); simulation.Tick();
        Assert.Equal(new Position(0, 1), world.WeaponCombat.Facing);
        world.Destination = world.Lord; simulation.Tick(); Assert.Equal(new Position(0, 1), world.WeaponCombat.Facing);
    }
    [Fact]
    public void MultiHitActivationEmitsRuntimeEventExactlyOnceEvenWhenEmpty()
    {
        var a = new Arena("disk", count: 3);
        var effect = new RuntimeEffectDefinition("test:repair", "attack", "repair-nearest", "building", 5, 1000, 0, 0, []);
        var catalog = a.Catalog with { Runtime = a.Catalog.Runtime! with { Equipment = new Dictionary<string, EquipmentRuntimeDefinition> { [a.Weapon.Id] = new(a.Weapon.Id, [], [effect]) } } };
        var building = new BuildingState { Id = a.World.AllocateId(), Position = a.World.Lord, Built = true, Health = 1 };
        a.World.Buildings.Add(building);
        var runtime = new RuntimeSystem(catalog, a.World, a.Random, a.Spatial, a.Observer);
        var combat = new CombatSystem(catalog, new(9000, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "weapon"), a.World, a.Random, a.Spatial, runtime, diagnostics: a.Observer);
        a.Enemy(0, 1); a.Enemy(0, 2); a.Enemy(0, 3);
        combat.Activate(a.Weapon, catalog.Weapons[a.Weapon.Id].Activation, null);
        Assert.Equal(6, building.Health); Assert.Equal(1, Assert.Single(runtime.Result().Effects).ActivationCount);
        a.World.Enemies.Clear(); a.Spatial.Rebuild(a.World.Enemies);
        combat.Activate(a.Weapon, catalog.Weapons[a.Weapon.Id].Activation, null);
        Assert.Equal(11, building.Health); Assert.Equal(2, Assert.Single(runtime.Result().Effects).ActivationCount);
    }

    [Fact]
    public void LevelRowsChangeRangeCountAndFrequencyWithoutMultiplyingDamage()
    {
        var a = new Arena("disk", count: 1);
        var definition = a.Catalog.WeaponCombat!.Weapons[a.Weapon.Id];
        definition.Levels[0] = new(1, 2, 10, 40, 1, 0, 0);
        definition.Levels[1] = new(2, 3, 30, 20, 2, 0, 0);
        var first = a.Enemy(0, 5); var second = a.Enemy(0, 20);
        a.Weapon.Level = 1; a.Fire(); Assert.Equal(98, first.Health); Assert.Equal(100, second.Health); Assert.Equal(40, a.Weapon.ReadyTick);
        a.Weapon.Level = 2; a.Fire(); Assert.Equal(95, first.Health); Assert.Equal(97, second.Health); Assert.Equal(20, a.Weapon.ReadyTick);
    }

    [Fact]
    public void PiercingOrdersByForwardProjectionBeforeRadialDistance()
    {
        var a = new Arena("rays", count: 1, pierce: 1, width: 10);
        var primary = a.Enemy(10, 0); var earlierProjection = a.Enemy(11, 9); var nearerRadius = a.Enemy(12, 0);
        a.Fire();
        Assert.Equal(87, primary.Health); Assert.Equal(87, earlierProjection.Health); Assert.Equal(100, nearerRadius.Health);
    }

}
