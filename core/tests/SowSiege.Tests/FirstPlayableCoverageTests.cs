using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;
public sealed class FirstPlayableCoverageTests
{
    private sealed record Observation(string Id, string Behavior, long Count, int Tick, string StateHash);
    private readonly List<Observation> observations = new();
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "tools/check.sh")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("Repository root unavailable.");
    }
    private void Observe(InteractiveSession s, string id, string behavior, long count = 1) => observations.Add(new(id, behavior, count, s.Simulation.World.Tick, s.ComputeStateHash()));
    private static InteractiveSession Fixture(ContentCatalog c)
    {
        var s = FirstPlayableTests.Session(c); var w = s.Simulation.World;
        w.Tick = 100; w.People.Clear(); w.Buildings.Clear(); w.Farms.Clear(); w.Enemies.Clear(); w.Equipment.Clear();
        foreach (var id in c.Weapons.Keys.Concat(c.Tools.Keys))
        {
            w.Equipment.Add(new() { Id = id, Level = 12 });
        }

        w.Food = 200; w.Destination = new(w.Lord.X + 1000, w.Lord.Y);
        w.Farms.Add(new() { Id = w.AllocateId(), Position = new(w.Lord.X + 150, w.Lord.Y), Source = c.Heroes[c.Tuning.DefaultHero].StartingTool, Stage = 3, Progress = 5 });
        w.Farms.Add(new() { Id = w.AllocateId(), Position = new(w.Lord.X - 150, w.Lord.Y), Source = c.Heroes[c.Tuning.DefaultHero].StartingTool, Stage = 0, Progress = 5 });
        w.Buildings.Add(new() { Id = w.AllocateId(), Position = new(w.Lord.X + 250, w.Lord.Y), Built = true, Health = 50, Source = c.Tools.Values.First(t => t.Growth.Target == "building").Id });
        foreach (var role in new[] { "peasant", "militia", "returning" })
        {
            w.People.Add(new() { Id = w.AllocateId(), Position = w.Lord, Role = role, Health = 200, Members = 1, DutyUntil = 500 });
        }

        w.Enemies.Add(new() { Id = w.AllocateId(), Position = new(w.Lord.X + 100, w.Lord.Y), Definition = c.Enemies.Keys.First(), Health = 10000 });
        return s;
    }
    private static string Physical(InteractiveSession s)
    {
        var w = s.Simulation.World;
        return JsonSerializer.Serialize(new { w.Food, w.Experience, w.WeaponDamage, Farms = w.Farms.Select(f => new { f.Id, f.Stage, f.Progress }), Buildings = w.Buildings.Select(b => new { b.Health }), People = w.People.Select(p => new { p.DutyUntil, p.Health }), Enemies = w.Enemies.Select(e => e.Health), RuntimeEntities = w.Runtime!.Entities.Select(p => new { p.Key, p.Value.Shield, p.Value.HoldUntil, p.Value.Waypoint, p.Value.RestSince, p.Value.ArrivalGuardUsed }) });
    }
    private static EffectContext Context(InteractiveSession s, RuntimeEffectDefinition effect)
    {
        var c = s.Catalog; var w = s.Simulation.World;
        var id = effect.Conditions.FirstOrDefault(x => x.Kind == "equipment-id")?.Value;
        var tag = effect.Conditions.FirstOrDefault(x => x.Kind == "equipment-tag")?.Value;
        id ??= tag is null ? c.Weapons.Keys.First() : c.Weapons.Values.Where(v => v.Tags.Contains(tag)).Select(v => v.Id).Concat(c.Tools.Values.Where(t => t.Tags.Contains(tag)).Select(t => t.Id)).First();
        var target = effect.Conditions.FirstOrDefault(x => x.Kind == "enemy-target")?.Value;
        if (target is not null)
        {
            w.Enemies[0].Definition = c.Enemies.Values.First(e => e.Target == target).Id;
        }

        return new(w.Lord, id, w.Enemies[0], w.Farms[0], w.Buildings[0], w.People[1], true, true, true);
    }
    private bool ExecuteEffect(ContentCatalog c, string kind, string id, RuntimeEffectDefinition effect, bool enabled, out InteractiveSession session)
    {
        var emptyEquipment = new Dictionary<string, EquipmentRuntimeDefinition>();
        var runtime = c.Runtime! with { Equipment = emptyEquipment, Charters = kind == "charter" ? new Dictionary<string, CharterDefinition> { [id] = c.Runtime.Charters[id] with { Effects = new[] { effect } } } : new Dictionary<string, CharterDefinition>(), Items = kind == "item" ? new Dictionary<string, ItemDefinition> { [id] = c.Runtime.Items[id] with { Effects = new[] { effect } } } : new Dictionary<string, ItemDefinition>(), Evolutions = kind == "evolution" ? new Dictionary<string, EvolutionDefinition> { [id] = c.Runtime.Evolutions[id] with { Effects = new[] { effect } } } : new Dictionary<string, EvolutionDefinition>() };
        session = Fixture(c with { Runtime = runtime }); var w = session.Simulation.World;
        if (enabled)
        {
            if (kind == "charter")
            {
                w.Runtime!.Charters[id] = 1;
            }

            if (kind == "item")
            {
                w.Runtime!.Items[id] = 1;
            }

            if (kind == "evolution")
            {
                w.Runtime!.Evolutions.Add(id);
            }
        }
        var context = Context(session, effect); var before = Physical(session);
        if (effect.Operation == "stat-add")
        {
            return session.Simulation.Runtime!.Modify(effect.Subject, 100, context) != 100;
        }

        if (effect.Operation == "planting-bias")
        {
            var origin = new Position(w.Lord.X + 500, w.Lord.Y); return session.Simulation.Runtime!.PlantingPosition(context.Equipment, origin) != origin;
        }
        session.Simulation.Runtime!.Emit(effect.Trigger, context); return before != Physical(session);
    }
    [Fact]
    public void AllSelectedRuntimeEffectsHaveObservedContributionAndDisabledCounterfactual()
    {
        var root = Root(); var data = Path.Combine(root, "data"); var c = ContentLoader.Load(data, profileName: "first-playable");
        foreach (var item in c.Runtime!.Items.Values)
        {
            var effect = item.Effects[0]; Assert.True(ExecuteEffect(c, "item", item.Id, effect, true, out var positive), item.Id);
            Assert.False(ExecuteEffect(c, "item", item.Id, effect, false, out var negative), item.Id);
            Observe(positive, item.Id, "condition-match");
            // Eligibility must also stop an owned item's effect when its required equipment tags are absent.
            var w = positive.Simulation.World; w.Equipment.Clear(); var context = new EffectContext(w.Lord, Farm: w.Farms[0], Person: w.People[1], Building: w.Buildings[0], Enemy: w.Enemies[0], WasRuined: true);
            var count = w.Runtime!.Effects.GetValueOrDefault(effect.Id)?.Count ?? 0;
            if (effect.Operation == "stat-add")
            {
                positive.Simulation.Runtime!.Modify(effect.Subject, 100, context);
            }
            else if (effect.Operation == "planting-bias")
            {
                positive.Simulation.Runtime!.PlantingPosition("", new(w.Lord.X + 500, w.Lord.Y));
            }
            else
            {
                positive.Simulation.Runtime!.Emit(effect.Trigger, context);
            }

            Assert.Equal(count, w.Runtime.Effects.GetValueOrDefault(effect.Id)?.Count ?? 0); Observe(positive, item.Id, "condition-miss");
        }
        foreach (var charter in c.Runtime.Charters.Values)
        {
            foreach (var axis in new[] { "weapon", "estate" })
            {
                var effect = charter.Effects.First(e => (e.Subject.StartsWith("attack-", StringComparison.Ordinal) || e.Operation == "damage-pulse") == (axis == "weapon"));
                Assert.True(ExecuteEffect(c, "charter", charter.Id, effect, true, out var positive), charter.Id + "/" + axis);
                Assert.False(ExecuteEffect(c, "charter", charter.Id, effect, false, out _)); Observe(positive, charter.Id, axis);
            }
        }
        foreach (var evolution in c.Runtime.Evolutions.Values)
        {
            var s = Fixture(c); var w = s.Simulation.World;
            while (w.Farms.Count < 12)
            {
                w.Farms.Add(new() { Id = w.AllocateId(), Stage = 3, Source = c.Heroes[c.Tuning.DefaultHero].StartingTool });
            }

            w.Farms[1].Stage = 3; s.Simulation.Runtime!.UnlockEvolutions(); Assert.Contains(evolution.Id, w.Runtime!.Evolutions); Observe(s, evolution.Id, "unlock");
            Assert.True(ExecuteEffect(c, "evolution", evolution.Id, evolution.Effects[0], true, out var positive), evolution.Id);
            Assert.False(ExecuteEffect(c, "evolution", evolution.Id, evolution.Effects[0], false, out _)); Observe(positive, evolution.Id, "contribution");
        }
        CoverWeapons(c); CoverTools(c); CoverEnemies(c); CoverEvents(c);
        var output = Environment.GetEnvironmentVariable("BS_FIRST_PLAYABLE_COVERAGE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, JsonSerializer.Serialize(new { contractVersion = 1, profileId = "core:first_playable", sourceHash = ContentProvenance.SourceHash(root), dataHash = ContentLoader.Hash(data, false), profileHash = ContentLoader.ProfileHash(data, "first-playable"), observations }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
        }
    }
    private void CoverWeapons(ContentCatalog c)
    {
        foreach (var id in c.Weapons.Keys)
        {
            var s = Fixture(c); var w = s.Simulation.World; w.Equipment.RemoveAll(e => e.Id != id); w.Enemies.Clear(); w.Runtime!.Evolutions.Clear();
            var definition = c.FirstPlayable!.Weapons[id]; var level = c.WeaponCombat!.Weapons[id].Levels[11];
            for (var offset = 200; offset <= Math.Max(level.Range, 4000); offset += 200)
            {
                foreach (var axis in new[] { new Position(offset, 0), new Position(0, offset), new Position(-offset, 0), new Position(0, -offset) })
                {
                    w.Enemies.Add(new() { Id = w.AllocateId(), Definition = c.Enemies.Keys.First(), Position = new(w.Lord.X + axis.X, w.Lord.Y + axis.Y), Health = 100000 });
                }
            }

            var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies); var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
            combat.Activate(w.Equipment.Single(), c.Weapons[id].Activation, null); Observe(s, id, "activation");
            var firstPositions = new Dictionary<int, Position>(); Dictionary<int, int>? halfway = null; var moved = false;
            for (var t = 0; t < definition.LifetimeTicks; t++)
            {
                combat.TickFirstPlayableAttacks();
                foreach (var a in w.FirstPlayable!.Attacks) { if (firstPositions.TryGetValue(a.Id, out var p) && a.Position != p) { moved = true; } firstPositions.TryAdd(a.Id, a.Position); }
                if (t == definition.LifetimeTicks / 2)
                {
                    halfway = w.Enemies.ToDictionary(e => e.Id, e => e.Health);
                }

                w.Tick++;
            }
            Assert.True(w.WeaponDamage > 0, id); Observe(s, id, "hit", w.FirstPlayable!.Coverage["equipment:" + id + ":hit"]);
            if (definition.Form == "chain") { Assert.InRange(w.Enemies.Count(e => e.Health < 100000), 2, level.Count + level.Pierce); Observe(s, id, "unique-jumps"); }
            if (definition.Form == "piercing") { Assert.InRange(w.Enemies.Count(e => e.Health < 100000), level.Pierce + 1, level.Count * (level.Pierce + 1)); Assert.Equal(level.Count * (level.Pierce + 1), w.FirstPlayable.Coverage["equipment:" + id + ":hit"]); Observe(s, id, "pierce-budget"); }
            if (definition.Form == "boomerang") { Assert.NotNull(halfway); Assert.Contains(w.Enemies, e => halfway[e.Id] < 100000 && e.Health < halfway[e.Id]); Observe(s, id, "return-hit"); }
            if (definition.Form == "orbit") { Assert.True(moved); Observe(s, id, "orbital-motion"); }
            if (definition.Form == "field") { Assert.False(moved); Assert.Empty(w.FirstPlayable.Attacks); Observe(s, id, "lifetime"); }
        }
    }
    private void CoverTools(ContentCatalog c)
    {
        foreach (var tool in c.Tools.Values)
        {
            var s = Fixture(c); var w = s.Simulation.World; w.Equipment.RemoveAll(e => e.Id != tool.Id);
            if (tool.Activation.Shape == "orbit")
            {
                w.Enemies[0].Position = new(w.Lord.X + tool.Activation.Range * 3 / 4, w.Lord.Y);
            }

            var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies); var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
            combat.Activate(w.Equipment.Single(), tool.Activation, w.Tools[tool.Id]); Assert.True(w.Tools[tool.Id].Activations > 0); Assert.True(w.Tools[tool.Id].ActivationDamage > 0, tool.Id); Observe(s, tool.Id, "activation");
            var estate = new EstateSystem(c, s.Options.Run, w, "C", spatial, s.Simulation.Runtime, interactive: s.State);
            estate.ApplyGrowth(tool); Assert.True(w.Tools[tool.Id].GrowthProduced > 0, tool.Id); Observe(s, tool.Id, "growth", w.Tools[tool.Id].GrowthProduced);
        }
    }
    private void CoverEnemies(ContentCatalog c)
    {
        var s = Fixture(c); var w = s.Simulation.World; w.Equipment.Clear(); w.Enemies.Clear();
        var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, new SpatialHash(600), s.Simulation.Runtime, interactive: s.State);
        var seen = new HashSet<string>();
        for (var tick = 0; tick <= 25200; tick += c.Tuning.World.Threat.SpawnPeriodTicks)
        {
            w.Tick = tick; combat.SpawnAndMoveEnemies();
            foreach (var enemy in w.Enemies)
            {
                if (seen.Add(enemy.Definition))
                {
                    Observe(s, enemy.Definition, "spawn");
                }
            }

            foreach (var enemy in w.Enemies.Where(e => c.FirstPlayable!.Enemies[e.Definition].Rank == "boss"))
            {
                Assert.Contains(s.State.Events, e => e.Kind == PresentationKind.BossWarning && e.SourceId == enemy.Definition); Observe(s, enemy.Definition, "warning"); enemy.Health = 0;
                combat.ResolveDeaths(); Assert.True(s.GetSummary().BossDefeated); Observe(s, enemy.Definition, "defeat"); break;
            }
            w.Enemies.Clear();
        }
        Assert.Equal(c.Enemies.Count, seen.Count);
    }
    private void CoverEvents(ContentCatalog c)
    {
        foreach (var entry in c.FirstPlayable!.MapEvents)
        {
            var s = Fixture(c); var w = s.Simulation.World; w.Tick = entry.FirstSpawnTick; w.Food = 1000;
            s.Simulation.Runtime!.Tick(); var entity = w.FirstPlayable!.MapEvents.Single(e => e.Definition == entry.Id); Observe(s, entry.Id, "spawn");
            w.Lord = entity.Position; w.Tick++;
            if (entry.Kind == "cart")
            {
                var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies); var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
                var weapon = w.Equipment.First(e => c.FirstPlayable.Weapons.TryGetValue(e.Id, out var d) && d.Form == "sector90");
                var before = w.Runtime!.Loot.Count;
                for (var i = 0; i < 100 && entity.Health > 0; i++) { combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks(); w.Tick++; }
                Assert.Equal(0, entity.Health); Observe(s, entry.Id, "broken"); s.Simulation.Runtime.Tick(); Assert.True(w.Runtime.Loot.Count >= before + entry.RewardCount); Observe(s, entry.Id, "drop");
            }
            else
            {
                s.Simulation.Runtime.Tick();
            }

            Assert.DoesNotContain(w.FirstPlayable.MapEvents, e => e.Id == entity.Id); Observe(s, entry.Id, "claim");
        }
    }

    [Fact]
    public void ActualHarvestContextPlantsSeedCrownAndExtendsOnlyNearbyDraftedPeople()
    {
        var c = ContentLoader.Load(Path.Combine(Root(), "data"), profileName: "first-playable");
        foreach (var enabled in new[] { false, true })
        {
            var s = Fixture(c); var w = s.Simulation.World;
            var farm = w.Farms[0]; var count = w.Farms.Count;
            var militia = w.People.Single(p => p.Role == "militia"); var worker = w.People.Single(p => p.Role == "peasant");
            var remote = new PersonState { Id = w.AllocateId(), Role = "militia", Position = new(w.Lord.X + 3000, w.Lord.Y), Health = 100, DutyUntil = 500 }; w.People.Add(remote);
            if (enabled) { w.Runtime!.Evolutions.Add("core:seed_crown"); w.Runtime.Evolutions.Add("core:rally_kitchen"); }
            var estate = new EstateSystem(c, s.Options.Run, w, "C", new SpatialHash(600), s.Simulation.Runtime);
            estate.Harvest(farm);
            Assert.Equal(count + (enabled ? 1 : 0), w.Farms.Count);
            Assert.Equal(enabled ? 860 : 500, militia.DutyUntil); Assert.Equal(500, worker.DutyUntil); Assert.Equal(500, remote.DutyUntil);
        }
    }
    [Fact]
    public void EveryCharterBothAxesChangeActualCombatEstateOrPersonState()
    {
        var c = ContentLoader.Load(Path.Combine(Root(), "data"), profileName: "first-playable");
        foreach (var charter in c.Runtime!.Charters.Values)
        {
            foreach (var axis in new[] { "weapon", "estate" })
            {
                var effect = charter.Effects.First(e => (e.Subject.StartsWith("attack-", StringComparison.Ordinal) || e.Operation == "damage-pulse") == (axis == "weapon"));
                var outcomes = new List<string>();
                foreach (var enabled in new[] { false, true })
                {
                    var runtime = c.Runtime with { Equipment = new Dictionary<string, EquipmentRuntimeDefinition>(), Charters = new Dictionary<string, CharterDefinition> { [charter.Id] = charter with { Effects = new[] { effect } } }, Items = new Dictionary<string, ItemDefinition>(), Evolutions = new Dictionary<string, EvolutionDefinition>() };
                    var s = Fixture(c with { Runtime = runtime }); var w = s.Simulation.World;
                    if (enabled)
                    {
                        w.Runtime!.Charters[charter.Id] = 1;
                    }

                    var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies);
                    var combat = new CombatSystem(s.Catalog, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime);
                    var estate = new EstateSystem(s.Catalog, s.Options.Run, w, "C", spatial, s.Simulation.Runtime);
                    var weapon = w.Equipment.First(e => s.Catalog.FirstPlayable!.Weapons.TryGetValue(e.Id, out var d) && d.Form == "sector90");
                    if (axis == "weapon") { combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks(); }
                    else if (effect.Subject == "repair-amount")
                    {
                        estate.ApplyGrowth(c.Tools.Values.First(t => t.Growth.Target == "building"));
                    }
                    else if (effect.Subject is "draft-speed" or "worker-speed")
                    {
                        var person = w.People.Single(p => p.Role == (effect.Subject == "worker-speed" ? "peasant" : "militia")); person.Position = new(w.Lord.X - 500, w.Lord.Y); person.Destination = w.Lord; estate.Tick();
                    }
                    else if (effect.Subject is "building-front-damage" or "building-rear-damage")
                    {
                        w.People.Clear(); var building = w.Buildings[0]; var enemy = w.Enemies[0]; enemy.Definition = c.Enemies.Values.First(e => e.Target == "building").Id;
                        enemy.Position = new(building.Position.X + (effect.Subject == "building-front-damage" ? 1 : -1), building.Position.Y); s.Simulation.Runtime!.Entity(building.Id).Facing = new(1, 0); combat.ResolveEnemyAttacks();
                    }
                    else if (effect.Subject == "worker-incoming-damage") { w.Enemies[0].Position = w.People[0].Position; combat.ResolveEnemyAttacks(); }
                    else
                    {
                        estate.Harvest(w.Farms[0]);
                    }

                    outcomes.Add(Physical(s) + JsonSerializer.Serialize(new { Ready = weapon.ReadyTick, Positions = w.People.Select(p => new { p.Position.X, p.Position.Y }), EnemyPositions = w.Enemies.Select(e => new { e.Position.X, e.Position.Y }) }));
                }
                Assert.NotEqual(outcomes[0], outcomes[1]);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StagedBuildingPreservesRuinConditionsUntilCompletion(bool ruined, bool equipped)
    {
        var c = ContentLoader.Load(Path.Combine(Root(), "data"), profileName: "first-playable");
        var s = Fixture(c); var w = s.Simulation.World; var building = w.Buildings.Single();
        building.Built = ruined; building.Health = 0; w.Lord = building.Position;
        w.Equipment.Clear(); w.Equipment.Add(new() { Id = "core:carpenter_hammer", Level = 1 });
        if (equipped)
        {
            w.Runtime!.Items["core:ruin_keystone"] = 1;
        }

        var estate = new EstateSystem(c, s.Options.Run, w, "C", new SpatialHash(600), s.Simulation.Runtime, interactive: s.State);
        var tool = c.Tools["core:carpenter_hammer"]; var states = new List<string>(); var counts = new List<int>();
        for (var n = 0; n < 4; n++)
        {
            estate.ApplyGrowth(tool); states.Add(s.View.CaptureFirstPlayable()!.BuildingProgress.Single().State); counts.Add(w.Rebuilds);
            if (n < 3)
            {
                Assert.Equal(0, building.Health);
            }
        }
        Assert.Equal(c.Tuning.World.Buildings.RepairAmount + (ruined && equipped ? 8 : 0), building.Health);
        Assert.Equal(new[] { "constructing", "constructing", "constructing", "damaged" }, states);
        Assert.Equal(new[] { 0, 0, 0, ruined ? 1 : 0 }, counts);
        Assert.True(building.Built); Assert.Empty(w.FirstPlayable!.BuildingWork);
        Assert.Single(s.State.Events, e => e.Kind == PresentationKind.BuildingStarted);
        Assert.Single(s.State.Events, e => e.Kind == PresentationKind.BuildingCompleted);
        var before = building.Health; estate.ApplyGrowth(tool);
        Assert.Equal(before + c.Tuning.World.Buildings.RepairAmount, building.Health);
        Assert.Equal(ruined ? 1 : 0, w.Rebuilds);
        Assert.Single(s.State.Events, e => e.Kind == PresentationKind.BuildingCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuiltRuinsDoNotSatisfyFreshBuildingCondition(bool ruined)
    {
        var c = ContentLoader.Load(Path.Combine(Root(), "data"), profileName: "first-playable");
        var item = c.Runtime!.Items["core:ruin_keystone"];
        var items = c.Runtime.Items.ToDictionary(p => p.Key, p => p.Value);
        items[item.Id] = item with { Effects = item.Effects.Select(e => e with { Conditions = new[] { new RuntimeCondition("building-new") } }).ToArray() };
        c = c with { Runtime = c.Runtime with { Items = items } };
        var s = Fixture(c); var w = s.Simulation.World; var building = w.Buildings.Single(); building.Built = ruined; building.Health = 0; w.Lord = building.Position;
        w.Equipment.Clear(); w.Equipment.Add(new() { Id = "core:carpenter_hammer", Level = 1 }); w.Runtime!.Items[item.Id] = 1;
        var estate = new EstateSystem(c, s.Options.Run, w, "C", new SpatialHash(600), s.Simulation.Runtime);
        for (var n = 0; n < 4; n++)
        {
            estate.ApplyGrowth(c.Tools["core:carpenter_hammer"]);
        }

        Assert.Equal(c.Tuning.World.Buildings.RepairAmount + (ruined ? 0 : 8), building.Health);
        var before = building.Health; estate.ApplyGrowth(c.Tools["core:carpenter_hammer"]);
        Assert.Equal(before + c.Tuning.World.Buildings.RepairAmount, building.Health);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void ActualOrbitEvolutionRepairsOnlyLivingFirstPlayableBuildings(int stage, bool evolved)
    {
        var c = ContentLoader.Load(Path.Combine(Root(), "data"), profileName: "first-playable");
        var s = Fixture(c); var w = s.Simulation.World; var building = w.Buildings.Single(); building.Health = stage == 2 ? 50 : 0; w.Lord = building.Position;
        var estate = new EstateSystem(c, s.Options.Run, w, "C", new SpatialHash(600), s.Simulation.Runtime, interactive: s.State);
        if (stage == 1)
        {
            estate.ApplyGrowth(c.Tools["core:carpenter_hammer"]);
        }

        if (evolved)
        {
            w.Runtime!.Evolutions.Add("core:warded_masonry");
        }

        var weapon = w.Equipment.Single(e => e.Id == "core:ward_orbit");
        w.Enemies[0].Position = new(w.Lord.X + c.WeaponCombat!.Weapons[weapon.Id].Levels[weapon.Level - 1].Range, w.Lord.Y);
        var spatial = new SpatialHash(600); spatial.Rebuild(w.Enemies);
        var combat = new CombatSystem(c, s.Options.Run, w, s.Simulation.RandomState, spatial, s.Simulation.Runtime, interactive: s.State);
        combat.Activate(weapon, c.Weapons[weapon.Id].Activation, null); combat.TickFirstPlayableAttacks();
        Assert.True(w.WeaponDamage > 0);
        Assert.Equal(stage == 2 ? 50 + (evolved ? 8 : 0) : 0, building.Health);
        Assert.Equal(0, w.Rebuilds);
        Assert.Equal(stage == 0 ? "ruin" : stage == 1 ? "constructing" : "damaged", s.View.CaptureFirstPlayable()!.BuildingProgress.Single().State);
        Assert.DoesNotContain(s.State.Events, e => e.Kind == PresentationKind.BuildingCompleted);
    }
    [Fact]
    public void LegacyPassiveRepairStillRevivesRuins()
    {
        var c = ContentLoader.Load(Path.Combine(Root(), "data"), profileName: "first-playable") with { FirstPlayable = null };
        var s = Fixture(c); var w = s.Simulation.World; var building = w.Buildings.Single(); building.Health = 0;
        w.Runtime!.Evolutions.Add("core:warded_masonry");
        s.Simulation.Runtime!.Emit("attack", new(building.Position, "core:ward_orbit"));
        Assert.Equal(8, building.Health); Assert.Equal(1, w.Rebuilds);
    }

}
