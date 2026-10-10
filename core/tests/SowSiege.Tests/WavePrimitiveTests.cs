using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WavePrimitiveTests
{
    [Theory]
    [InlineData("core:carpenter_hammer", "core:seed_bag", "unit:growth-cycle")]
    [InlineData("core:sheltered_sowing", "core:seed_bag", "unit:growth-cycle")]
    [InlineData("core:raider", "core:ram_runner", "unit:enemy-tell")]
    public void ValidTupleWithUnsupportedSubstrateCombinationIsRejected(string id, string donor, string unit)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        var programs = catalog.WaveRuntime!.Programs!;
        var units = programs[id].Params.ToDictionary(p => p.Key, p => p.Value);
        units[unit] = programs[donor].Params[unit];
        Assert.Throws<ArgumentException>(() => WavePrimitiveSupport.Validate(new(units)));
    }

    [Theory]
    [InlineData("core:rain_ladle", "unit:stock-cycle")]
    [InlineData("core:rain_ladle", "unit:growth-cycle")]
    [InlineData("core:carpenter_hammer", "unit:stock-cycle")]
    [InlineData("core:muster_horn", "unit:ally-task")]
    [InlineData("core:muster_horn", "unit:mission-cycle")]
    [InlineData("core:muster_horn", "unit:growth-cycle")]
    [InlineData("core:sheltered_sowing", "unit:growth-protect")]
    [InlineData("core:sheltered_sowing", "unit:paired-growth")]
    [InlineData("core:raider", "unit:enemy-tell")]
    public void IncompleteStateSubstrateIsRejectedBeforeExecution(string id, string missing)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        var units = catalog.WaveRuntime!.Programs![id].Params.ToDictionary(p => p.Key, p => p.Value);
        Assert.True(units.Remove(missing));
        Assert.Throws<ArgumentException>(() => WavePrimitiveSupport.Validate(new(units)));
    }

    [Theory]
    [InlineData("core:carpenter_hammer", "unit:completed-structure-attack", "building")]
    [InlineData("core:rain_ladle", "unit:status-apply", "water")]
    public void RemovingWorkEffectUnitRemovesItsObservableEffect(string id, string unit, string kind)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        var programs = catalog.WaveRuntime!.Programs!.ToDictionary(p => p.Key, p => p.Value);
        var units = programs[id].Params.ToDictionary(p => p.Key, p => p.Value);
        Assert.True(units.Remove(unit)); programs[id] = new(units);
        catalog = catalog with { WaveRuntime = catalog.WaveRuntime with { Programs = programs } };
        var world = new WorldState { Lord = new(5000, 5000), WaveRuntime = new() };
        world.WaveRuntime.Work.Add(new() { Id = 1, Source = id, Kind = kind, Health = 100, Complete = true, Position = world.Lord, WetUntil = 100 });
        var enemy = new EnemyState { Id = 900, Health = 100, Position = new(5010, 5000) }; world.Enemies.Add(enemy);
        new WaveWorkSystem(catalog, world, null).Tick();
        Assert.Equal(100, enemy.Health);
        Assert.False(world.WaveRuntime.EnemyActions.TryGetValue(enemy.Id, out var action) && action.WetUntil > 0);
    }

    [Fact]
    public void EvolutionAttackShapeControlsActiveAttackInsteadOfHistoricalWeaponShape()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        var evolution = catalog.WaveRuntime!.Evolutions.Values.First(e => e.Kind == WaveEvolutionKind.PlantingArc);
        var programs = catalog.WaveRuntime.Programs!.ToDictionary(p => p.Key, p => p.Value);
        var units = programs[evolution.Id].Params.ToDictionary(p => p.Key, p => p.Value);
        units["unit:attack-shape"] = programs["core:ember_wand"].Params["unit:attack-shape"];
        programs[evolution.Id] = new(units);
        catalog = catalog with { WaveRuntime = catalog.WaveRuntime with { Programs = programs } };
        var world = new WorldState { Lord = new(5000, 5000), LordHealth = 1000 };
        world.Equipment.Add(new() { Id = evolution.InputIds[0] });
        world.Enemies.Add(new() { Id = 900, Definition = catalog.Enemies.Keys.First(), Health = 1000, Position = new(5300, 5000) });
        var system = new WaveRuntimeSystem(catalog, world, new(30000, true), null);
        world.WaveRuntime!.Evolutions.Add(evolution.Id);
        system.Tick(world.Lord);
        Assert.Contains(world.WaveRuntime.Projectiles, p => p.Source == evolution.Id);
    }

    [Fact]
    public void ComposedAttackShapeOverridesHistoricalSerializationRecipe()
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Homing);
        var old = catalog.WaveRuntime!;
        var programs = WaveLegacyCompiler.Compile(old).ToDictionary(p => p.Key, p => p.Value);
        var id = world.Equipment[0].Id;
        var units = programs[id].Params.ToDictionary(p => p.Key, p => p.Value);
        var shape = units["unit:attack-shape"].ToDictionary(p => p.Key, p => p.Value);
        shape["shape"] = "melee-fan";
        shape["aim"] = "facing";
        units["unit:attack-shape"] = shape;
        programs[id] = new(units);
        catalog = catalog with { WaveRuntime = old with { Programs = programs } };
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        system.Tick(world.Lord);
        Assert.Equal(90, enemy.Health);
        Assert.Empty(world.WaveRuntime!.Projectiles);
        Assert.False(WavePrimitiveModules.Equivalent(catalog.WaveRuntime));
    }

    [Fact]
    public void OriginalNormalInputGoldenHashesRemainUnchanged()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = ContentLoader.Load(data, false, "wave-1a");
        using var goldens = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/wave-primitive-baseline/hashes.json")));
        foreach (var golden in goldens.RootElement.EnumerateArray())
        {
            using var output = new MemoryStream();
            var result = InteractiveCli.RecordWaveFixture(catalog, "942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC", golden.GetProperty("Seed").GetInt32(), output, 3000);
            Assert.Equal(golden.GetProperty("StateHash").GetString(), result.StateHash);
        }
    }
    [Fact]
    public void CompiledLegacyProgramsRetainHashButEveryChangedExecutableParameterIsVisible()
    {
        var (catalog, _, _, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var definition = catalog.WaveRuntime!;
        var programs = WaveLegacyCompiler.Compile(definition).ToDictionary(p => p.Key, p => p.Value);
        var compiled = catalog with { WaveRuntime = definition with { Programs = programs } };
        var expected = PortableStateCodec.HashCatalog(catalog);
        Assert.Equal(expected, PortableStateCodec.HashCatalog(compiled));
        var id = definition.Gear.Keys.Single();
        var units = programs[id].Params.ToDictionary(p => p.Key, p => p.Value);
        var shape = units["unit:attack-shape"].ToDictionary(p => p.Key, p => p.Value);
        foreach (var parameter in shape.Keys)
        {
            var changed = new Dictionary<string, string>(shape) { [parameter] = "unsupported-change" };
            units["unit:attack-shape"] = changed;
            programs[id] = new(units);
            Assert.NotEqual(expected, PortableStateCodec.HashCatalog(compiled));
            Assert.Throws<ArgumentException>(() => WavePrimitiveSupport.Validate(programs[id]));
        }
    }

    [Fact]
    public void ExplicitHistoryGateControlsEligibilityRatherThanEvolutionShape()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var catalog = ContentLoader.Load(data, false, "wave-1a");
        var definition = catalog.WaveRuntime!;
        var recipe = definition.Evolutions.Values.First(e => definition.Programs![e.Id].Has("unit:attack-anchor"));
        var programs = definition.Programs!.ToDictionary(p => p.Key, p => p.Value);
        var units = programs[recipe.Id].Params.ToDictionary(p => p.Key, p => p.Value);
        var gate = units["unit:event-gate"].ToDictionary(p => p.Key, p => p.Value);
        gate["condition"] = "harvest-near-building"; units["unit:event-gate"] = gate; programs[recipe.Id] = new(units);
        catalog = catalog with { WaveRuntime = definition with { Programs = programs } };
        var world = new WorldState(); foreach (var id in recipe.InputIds) { world.Equipment.Add(new() { Id = id }); }
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        world.WaveRuntime!.RepairCompleted = true;
        Assert.False(system.EligibleEvolution(recipe));
        world.WaveRuntime.HarvestNearBuilding = true;
        Assert.True(system.EligibleEvolution(recipe));
    }

    [Fact]
    public void UnregisteredPrimitiveOrParameterIsRejected()
    {
        Assert.Throws<ArgumentException>(() => WavePrimitiveSupport.Validate(new(new Dictionary<string, IReadOnlyDictionary<string, string>> { ["unit:missing"] = new Dictionary<string, string>() })));
        var (catalog, _, _, _) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var programs = WaveLegacyCompiler.Compile(catalog.WaveRuntime!).ToDictionary(p => p.Key, p => p.Value);
        programs.Remove(catalog.WaveRuntime!.ChapterId);
        Assert.Throws<ArgumentException>(() => WavePrimitiveSupport.Resolve(catalog.WaveRuntime with { Programs = programs }));
    }

}
