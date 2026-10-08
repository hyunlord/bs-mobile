using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;
public sealed class FirstPlayableMechanicsTests
{
    private static (InteractiveSession S, CombatSystem Combat, EquipmentState Weapon) Arena(string form, int count = 1, int pierce = 0)
    {
        var c = FirstPlayableTests.Catalog(form); var id = c.Tuning.World.Progression.StartingWeapon;
        var weapons = c.WeaponCombat!.Weapons.ToDictionary(p => p.Key, p => p.Value);
        weapons[id] = weapons[id] with { Levels = weapons[id].Levels.Select(l => l with { Range = 1000, Damage = 10, Knockback = 0, Count = count, Pierce = pierce }).ToArray() };
        c = c with { WeaponCombat = c.WeaponCombat with { Weapons = weapons } };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World; w.People.Clear(); w.Equipment.RemoveAll(e => e.Id != id); w.Enemies.Clear();
        var spatial = new SpatialHash(600); var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
        foreach (var offset in new[] { 200, 400, 600 })
        {
            w.Enemies.Add(new() { Id = w.AllocateId(), Definition = c.Enemies.Keys.First(), Position = new(w.Lord.X + offset, w.Lord.Y), Health = 10000 });
        }

        spatial.Rebuild(w.Enemies); return (s, combat, w.Equipment.Single());
    }
    [Fact]
    public void ChainJumpsToDistinctNeighborsAndStopsAtCount()
    {
        var (s, combat, weapon) = Arena("chain", 2); combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks();
        Assert.Equal(2, s.Simulation.World.Enemies.Count(e => e.Health < 10000));
        Assert.Empty(s.Simulation.World.FirstPlayable!.Attacks);
    }
    [Fact]
    public void PiercingHitsExactlyPiercePlusOneAndSingleProjectileDoesNot()
    {
        foreach (var pierce in new[] { 0, 1 })
        {
            var (s, combat, weapon) = Arena("piercing", 1, pierce); combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null);
            for (var i = 0; i < 12; i++) { combat.TickFirstPlayableAttacks(); s.Simulation.World.Tick++; }
            Assert.Equal(pierce + 1, s.Simulation.World.Enemies.Count(e => e.Health < 10000));
        }
    }
    [Fact]
    public void BoomerangReturnsAndCanHitSameEnemyOnReturn()
    {
        var (s, combat, weapon) = Arena("boomerang"); combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null);
        for (var i = 0; i < 3; i++) { combat.TickFirstPlayableAttacks(); s.Simulation.World.Tick++; }
        var firstDamage = 10000 - s.Simulation.World.Enemies[0].Health;
        Assert.True(firstDamage > 0);
        for (var i = 3; i < 90; i++) { combat.TickFirstPlayableAttacks(); s.Simulation.World.Tick++; }
        Assert.True(10000 - s.Simulation.World.Enemies[0].Health > firstDamage);
        Assert.Empty(s.Simulation.World.FirstPlayable!.Attacks);
    }
    [Fact]
    public void OrbitMovesAroundLordAndFieldStaysAtOriginUntilExpiry()
    {
        foreach (var form in new[] { "orbit", "field" })
        {
            var (s, combat, weapon) = Arena(form); combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks();
            var a = s.Simulation.World.FirstPlayable!.Attacks.Single(); var first = a.Position;
            for (var i = 1; i < 20; i++) { s.Simulation.World.Tick++; combat.TickFirstPlayableAttacks(); }
            if (form == "orbit")
            {
                Assert.NotEqual(first, a.Position);
            }
            else
            {
                Assert.Equal(first, a.Position);
            }

            for (var i = 20; i < 90; i++) { s.Simulation.World.Tick++; combat.TickFirstPlayableAttacks(); }
            Assert.Empty(s.Simulation.World.FirstPlayable.Attacks);
        }
    }
    [Fact]
    public void EventsRequireTheirOwnInteractionAndCartDamageBeforeDrop()
    {
        var c = FirstPlayableTests.Catalog("field");
        c = c with
        {
            FirstPlayable = c.FirstPlayable! with
            {
                MapEvents = new[] {
            new MapEventDefinition("test:merchant", "merchant", 0, 0, 1000, 0, 100, 5, 1, 0, 0, 0, Array.Empty<string>()),
            new MapEventDefinition("test:shrine", "shrine", 0, 0, 1000, 0, 100, 0, 1, 50, 10, 0, Array.Empty<string>()),
            new MapEventDefinition("test:cart", "cart", 0, 0, 1000, 0, 100, 0, 2, 0, 0, 1, Array.Empty<string>()) }
            }
        };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World; w.LordHealth -= 100; var food = w.Food;
        s.Simulation.Runtime!.Tick(); Assert.Equal(food - 5, w.Food); Assert.Equal(c.Tuning.World.Map.LordHealth - 50, w.LordHealth); Assert.Equal(2, w.Runtime!.Loot.Count);
        var cart = Assert.Single(w.FirstPlayable!.MapEvents); Assert.Equal("test:cart", cart.Definition);
        var spatial = new SpatialHash(600); var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
        var weapon = w.Equipment.Single(e => c.Weapons.ContainsKey(e.Id)); combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks();
        Assert.Equal(0, cart.Health); w.Tick++; s.Simulation.Runtime.Tick(); Assert.Empty(w.FirstPlayable.MapEvents); Assert.Equal(4, w.Runtime.Loot.Count);
        Assert.Contains(s.State.Events, e => e.Kind == PresentationKind.CartBroken);
    }
    [Fact]
    public void BossArrivesAtScheduleWarnsAndOnlyDefeatCountsAsVictory()
    {
        var c = FirstPlayableTests.Catalog(); var id = c.Enemies.Keys.First(); var enemies = c.FirstPlayable!.Enemies.ToDictionary(p => p.Key, p => p.Value);
        enemies[id] = new("boss", 10, 0, 0); c = c with { FirstPlayable = c.FirstPlayable with { Enemies = enemies } };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World; w.Tick = 10; s.Apply(new(0, 10, ReplayCommandKind.Advance));
        var boss = Assert.Single(s.View.CaptureFirstPlayable()!.Bosses); Assert.Equal("active", boss.State); Assert.False(s.GetSummary().BossDefeated);
        Assert.Contains(s.State.Events, e => e.Kind == PresentationKind.BossWarning);
        w.Enemies.Single(e => e.Id == boss.EntityId).Health = 0; s.Apply(new(1, 11, ReplayCommandKind.Advance));
        Assert.True(s.GetSummary().BossDefeated); Assert.Equal("defeated", s.View.CaptureFirstPlayable()!.Bosses.Single().State);
    }
    [Fact]
    public void BuildingRequiresFourWorkActivationsAndShowsConstructionBeforeCompletion()
    {
        var c = FirstPlayableTests.Catalog(); var s = FirstPlayableTests.Session(c); var w = s.Simulation.World;
        var tool = c.Tools.Values.First(t => t.Growth.Target == "building"); var building = w.Buildings[0]; w.Lord = building.Position;
        var estate = new EstateSystem(c, s.Options.Run, w, "C", new SpatialHash(600), s.Simulation.Runtime, interactive: s.State);
        estate.ApplyGrowth(tool); Assert.False(building.Built); Assert.Equal("constructing", s.View.CaptureFirstPlayable()!.BuildingProgress.Single(b => b.Id == building.Id).State);
        estate.ApplyGrowth(tool); estate.ApplyGrowth(tool); estate.ApplyGrowth(tool); Assert.True(building.Built);
        building.Health = 0; estate.ApplyGrowth(tool); Assert.True(building.Built); Assert.Equal(0, w.Rebuilds);
        Assert.Equal("constructing", s.View.CaptureFirstPlayable()!.BuildingProgress.Single(b => b.Id == building.Id).State);
    }
    [Fact]
    public void RipeEvolutionThresholdIsCheckedAndAllNewHiddenFieldsChangeHash()
    {
        var c = FirstPlayableTests.Catalog(); var id = c.Runtime!.Evolutions.Keys.First(); c = c with { FirstPlayable = c.FirstPlayable! with { EvolutionGrowthRequirements = new Dictionary<string, EvolutionGrowthRequirement> { [id] = new("ripe", 12) } } };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World;
        foreach (var input in c.Runtime.Evolutions[id].InputIds)
        {
            if (!w.Equipment.Any(e => e.Id == input))
            {
                w.Equipment.Add(new() { Id = input });
            }
        }

        foreach (var equipment in w.Equipment)
        {
            equipment.Level = 12;
        }

        Assert.False(RuntimeSystem.EvolutionEligible(c, w, c.Runtime.Evolutions[id]));
        for (var i = 0; i < 12; i++)
        {
            w.Farms.Add(new() { Id = w.AllocateId(), Stage = 3 });
        }

        Assert.True(RuntimeSystem.EvolutionEligible(c, w, c.Runtime.Evolutions[id]));
        var mutations = new Action<FirstPlayableState>[] {
            fp => fp.NextEnemySpawn["test:enemy"] = 2, fp => fp.NextEventSpawn["test:event"] = 2,
            fp => fp.BuildingWork[1] = 3, fp => fp.OfferedRarities["test:card"] = new("rare", 1, 2),
            fp => fp.PersonActivities[1] = "work", fp => fp.BossEntities["test:boss"] = 3,
            fp => fp.DefeatedBosses.Add("test:boss"), fp => fp.Coverage["test:counter"] = 1,
            fp => fp.Kills++, fp => fp.MapEvents.Add(new() { Id = 1, Health = 2 }),
            fp => fp.Attacks.Add(new() { Id = 1, HitTicks = new() { [2] = 3 } }) };
        foreach (var mutation in mutations) { var before = s.ComputeStateHash(); mutation(w.FirstPlayable!); Assert.NotEqual(before, s.ComputeStateHash()); }
    }
    [Fact]
    public void VolleyEmptyCountsOneCompletedActivationRatherThanItsThreeChildren()
    {
        var (s, combat, weapon) = Arena("volley", 3); s.Simulation.World.Enemies.ForEach(e => e.Health = 0);
        combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null);
        Assert.Equal(3, s.Simulation.World.FirstPlayable!.Attacks.Count);
        for (var i = 0; i < 90; i++) { combat.TickFirstPlayableAttacks(); s.Simulation.World.Tick++; }
        var fp = s.Simulation.World.FirstPlayable;
        Assert.Equal(1, fp.Coverage["equipment:" + weapon.Id + ":activation"]); Assert.Equal(1, fp.Coverage["equipment:" + weapon.Id + ":completed"]); Assert.Equal(1, fp.Coverage["equipment:" + weapon.Id + ":empty"]); Assert.Empty(fp.Activations);
    }
    [Fact]
    public void CappedAttackWithNoChildrenCompletesAndGroupStateIsHashBound()
    {
        var c = FirstPlayableTests.Catalog("volley"); c = c with { FirstPlayable = c.FirstPlayable! with { MaxActiveAttacks = 1 } };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World; var spatial = new SpatialHash(600);
        var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime); var weapon = w.Equipment.Single(e => c.Weapons.ContainsKey(e.Id));
        combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null); combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null);
        Assert.Equal(1, w.FirstPlayable!.Coverage["equipment:" + weapon.Id + ":completed"]);
        var hash = s.ComputeStateHash(); w.FirstPlayable.Activations.Values.Single().HadHit = true; Assert.NotEqual(hash, s.ComputeStateHash());
        for (var i = 0; i < 90; i++) { combat.TickFirstPlayableAttacks(); w.Tick++; }
        Assert.Empty(w.FirstPlayable.Activations); Assert.Equal(2, w.FirstPlayable.Coverage["equipment:" + weapon.Id + ":completed"]);
    }
    [Fact]
    public void BossStillSpawnsWhenEnemyCapIsPermanentlyFull()
    {
        var c = FirstPlayableTests.Catalog(); var bossId = c.Enemies.Keys.First(); var normalId = c.Enemies.Keys.Skip(1).First();
        var definitions = c.FirstPlayable!.Enemies.ToDictionary(p => p.Key, p => p.Value); definitions[bossId] = new("boss", 10, 0, 0);
        c = c with { FirstPlayable = c.FirstPlayable with { Enemies = definitions }, Tuning = c.Tuning with { World = c.Tuning.World with { Threat = c.Tuning.World.Threat with { EnemyCap = 1 } } } };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World; w.Tick = 10; w.Enemies.Add(new() { Id = w.AllocateId(), Definition = normalId, Health = 100, Position = new(0, 0) });
        var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, new SpatialHash(600)); combat.SpawnAndMoveEnemies();
        Assert.Equal(bossId, Assert.Single(w.Enemies).Definition); Assert.Equal(0, w.FirstPlayable!.Kills);
    }
    [Fact]
    public void PeopleTargetMovesTowardWorkerRatherThanLord()
    {
        var c = FirstPlayableTests.Catalog(); var enemies = c.Enemies.ToDictionary(p => p.Key, p => p.Value); var id = enemies.Keys.First(); enemies[id] = enemies[id] with { Target = "people" }; c = c with { Enemies = enemies };
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World; w.Tick = 1; w.People.Clear(); w.People.Add(new() { Id = w.AllocateId(), Position = new(w.Lord.X - 1000, w.Lord.Y), Health = 100 });
        var enemy = new EnemyState { Id = w.AllocateId(), Definition = id, Position = new(w.Lord.X - 500, w.Lord.Y), Health = 100 }; w.Enemies.Add(enemy);
        var oldX = enemy.Position.X; var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, new SpatialHash(600)); combat.SpawnAndMoveEnemies();
        Assert.True(enemy.Position.X < oldX); Assert.Equal("person", enemy.LastTarget);
    }
    [Fact]
    public void MissedSwordCannotPlantButActualForwardHitCan()
    {
        foreach (var forward in new[] { false, true })
        {
            var (s, combat, weapon) = Arena("sector90"); var w = s.Simulation.World;
            w.Runtime!.Evolutions.Add("core:sowing_sworddance"); w.Equipment.Add(new() { Id = "core:seed_bag", Level = 3 }); w.Enemies.RemoveRange(1, 2);
            w.Enemies[0].Position = new(w.Lord.X + (forward ? 200 : -200), w.Lord.Y);
            var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies);
            combat = new CombatSystem(s.Catalog, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime);
            var before = w.Farms.Count; combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks();
            Assert.Equal(before + (forward ? 1 : 0), w.Farms.Count);
        }
    }

    [Theory]
    [InlineData("projectile")]
    [InlineData("piercing")]
    [InlineData("field")]
    [InlineData("nova")]
    public void CountGrowthCreatesActualAttacksForAllMultiObjectForms(string form)
    {
        var (s, combat, weapon) = Arena(form, 3); combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null);
        Assert.Equal(3, s.Simulation.World.FirstPlayable!.Attacks.Count);
        Assert.Single(s.Simulation.World.FirstPlayable.Activations);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NonPiercingProjectileSharesBudgetAcrossCartAndEnemy(bool cartFirst)
    {
        var (s, combat, weapon) = Arena("projectile"); var w = s.Simulation.World;
        w.Enemies.RemoveRange(1, 2); w.Enemies[0].Position = new(w.Lord.X + (cartFirst ? 400 : 200), w.Lord.Y);
        w.FirstPlayable!.MapEvents.Add(new() { Id = w.AllocateId(), Definition = "test:cart", Position = new(w.Lord.X + (cartFirst ? 200 : 400), w.Lord.Y), Health = 10000 });
        var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies); combat = new(s.Catalog, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
        combat.Activate(weapon, s.Catalog.Weapons[weapon.Id].Activation, null);
        for (var tick = 0; tick < 12; tick++) { combat.TickFirstPlayableAttacks(); w.Tick++; }
        if (cartFirst)
        {
            Assert.Equal(10000, w.Enemies[0].Health);
        }
        else
        {
            Assert.True(w.Enemies[0].Health < 10000);
        }

        Assert.Equal(cartFirst ? 1 : 0, w.FirstPlayable.MapEvents.Count(e => e.Health < 10000));
        Assert.Equal(1, w.FirstPlayable.Coverage["equipment:" + weapon.Id + ":hit"]);
        Assert.Equal((10000 - w.Enemies[0].Health) + (10000 - w.FirstPlayable.MapEvents[0].Health), w.WeaponDamage);
    }

    [Fact]
    public void CartUsesModifiersHitIntervalsPresentationAndBoundedDamageLedger()
    {
        var damages = new List<long>();
        foreach (var boosted in new[] { false, true })
        {
            var (s, _, weapon) = Arena("field"); var w = s.Simulation.World; w.Enemies.Clear();
            var effect = new RuntimeEffectDefinition("test:damage", "modifier", "stat-add", "attack-damage", 100, 0, 0, 0, Array.Empty<RuntimeCondition>());
            var runtimeCatalog = s.Catalog.Runtime! with { Charters = new Dictionary<string, CharterDefinition> { ["test:charter"] = new("test:charter", "weapon", new[] { effect }) } };
            var c = s.Catalog with { Runtime = runtimeCatalog };
            var spatial = new SpatialHash(600); var runtime = new RuntimeSystem(c, w, s.Simulation.RandomState, spatial);
            if (boosted)
            {
                w.Runtime!.Charters["test:charter"] = 1;
            }

            var observer = new DiagnosticObserver(new(DiagnosticVariant.Control)); observer.Attach(c, w);
            var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, runtime, diagnostics: observer, interactive: s.State);
            var cart = new MapEventState { Id = w.AllocateId(), Definition = "test:cart", Position = new(w.Lord.X + 200, w.Lord.Y), Health = 10000 }; w.FirstPlayable!.MapEvents.Add(cart);
            combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks(); var first = w.WeaponDamage;
            Assert.True(first > 0); damages.Add(first);
            Assert.Contains(s.State.Events, e => e.HitEntityIds.Contains(cart.Id) && e.Endpoints.Any(p => p.X == cart.Position.X && p.Y == cart.Position.Y));
            for (var tick = 1; tick < 10; tick++) { w.Tick = tick; combat.TickFirstPlayableAttacks(); }
            Assert.Equal(first, w.WeaponDamage);
            w.Tick = 10; combat.TickFirstPlayableAttacks(); Assert.True(w.WeaponDamage > first);
            cart.Health = 1; var before = w.WeaponDamage; w.Tick = 20; combat.TickFirstPlayableAttacks();
            Assert.Equal(0, cart.Health); Assert.Equal(before + 1, w.WeaponDamage);
            Assert.Equal(w.WeaponDamage, observer.Result().AttackSources.Single().AppliedHpDamage);
            Assert.True(w.FirstPlayable.Coverage["equipment:" + weapon.Id + ":overkill"] > 0);
        }
        Assert.True(damages[1] > damages[0]);
    }

}
