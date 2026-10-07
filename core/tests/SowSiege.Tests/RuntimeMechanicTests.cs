using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class RuntimeMechanicTests
{
    private static RuntimeEffectDefinition Effect(string id, string trigger, string operation, string subject, int amount, int radius = 1000, int duration = 100, int food = 0, params RuntimeCondition[] conditions) => new(id, trigger, operation, subject, amount, radius, duration, food, conditions);
    private static Simulation WithItems(params RuntimeEffectDefinition[] effects)
    {
        var catalog = RuntimeTests.Catalog();
        catalog = catalog with { Runtime = catalog.Runtime! with { Items = new Dictionary<string, ItemDefinition> { ["test:item"] = new("test:item", [], effects) } } };
        var simulation = RuntimeTests.Create(catalog);
        simulation.World.Runtime!.Items.Add("test:item", 1);
        return simulation;
    }
    private static EnemyState Enemy(Simulation simulation, string target = "seed", int health = 100)
    {
        var definition = RuntimeTests.Catalog().Enemies.Values.First(enemy => enemy.Target == target);
        var enemy = new EnemyState { Id = simulation.World.AllocateId(), Definition = definition.Id, Health = health, Position = simulation.World.Lord };
        simulation.World.Enemies.Add(enemy); return enemy;
    }
    private static FarmState Farm(Simulation simulation, int stage = 0, int dx = 0)
    {
        var farm = new FarmState { Id = simulation.World.AllocateId(), Source = simulation.World.Equipment[0].Id, Position = new(simulation.World.Lord.X + dx, simulation.World.Lord.Y), Stage = stage };
        simulation.World.Farms.Add(farm); return farm;
    }

    [Fact]
    public void ModifierRequiresActualAttackingEquipmentTagAndTarget()
    {
        var simulation = WithItems(Effect("test:damage", "modifier", "stat-add", "attack-damage", 4, conditions: [new("equipment-tag", "melee"), new("enemy-target", "seed"), new("near-seed")]));
        Farm(simulation); var enemy = Enemy(simulation);
        var catalog = RuntimeTests.Catalog();
        var melee = catalog.Weapons.Values.First(weapon => weapon.Tags.Contains("melee")).Id;
        var other = catalog.Weapons.Values.First(weapon => !weapon.Tags.Contains("melee")).Id;
        Assert.Equal(14, simulation.Runtime!.Modify("attack-damage", 10, new(simulation.World.Lord, melee, enemy)));
        Assert.Equal(10, simulation.Runtime.Modify("attack-damage", 10, new(simulation.World.Lord, other, enemy)));
        Assert.Equal(1, simulation.Result().Runtime!.Effects.Single().ActivationCount);
    }

    [Fact]
    public void FoodRepairChargesOnlyRealTargetAndResumesCombat()
    {
        var simulation = WithItems(Effect("test:repair", "harvest", "repair-nearest", "building", 12, food: 2));
        var world = simulation.World; world.Food = 10; world.Lord = world.Buildings[0].Position;
        simulation.Runtime!.Emit("harvest", new(world.Lord));
        Assert.Equal(10, world.Food);
        var building = world.Buildings[0]; building.Built = true; building.Health = 0;
        simulation.Runtime.Emit("harvest", new(world.Lord));
        Assert.Equal(12, building.Health); Assert.Equal(8, world.Food);
        var enemy = Enemy(simulation); enemy.Position = building.Position;
        world.Equipment.Clear();
        var spatial = new SpatialHash(600); spatial.Rebuild(world.Enemies);
        new EstateSystem(RuntimeTests.Catalog(), new(42, "core:founder", "core:meadow", "mixed"), world, "C", spatial, simulation.Runtime).Tick();
        Assert.True(world.AllyDamage > 0);
        Assert.True(enemy.Health < 100);
    }

    [Fact]
    public void WorkerMealUsesFoodAndActuallySpeedsGrowth()
    {
        var simulation = WithItems(Effect("test:meal", "harvest", "worker-buff", "people", 3, food: 2));
        var farm = Farm(simulation); var world = simulation.World; world.Food = 10;
        var catalog = RuntimeTests.Catalog();
        var estate = new EstateSystem(catalog, new(42, "core:founder", "core:meadow", "mixed"), world, "C", new(600), simulation.Runtime);
        estate.Tick(); var baseline = farm.Progress; farm.Progress = 0;
        simulation.Runtime!.Emit("harvest", new(world.Lord)); estate.Tick();
        Assert.True(farm.Progress > baseline); Assert.Equal(8, world.Food);
        Assert.True(world.People.Count(person => person.DutyUntil > world.Tick) > 0);
        Assert.True(world.Food / catalog.Tuning.World.People.FoodPerPerson < 10 / catalog.Tuning.World.People.FoodPerPerson);
    }

    [Fact]
    public void ShieldConsumptionPausesNeighborOnlyOnProtectedHit()
    {
        var simulation = WithItems(Effect("test:shield", "plant", "shield-farms", "land", 1, radius: 0), Effect("test:delay", "farm-hit", "pause-neighbor-growth", "land", 1, conditions: [new("shield-consumed")]));
        var farm = Farm(simulation); var neighbor = Farm(simulation, dx: 300); var enemy = Enemy(simulation);
        Assert.False(simulation.Runtime!.ShieldFarm(farm, enemy)); Assert.Equal(0, simulation.Runtime.Entity(neighbor.Id).PauseUntil);
        simulation.Runtime.Emit("plant", new(farm.Position, Farm: farm));
        Assert.True(simulation.Runtime.ShieldFarm(farm, enemy)); Assert.True(simulation.Runtime.Entity(neighbor.Id).PauseUntil > 0);
        Assert.False(simulation.Runtime.ShieldFarm(farm, enemy));
        Assert.Equal(1, simulation.Result().Runtime!.Effects.Single(effect => effect.EffectId == "test:delay").ActivationCount);
    }

    [Fact]
    public void ReturnWaypointAndGuardDelayConserveHealthAndPopulation()
    {
        var simulation = WithItems(Effect("test:route", "return-start", "return-via-building", "building", 1), Effect("test:guard", "return-arrival", "guard-return", "people", 1, duration: 10));
        var world = simulation.World; world.People.Clear();
        var person = new PersonState { Id = world.AllocateId(), Role = "returning", Health = 20, Members = 2, Position = new(world.Estate.X + 300, world.Estate.Y) }; world.People.Add(person);
        var building = world.Buildings[0]; building.Built = true; building.Health = 100; building.Position = new(world.Estate.X + 150, world.Estate.Y + 150);
        simulation.Runtime!.Emit("return-start", new(person.Position, Person: person));
        Assert.Equal(building.Position, simulation.Runtime.Entity(person.Id).Waypoint);
        person.Position = world.Estate; simulation.Runtime.Entity(person.Id).Waypoint = null;
        var estate = new EstateSystem(RuntimeTests.Catalog(), new(42, "core:founder", "core:meadow", "mixed"), world, "C", new(600), simulation.Runtime);
        estate.Tick(); Assert.Equal("returning", person.Role); Assert.Single(world.People);
        world.Tick = 11; estate.Tick(); Assert.All(world.People, worker => Assert.Equal("peasant", worker.Role));
        Assert.Equal(2, world.People.Sum(worker => worker.Members)); Assert.Equal(20, world.People.Sum(worker => worker.Health));
    }

    [Fact]
    public void RecentExperienceAffectsOfferWeightAndExpires()
    {
        var simulation = RuntimeTests.Create(RuntimeTests.Catalog());
        Assert.Equal(1, simulation.Runtime!.OfferWeight("weapon"));
        simulation.Runtime.Experience("weapon", 50);
        Assert.Equal(6, simulation.Runtime.OfferWeight("weapon")); Assert.Equal(1, simulation.Runtime.OfferWeight("land"));
        simulation.World.Tick = 301; Assert.Equal(1, simulation.Runtime.OfferWeight("weapon"));
    }

    [Fact]
    public void InventoryRecentExperienceAndTemporaryStateChangeTheFullHash()
    {
        var simulation = WithItems(Effect("test:modifier", "modifier", "stat-add", "attack-damage", 1));
        var state = simulation.World.Runtime!;
        var before = simulation.Result().Hash; state.Items["test:item"]++;
        Assert.NotEqual(before, simulation.Result().Hash);
        before = simulation.Result().Hash; state.Experience.Add(new(0, "weapon", 1));
        Assert.NotEqual(before, simulation.Result().Hash);
        before = simulation.Result().Hash; simulation.Runtime!.Entity(simulation.World.People[0].Id).HoldUntil = 1;
        Assert.NotEqual(before, simulation.Result().Hash);
    }

    [Fact]
    public void CharterSlotsAndEquipmentSlotsRejectNewButPermitOwnedUpgrades()
    {
        var catalog = RuntimeTests.Catalog();
        var weapon = catalog.Weapons.Values.First(); var tool = catalog.Tools.Values.First();
        var weapons = Enumerable.Range(0, 6).ToDictionary(i => $"test:w{i}", i => weapon with { Id = $"test:w{i}" });
        var tools = Enumerable.Range(0, 5).ToDictionary(i => $"test:t{i}", i => tool with { Id = $"test:t{i}" });
        var charters = Enumerable.Range(0, 5).ToDictionary(i => $"test:c{i}", i => new CharterDefinition($"test:c{i}", "weapon", []));
        catalog = catalog with { Weapons = weapons, Tools = tools, Runtime = catalog.Runtime! with { Charters = charters } };
        var world = new WorldState { Runtime = new(), Experience = catalog.Tuning.World.Progression.BaseExperience };
        foreach (var id in weapons.Keys.Take(5).Concat(tools.Keys.Take(4))) { world.Equipment.Add(new() { Id = id }); }
        foreach (var id in charters.Keys.Take(4)) { world.Runtime.Charters.Add(id, 1); }
        var options = new RunOptions(42, "core:founder", "core:meadow", "mixed", ManualCards: true);
        var runtime = new RuntimeSystem(catalog, world, new(42), new(600));
        foreach (var id in charters.Keys.Take(4)) { world.Runtime!.Charters.Add(id, 1); }
        var progression = new ProgressionSystem(catalog, options, world, new(42), runtime);
        for (var i = 0; i < 30; i++)
        {
            world.Experience = long.MaxValue / 2; progression.Tick();
            Assert.DoesNotContain("test:w5", world.PendingCards); Assert.DoesNotContain("test:t4", world.PendingCards); Assert.DoesNotContain("test:c4", world.PendingCards);
            Assert.NotEmpty(world.PendingCards); progression.Select(world.PendingCards[0]);
        }
        Assert.True(world.Equipment.Any(equipment => equipment.Level > 1) || world.Runtime!.Charters.Values.Any(rank => rank > 1));
    }
    [Fact]
    public void DurationItemsHaveRealTwoStackBenefit()
    {
        var simulation = WithItems(Effect("test:duty", "draft", "extend-duty", "people", 1, duration: 100), Effect("test:arrival", "return-arrival", "guard-return", "people", 1, duration: 20));
        var person = simulation.World.People[1]; person.DutyUntil = 10;
        simulation.Runtime!.Emit("draft", new(person.Position, Person: person)); Assert.Equal(110, person.DutyUntil);
        simulation.World.Runtime!.Items["test:item"] = 2; person.DutyUntil = 10;
        simulation.Runtime.Emit("draft", new(person.Position, Person: person)); Assert.Equal(210, person.DutyUntil);
        simulation.Runtime.Emit("return-arrival", new(person.Position, Person: person)); Assert.Equal(40, simulation.Runtime.Entity(person.Id).HoldUntil);
    }

    [Fact]
    public void NorthFacingHarvestPulseHitsNorthOnlyAndCreditsActualHealthOnce()
    {
        var simulation = WithItems(Effect("test:pulse", "harvest", "damage-pulse", "weapon-front", 100));
        var world = simulation.World; world.People.Clear(); world.Destination = new(world.Lord.X, world.Lord.Y + 100);
        var north = Enemy(simulation, health: 3); north.Position = new(world.Lord.X, world.Lord.Y + 20);
        var south = Enemy(simulation, health: 100); south.Position = new(world.Lord.X, world.Lord.Y - 20);
        // Runtime's spatial index is refreshed by the genuine attack loop; suppress all base attacks.
        foreach (var equipment in world.Equipment) { equipment.ReadyTick = int.MaxValue; }
        world.Tick = 1; simulation.Tick();
        north.Position = new(world.Lord.X, world.Lord.Y + 20); south.Position = new(world.Lord.X, world.Lord.Y - 20);
        simulation.Runtime!.Emit("harvest", new(world.Lord)); simulation.Runtime.Emit("harvest", new(world.Lord));
        Assert.Equal(0, north.Health); Assert.Equal(100, south.Health); Assert.Equal(3, world.WeaponDamage);
    }

    [Fact]
    public void EvolutionRequiresBothInputsAndPlantsActualAttackPath()
    {
        var catalog = RuntimeTests.Catalog(); var weapon = catalog.Weapons.Values.First().Id; var tool = catalog.Tools.Values.First(tool => tool.Growth.Target == "land").Id;
        catalog = catalog with { Runtime = catalog.Runtime! with { Evolutions = new Dictionary<string, EvolutionDefinition> { ["test:evo"] = new("test:evo", "weapon-tool", [weapon, tool], weapon, [Effect("test:line", "attack", "plant-path", tool, 1)]) } } };
        var simulation = RuntimeTests.Create(catalog); simulation.World.Equipment.Clear(); simulation.World.Equipment.Add(new() { Id = weapon });
        simulation.Runtime!.UnlockEvolutions(); Assert.Empty(simulation.Result().Runtime!.Build.Evolutions);
        simulation.World.Equipment.Add(new() { Id = tool }); simulation.Runtime.UnlockEvolutions(); Assert.Single(simulation.Result().Runtime!.Build.Evolutions);
        var enemy = Enemy(simulation); enemy.Position = new(simulation.World.Lord.X + 800, simulation.World.Lord.Y);
        simulation.Runtime.Emit("attack", new(simulation.World.Lord, weapon, enemy));
        var farm = Assert.Single(simulation.World.Farms); Assert.True(farm.Position.X > simulation.World.Lord.X); Assert.Equal(simulation.World.Lord.Y, farm.Position.Y);
        Assert.Equal(1, simulation.Result().Runtime!.Effects.Single().ActivationCount);
    }

    [Fact]
    public void LootRequiresDistanceTagsAndPaymentWithoutChargingFailedPickup()
    {
        var catalog = RuntimeTests.Catalog();
        catalog = catalog with { Runtime = catalog.Runtime! with { Tuning = catalog.Runtime.Tuning with { LootPeriodTicks = 100, LootSources = [new("test:market", "market", 1, 2)] }, Items = new Dictionary<string, ItemDefinition> { ["test:item"] = new("test:item", ["melee"], []) } } };
        var simulation = RuntimeTests.Create(catalog); var world = simulation.World; world.Tick = 1; world.Food = 1;
        world.Runtime!.GroundLoot.Add(new(world.AllocateId(), "test:market", "test:item", new(world.Lord.X + 1000, world.Lord.Y)));
        simulation.Runtime!.Tick(); Assert.Empty(world.Runtime.Items); Assert.Equal(1, world.Food);
        world.Runtime.GroundLoot[0] = world.Runtime.GroundLoot[0] with { Position = world.Lord };
        simulation.Runtime.Tick(); Assert.Empty(world.Runtime.Items); Assert.Equal(1, world.Food);
        world.Food = 10; world.Equipment.Clear(); simulation.Runtime.Tick(); Assert.Empty(world.Runtime.Items); Assert.Equal(10, world.Food);
        world.Equipment.Add(new() { Id = catalog.Weapons.Values.First(weapon => weapon.Tags.Contains("melee")).Id }); simulation.Runtime.Tick();
        Assert.Equal(1, world.Runtime.Items["test:item"]); Assert.Equal(8, world.Food);
        var pickup = Assert.Single(world.Runtime.Loot); Assert.Equal(2, pickup.FoodPaid); Assert.Equal("test:market", pickup.SourceId);
    }

    [Fact]
    public void DirectionalProtectionReducesFrontButExposesRear()
    {
        var simulation = WithItems(Effect("test:front", "modifier", "stat-add", "building-front-damage", -2), Effect("test:rear", "modifier", "stat-add", "building-rear-damage", 2));
        var building = simulation.World.Buildings[0]; var enemy = Enemy(simulation); simulation.Runtime!.Entity(building.Id).Facing = new(0, 1);
        enemy.Position = new(building.Position.X, building.Position.Y + 1); Assert.Equal(8, simulation.Runtime.BuildingDamage(building, enemy, 10));
        enemy.Position = new(building.Position.X, building.Position.Y - 1); Assert.Equal(12, simulation.Runtime.BuildingDamage(building, enemy, 10));
    }

    [Fact]
    public void ScytheActuallyHarvestsAndLosesImmatureProgress()
    {
        var simulation = WithItems(Effect("test:harvest", "attack", "harvest-near", "land", 1), Effect("test:cost", "attack", "damage-young-plots", "land", 1));
        var ripe = Farm(simulation, stage: 3); var young = Farm(simulation, dx: 300); young.Progress = 50;
        var before = simulation.World.Harvests; simulation.Runtime!.Emit("attack", new(simulation.World.Lord));
        Assert.Equal(before + 1, simulation.World.Harvests); Assert.Equal(0, ripe.Stage); Assert.Equal(0, young.Progress);
        Assert.True(simulation.World.HarvestExperience > 0);
    }

    [Fact]
    public void RallyCreatesActualCombatWhileDelayingWorkerReturn()
    {
        var simulation = WithItems(Effect("test:rally", "estate-cross", "rally-returners", "people", 1, duration: 10));
        var world = simulation.World; world.People.Clear();
        var person = new PersonState { Id = world.AllocateId(), Role = "returning", Position = world.Estate, Health = 100 }; world.People.Add(person);
        var enemy = Enemy(simulation); var spatial = new SpatialHash(600); spatial.Rebuild(world.Enemies);
        simulation.Runtime!.Emit("estate-cross", new(world.Estate));
        var estate = new EstateSystem(RuntimeTests.Catalog(), new(42, "core:founder", "core:meadow", "mixed"), world, "C", spatial, simulation.Runtime);
        estate.Tick(); Assert.Equal("returning", person.Role); Assert.True(enemy.Health < 100);
        world.Tick = 11; estate.Tick(); Assert.Equal("peasant", person.Role);
    }

    [Fact]
    public void ToolToolEvolutionShieldsOnlyFarmsNearRealRepairedBuilding()
    {
        var catalog = RuntimeTests.Catalog(); var land = catalog.Tools.Values.First(tool => tool.Growth.Target == "land").Id; var hammer = catalog.Tools.Values.First(tool => tool.Growth.Target == "building").Id;
        catalog = catalog with { Runtime = catalog.Runtime! with { Evolutions = new Dictionary<string, EvolutionDefinition> { ["test:shelter"] = new("test:shelter", "tool-tool", [land, hammer], land, [Effect("test:shelter_effect", "repair", "shield-farms", "land", 1, radius: 300)]) } } };
        var simulation = RuntimeTests.Create(catalog); var world = simulation.World; world.Equipment.Clear(); world.Equipment.Add(new() { Id = land });
        var building = world.Buildings[0]; world.Lord = building.Position; var near = Farm(simulation); var far = Farm(simulation, dx: 1000);
        simulation.Runtime!.UnlockEvolutions(); Assert.Empty(world.Runtime!.Evolutions);
        world.Equipment.Add(new() { Id = hammer }); simulation.Runtime.UnlockEvolutions();
        var estate = new EstateSystem(catalog, new(42, "core:founder", "core:meadow", "mixed"), world, "C", new(600), simulation.Runtime); estate.ApplyGrowth(catalog.Tools[hammer]);
        Assert.True(building.Health > 0); Assert.Equal(1, simulation.Runtime.Entity(near.Id).Shield); Assert.Equal(0, simulation.Runtime.Entity(far.Id).Shield);
    }

    [Fact]
    public void HeldReturningSquadHonorsAttackCooldown()
    {
        var simulation = WithItems(Effect("test:rally", "estate-cross", "rally-returners", "people", 1, duration: 100));
        var world = simulation.World; world.People.Clear();
        var person = new PersonState { Id = world.AllocateId(), Role = "returning", Position = world.Estate, Health = 100 }; world.People.Add(person);
        var enemy = Enemy(simulation); var spatial = new SpatialHash(600); spatial.Rebuild(world.Enemies);
        simulation.Runtime!.Emit("estate-cross", new(world.Estate));
        var estate = new EstateSystem(RuntimeTests.Catalog(), new(42, "core:founder", "core:meadow", "mixed"), world, "C", spatial, simulation.Runtime);
        estate.Tick(); var health = enemy.Health; world.Tick++; estate.Tick(); Assert.Equal(health, enemy.Health);
    }

    [Fact]
    public void AttackHarvestDispatchesDistinctCharterHarvestTrigger()
    {
        var catalog = SowSiege.Sim.ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "s4-stage-one");
        var simulation = RuntimeTests.Create(catalog); var world = simulation.World;
        world.Equipment.Add(new() { Id = "core:harvest_scythe", ReadyTick = int.MaxValue });
        world.Runtime!.Charters.Add("core:guarded_harvest", 1);
        world.People.Clear(); world.Tick = 1; world.Destination = new(world.Lord.X + 100, world.Lord.Y);
        var enemy = Enemy(simulation); enemy.Position = new(world.Lord.X + 30, world.Lord.Y);
        foreach (var equipment in world.Equipment) { equipment.ReadyTick = int.MaxValue; }
        simulation.Tick(); var farm = Farm(simulation, stage: 3);
        var health = enemy.Health;
        simulation.Runtime!.Emit("attack", new(world.Lord, "core:harvest_scythe", enemy));
        Assert.Equal(0, farm.Stage); Assert.True(enemy.Health < health); Assert.True(world.WeaponDamage > 0);
        Assert.True(simulation.Result().Runtime!.Effects.Single(effect => effect.EffectId == "core:guarded_harvest_strike").ActivationCount > 0);
    }

}
