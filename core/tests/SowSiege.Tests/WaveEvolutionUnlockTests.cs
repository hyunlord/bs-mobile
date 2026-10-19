using System.Text.Json;
using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;

// ADR0058 implementation step 2: unlock levels and event counts come from data; defaults keep every historical hash.
public sealed class WaveEvolutionUnlockTests
{
    private const string HistoricalDataHash = "942CFEA33A25F90CBD5053D711C9BBFF1279DBE481950F2081C765B6093C3FDC";
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static ContentCatalog Catalog() => ContentLoader.Load(Data, false, "wave-1a");
    private static ContentCatalog With(ContentCatalog catalog, string id, WaveEvolutionUnlock unlock)
    {
        var definition = catalog.WaveRuntime!;
        var evolutions = definition.Evolutions.ToDictionary(p => p.Key, p => p.Value);
        evolutions[id] = evolutions[id] with { Unlock = unlock };
        return catalog with { WaveRuntime = definition with { Evolutions = evolutions } };
    }
    private static WaveEvolutionDefinition Sowing(ContentCatalog catalog) => catalog.WaveRuntime!.Evolutions.Values.Single(e => e.Kind == WaveEvolutionKind.PlantingArc);
    private static WaveEvolutionUnlock Unlock(int? bladeLevel = null, bool? bladeMax = null, int count = 1) =>
        new(new[] { new WaveUnlockInput("core:iron_blade", bladeLevel, bladeMax), new WaveUnlockInput("core:seed_bag") }, new[] { new WaveUnlockEvent("attack-kill-near-growth", count) });

    [Fact]
    public void ExplicitDefaultUnlocksKeepCatalogAndReplayHashes()
    {
        var catalog = Catalog();
        catalog = catalog with { WaveRuntime = catalog.WaveRuntime! with { EnemySeparation = null } };
        var programs = catalog.WaveRuntime!.Programs!;
        foreach (var evolution in catalog.WaveRuntime.Evolutions.Values.ToArray())
        {
            catalog = With(catalog, evolution.Id, new(evolution.InputIds.Select(id => new WaveUnlockInput(id, 1, false)).ToArray(),
                new[] { new WaveUnlockEvent(programs[evolution.Id].Value("unit:event-gate", "condition"), 1) }));
        }
        catalog = catalog with { WaveRuntime = catalog.WaveRuntime! with { CardOffer = new("none", 1, 1, new Dictionary<string, WaveCardOfferCard> { ["core:iron_blade"] = new(1) }) } };
        Assert.Equal("D108BA8D527BFE93A00C5FB78111AE2B8D4F53B4047CEA269AFE78D551CFC58E", PortableStateCodec.HashCatalog(catalog));
        using var goldens = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/wave-primitive-baseline/hashes.json")));
        foreach (var golden in goldens.RootElement.EnumerateArray())
        {
            using var output = new MemoryStream();
            var result = InteractiveCli.RecordWaveFixture(catalog, HistoricalDataHash, golden.GetProperty("Seed").GetInt32(), output, 3000);
            Assert.Equal(golden.GetProperty("StateHash").GetString(), result.StateHash);
        }
    }

    [Fact]
    public void NonDefaultUnlockOrOfferIsRecordedInTheCatalogHash()
    {
        var catalog = Catalog();
        var original = PortableStateCodec.HashCatalog(catalog);
        Assert.NotEqual(original, PortableStateCodec.HashCatalog(With(catalog, Sowing(catalog).Id, Unlock(bladeLevel: 3))));
        Assert.NotEqual(original, PortableStateCodec.HashCatalog(With(catalog, Sowing(catalog).Id, Unlock(count: 2))));
        Assert.NotEqual(original, PortableStateCodec.HashCatalog(catalog with { WaveRuntime = catalog.WaveRuntime! with { CardOffer = new("first-slot") } }));
    }

    [Fact]
    public void MinimumAndMaximumInputLevelsDelayTheUnlock()
    {
        foreach (var (unlock, required) in new[] { (Unlock(bladeLevel: 3), 3), (Unlock(bladeMax: true), Catalog().WaveRuntime!.Gear["core:iron_blade"].Levels!.Length) })
        {
            var catalog = With(Catalog(), Sowing(Catalog()).Id, unlock);
            var recipe = Sowing(catalog);
            var world = new WorldState();
            foreach (var id in recipe.InputIds) { world.Equipment.Add(new() { Id = id }); }
            var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
            world.WaveRuntime!.BladePlotKill = true;
            world.Equipment.Single(e => e.Id == "core:iron_blade").Level = required - 1;
            Assert.False(system.EligibleEvolution(recipe));
            world.Equipment.Single(e => e.Id == "core:iron_blade").Level = required;
            Assert.True(system.EligibleEvolution(recipe));
        }
    }

    [Fact]
    public void EventCountsAreStateOnlyWhenDataAsksForMoreThanOne()
    {
        var plain = new WorldState();
        _ = new WaveRuntimeSystem(Catalog(), plain, new(1, true), null);
        Assert.False(plain.WaveRuntime!.CountUnlockEvents);
        WaveEvolutionUnlocks.Record(plain.WaveRuntime, "attack-kill-near-growth");
        Assert.True(plain.WaveRuntime.BladePlotKill);
        Assert.Empty(plain.WaveRuntime.UnlockEvents);

        var catalog = With(Catalog(), Sowing(Catalog()).Id, Unlock(count: 2));
        var recipe = Sowing(catalog);
        var world = new WorldState();
        foreach (var id in recipe.InputIds) { world.Equipment.Add(new() { Id = id }); }
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), null);
        Assert.True(world.WaveRuntime!.CountUnlockEvents);
        WaveEvolutionUnlocks.Record(world.WaveRuntime, "attack-kill-near-growth");
        Assert.False(system.EligibleEvolution(recipe));
        WaveEvolutionUnlocks.Record(world.WaveRuntime, "attack-kill-near-growth");
        Assert.True(system.EligibleEvolution(recipe));
        Assert.Equal(2, world.WaveRuntime.UnlockEvents["attack-kill-near-growth"]);
    }

    [Fact]
    public void EventCountsChangeTheStateHashOnlyWhenRecorded()
    {
        var catalog = With(Catalog(), Sowing(Catalog()).Id, Unlock(count: 2));
        var session = FirstPlayableTests.Session(catalog);
        var before = session.ComputeStateHash();
        WaveEvolutionUnlocks.Record(session.Simulation.World.WaveRuntime!, "attack-kill-near-growth");
        var after = session.ComputeStateHash();
        session.Simulation.World.WaveRuntime!.UnlockEvents.Clear();
        Assert.NotEqual(after, session.ComputeStateHash());
        Assert.NotEqual(before, after);
    }

    private static string LoadError(Action<JsonObject> mutate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wave-unlock-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(Data, directory);
            var file = Path.Combine(directory, "runtime", "wave-1a.json");
            var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            mutate(root["definition"]!.AsObject());
            File.WriteAllText(file, root.ToJsonString());
            try { ContentLoader.Load(directory, false, "wave-1a"); return ""; }
            catch (Exception error) { return error.Message; }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) { File.Copy(file, Path.Combine(target, Path.GetFileName(file))); }
        foreach (var folder in Directory.GetDirectories(source)) { CopyDirectory(folder, Path.Combine(target, Path.GetFileName(folder))); }
    }
    private static Action<JsonObject> SowingUnlock(string json) => definition => definition["evolutions"]!["core:sowing_sworddance"]!.AsObject()["unlock"] = JsonNode.Parse(json);

    [Fact]
    public void LoaderAcceptsValidUnlockAndRejectsContradictions()
    {
        Assert.Equal("", LoadError(SowingUnlock("""{"inputs":[{"id":"core:iron_blade","minLevel":3},{"id":"core:seed_bag","requireMaxLevel":false}],"events":[{"event":"attack-kill-near-growth","minCount":2}]}""")));
        foreach (var (json, message) in new[]
        {
            ("""{"inputs":[{"id":"core:iron_blade"},{"id":"core:iron_blade","minLevel":2}],"events":[{"event":"attack-kill-near-growth","minCount":1}]}""", "input IDs must be unique"),
            ("""{"inputs":[{"id":"core:iron_blade"}],"events":[{"event":"attack-kill-near-growth","minCount":1}]}""", "inputs must equal inputIds"),
            ("""{"inputs":[{"id":"core:iron_blade","minLevel":13},{"id":"core:seed_bag"}],"events":[{"event":"attack-kill-near-growth","minCount":1}]}""", "minLevel outside"),
            ("""{"inputs":[{"id":"core:iron_blade"},{"id":"core:seed_bag"}],"events":[{"event":"repair-completed","minCount":1}]}""", "must match the program's unit:event-gate"),
            ("""{"inputs":[{"id":"core:iron_blade"},{"id":"core:seed_bag"}],"events":[{"event":"attack-kill-near-growth","minCount":1001}]}""", "minCount outside"),
            ("""{"inputs":[{"id":"core:iron_blade"},{"id":"core:seed_bag"}],"events":[{"event":"anything","minCount":1}]}""", "unsupported event"),
        })
        {
            Assert.Contains(message, LoadError(SowingUnlock(json)));
        }
        // Step 3 applies the rules, so non-default values load; values outside the documented ranges still fail.
        Assert.Equal("", LoadError(definition => definition["cardOffer"] = JsonNode.Parse("""{"evolutionPriority":"first-slot","ownedGearWeight":0,"cards":{"core:iron_blade":{"weight":3,"maxPicks":2}}}""")));
        Assert.Contains("evolutionPriority must be none or first-slot", LoadError(definition => definition["cardOffer"] = JsonNode.Parse("""{"evolutionPriority":"last-slot"}""")));
        Assert.Contains("gear weights out of range", LoadError(definition => definition["cardOffer"] = JsonNode.Parse("""{"unownedGearWeight":0}""")));
        Assert.Contains("unknown card", LoadError(definition => definition["cardOffer"] = JsonNode.Parse("""{"cards":{"core:absent":{"weight":1}}}""")));
        Assert.Equal("", LoadError(definition => definition["cardOffer"] = JsonNode.Parse("""{"evolutionPriority":"none","cards":{"core:iron_blade":{"weight":1}}}""")));
    }
}
