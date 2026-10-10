using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WavePrimitiveTests
{
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
