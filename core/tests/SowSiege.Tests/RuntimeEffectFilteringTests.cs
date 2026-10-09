using System.Diagnostics;
using SowSiege.Core;
using Xunit;
using Xunit.Abstractions;

namespace SowSiege.Tests;

public sealed class RuntimeEffectFilteringTests(ITestOutputHelper output)
{
    private static RuntimeEffectDefinition Effect(string id, int amount, string trigger = "modifier", string operation = "stat-add", string subject = "attack-damage") => new(id, trigger, operation, subject, amount, 10000, 10, 0, []);
    private static ContentCatalog Catalog() => RuntimeTests.Catalog();

    [Fact]
    public void OrdinalSourceAndEffectOrderPreserveClampingAndCurrentStacks()
    {
        var catalog = Catalog();
        catalog = catalog with
        {
            Runtime = catalog.Runtime! with
            {
                Charters = new Dictionary<string, CharterDefinition>
                {
                    ["test:Z"] = new("test:Z", "weapon", [Effect("test:Z-add", 3)]),
                    ["test:A"] = new("test:A", "weapon", [Effect("test:z", 5), Effect("test:a", -20)])
                },
                Items = new Dictionary<string, ItemDefinition> { ["test:a"] = new("test:a", [], [Effect("test:item", 2)]) }
            }
        };
        var simulation = RuntimeTests.Create(catalog); var state = simulation.World.Runtime!;
        state.Charters.Add("test:Z", 3); state.Charters.Add("test:A", 2); state.Items.Add("test:a", 4);
        Assert.Equal(27, simulation.Runtime!.Modify("attack-damage", 10, new(simulation.World.Lord)));
        state.Charters["test:A"] = 1; state.Items["test:a"] = 2;
        Assert.Equal(18, simulation.Runtime.Modify("attack-damage", 10, new(simulation.World.Lord)));
        Assert.Equal(10, simulation.Runtime.Modify("no-matching-subject", 10, new(simulation.World.Lord)));
        Assert.All(simulation.Result().Runtime!.Effects, effect => Assert.Equal(2, effect.ActivationCount));
    }

    [Fact]
    public void ItemTagEligibilityIsReevaluatedBetweenCalls()
    {
        var catalog = FirstPlayableTests.Catalog();
        var tagged = catalog.Weapons.Values.First(w => w.Tags.Contains("melee"));
        catalog = catalog with
        {
            Runtime = catalog.Runtime! with
            {
                Equipment = new Dictionary<string, EquipmentRuntimeDefinition>(),
                Items = new Dictionary<string, ItemDefinition> { ["test:item"] = new("test:item", ["melee"], [Effect("test:bonus", 3)]) }
            }
        };
        var session = FirstPlayableTests.Session(catalog); var world = session.Simulation.World; var runtime = session.Simulation.Runtime!;
        world.Equipment.Clear(); world.Runtime!.Items.Add("test:item", 2);
        Assert.Equal(10, runtime.Modify("attack-damage", 10, new(world.Lord)));
        world.Equipment.Add(new() { Id = tagged.Id, Level = 1 });
        Assert.Equal(16, runtime.Modify("attack-damage", 10, new(world.Lord)));
        world.Equipment.Clear();
        Assert.Equal(10, runtime.Modify("attack-damage", 10, new(world.Lord)));
        Assert.Equal(1, Assert.Single(runtime.Result().Effects, effect => effect.EffectId == "test:bonus").ActivationCount);
    }

    [Fact]
    public void NestedDifferentTriggerKeepsOuterSnapshotAndSameTriggerGuard()
    {
        var catalog = Catalog();
        catalog = catalog with
        {
            Runtime = catalog.Runtime! with
            {
                Items = new Dictionary<string, ItemDefinition>
                {
                    ["test:item"] = new("test:item", [], [Effect("test:b", 1, "harvest", "repair-nearest", "building"), Effect("test:a", 1, "harvest", "repair-nearest", "building"), Effect("test:nested", 2, "repair", "repair-nearest", "building")])
                }
            }
        };
        var simulation = RuntimeTests.Create(catalog); var world = simulation.World;
        world.Runtime!.Items.Add("test:item", 1);
        var building = world.Buildings[0]; building.Built = true; building.Health = 10;
        simulation.Runtime!.Emit("harvest", new(building.Position));
        Assert.Equal(16, building.Health);
        var effects = simulation.Result().Runtime!.Effects.ToDictionary(e => e.EffectId);
        Assert.Equal(1, effects["test:a"].ActivationCount); Assert.Equal(1, effects["test:b"].ActivationCount);
        Assert.Equal(2, effects["test:nested"].ActivationCount);
        simulation.Runtime.Emit("harvest", new(building.Position));
        Assert.Equal(22, building.Health);
    }

    [Fact]
    public void UnrelatedEffectsDoNotScaleAllocationForMissingModifierAndTrigger()
    {
        var empty = Workload(0); var unrelated = Workload(128);
        output.WriteLine($".NET only; 2000 Modify+Emit pairs: empty={empty.Bytes} bytes/{empty.Milliseconds:F3}ms; unrelated128={unrelated.Bytes} bytes/{unrelated.Milliseconds:F3}ms");
        Assert.True(unrelated.Bytes < empty.Bytes * 2, $"Unrelated effects caused proportional allocation: {empty.Bytes} -> {unrelated.Bytes}");
    }

    private static (long Bytes, double Milliseconds) Workload(int count)
    {
        var catalog = Catalog();
        catalog = catalog with
        {
            Runtime = catalog.Runtime! with
            {
                Items = new Dictionary<string, ItemDefinition>
                {
                    ["test:item"] = new("test:item", [], Enumerable.Range(0, count).Select(i => Effect($"test:unused-{i}", 1)).ToArray())
                }
            }
        };
        var simulation = RuntimeTests.Create(catalog); simulation.World.Runtime!.Items.Add("test:item", 1);
        var runtime = simulation.Runtime!; var context = new EffectContext(simulation.World.Lord);
        void Pair() { runtime.Modify("missing", 10, context); runtime.Emit("absent", context); }
        for (var i = 0; i < 200; i++) { Pair(); }
        var timer = Stopwatch.StartNew(); var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++) { Pair(); }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before; timer.Stop();
        return (bytes, timer.Elapsed.TotalMilliseconds);
    }
}
