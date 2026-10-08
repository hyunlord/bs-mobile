using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class FirstPlayableContentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "first-playable-content-" + Guid.NewGuid().ToString("N"));
    public FirstPlayableContentTests()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "data");
        foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }

    [Fact]
    public void ProfileLoadsFifteenMinuteContentWithoutChangingProductionCatalog()
    {
        var old = ContentLoader.Load(root, false, "production");
        var first = ContentLoader.Load(root, false, "first-playable");
        Assert.Equal(21600, old.Tuning.DurationTicks); Assert.Null(old.FirstPlayable);
        Assert.Equal(27000, first.Tuning.DurationTicks); Assert.NotNull(first.FirstPlayable);
        Assert.Equal(10, first.Weapons.Count); Assert.Equal(8, first.Tools.Count); Assert.Equal(13, first.Enemies.Count);
        Assert.Equal(8, first.Runtime!.Charters.Count); Assert.Equal(30, first.Runtime.Items.Count); Assert.Equal(8, first.Runtime.Evolutions.Count);
        Assert.Equal(new CanonicalStateHasher().Compute(ContentLoader.Load(root, false, "weapon-growth-79")), new CanonicalStateHasher().Compute(old));
    }

    [Fact]
    public void RuntimeOverridesAreEffectiveWithoutMutatingLegacyProjection()
    {
        var old = ContentLoader.Load(root, false, "production"); var first = ContentLoader.Load(root, false, "first-playable");
        Assert.Contains(old.Runtime!.Charters["core:guarded_harvest"].Effects, effect => effect.Operation == "damage-pulse");
        Assert.All(first.Runtime!.Charters.Values, charter => Assert.Contains(charter.Effects, effect => effect.Operation == "stat-add" && effect.Subject.StartsWith("attack-", StringComparison.Ordinal)));
        var metadata = ContentLoader.EffectCatalog(root, ContentLoader.LoadProfile(root, "first-playable"), false, first);
        Assert.Contains(metadata, effect => effect.EffectId == "core:guarded_harvest_fp_weapon");
        Assert.DoesNotContain(metadata, effect => effect.EffectId == "core:guarded_harvest_strike");
    }

    [Fact]
    public void AlternateHeroAndEstateRunThroughTheSameFirstPlayableLoaderAndSimulation()
    {
        var catalog = ContentLoader.Load(root, true, "first-playable");
        var options = new RunOptions(40017, "test:scout", "test:moor", "mixed", "A", Movement: "circuit");
        var run = SimulationFactory.Create(catalog, options);
        for (var tick = 0; tick < 900 && !run.IsComplete; tick++) { run.Tick(); }
        var result = run.Result();
        Assert.Equal("test:scout", result.HeroId); Assert.Equal("test:moor", result.EstateId);
        var tool = result.PerTool[catalog.Heroes["test:scout"].StartingTool];
        Assert.True(tool.Activations > 0); Assert.True(tool.GrowthProduced > 0);
        Assert.NotNull(catalog.FirstPlayable);
    }

    [Theory]
    [InlineData("missing-shape")]
    [InlineData("early-boss")]
    [InlineData("growth-mismatch")]
    [InlineData("foreign-override")]
    [InlineData("unsafe-tuning")]
    public void InvalidFirstPlayableContractsAreRejected(string mutation)
    {
        var path = Path.Combine(root, "profiles/first-playable.json"); var json = JsonNode.Parse(File.ReadAllText(path))!;
        switch (mutation)
        {
            case "missing-shape": json["firstPlayable"]!["weapons"]!.AsObject().Remove("core:iron_blade"); break;
            case "early-boss": json["firstPlayable"]!["enemies"]!["core:winter_hart"]!["firstSpawnTick"] = 100; break;
            case "growth-mismatch": json["firstPlayable"]!["evolutionGrowthRequirements"]!["core:seed_crown"]!["minimum"] = 11; break;
            case "foreign-override": json["runtimeOverrides"]!["charters"]!["core:not_selected"] = json["runtimeOverrides"]!["charters"]!["core:guarded_harvest"]!.DeepClone(); break;
            case "unsafe-tuning": json["tuningFile"] = "../data/tuning.json"; break;
        }
        File.WriteAllText(path, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ContentLoader.Load(root, false, "first-playable"));
    }

    [Theory]
    [InlineData("enemies", "core:raider", "firstSpawnTick", -1)]
    [InlineData("enemies", "core:shield_raider", "repeatTicks", -1)]
    [InlineData("enemies", "core:raider", "weight", 1000001)]
    [InlineData("weapons", "core:ember_wand", "speed", 1000001)]
    [InlineData("weapons", "core:spore_fan", "lifetimeTicks", 1000001)]
    [InlineData("weapons", "core:frost_chain", "spreadPermille", 1001)]
    [InlineData("weapons", "core:frost_chain", "burstIntervalTicks", 91)]
    [InlineData("weapons", "core:frost_chain", "spreadPermille", -1)]
    [InlineData("weapons", "core:frost_chain", "burstIntervalTicks", -1)]
    [InlineData("mapEvents", "0", "repeatTicks", -1)]
    [InlineData("mapEvents", "0", "rewardCount", -1)]
    [InlineData("mapEvents", "0", "foodCost", -1)]
    [InlineData("mapEvents", "1", "healAmount", -1)]
    [InlineData("mapEvents", "1", "experience", -1)]
    [InlineData("mapEvents", "1", "spawnRadius", 1000001)]
    [InlineData("mapEvents", "2", "health", 1000001)]
    public void OutOfBoundsFirstPlayableNumbersFailAtLoad(string family, string key, string field, int value)
    {
        var path = Path.Combine(root, "profiles/first-playable.json"); var json = JsonNode.Parse(File.ReadAllText(path))!;
        var group = json["firstPlayable"]![family]!;
        var record = family == "mapEvents" ? group[int.Parse(key, System.Globalization.CultureInfo.InvariantCulture)]! : group[key]!;
        record[field] = value;
        File.WriteAllText(path, json.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ContentLoader.Load(root, false, "first-playable"));
    }

    [Theory]
    [InlineData("building")]
    [InlineData("people")]
    public void TestToolDomainsDoNotChangeProductionCounts(string target)
    {
        var path = Path.Combine(root, "test/tools/test_spade.json"); var json = JsonNode.Parse(File.ReadAllText(path))!;
        json["growth"]!["target"] = target;
        File.WriteAllText(path, json.ToJsonString());
        var catalog = ContentLoader.Load(root, true, "first-playable");
        Assert.Equal(target, catalog.Tools["test:spade"].Growth.Target);
        Assert.Equal(10, catalog.Tools.Count);
    }

    [Fact]
    public void ExportIncludesActualGrowthStatsAndEvolutionConditions()
    {
        var source = UnityExport.Generate(root, "first-playable");
        Assert.Contains("UpgradeStat", source); Assert.Contains("EffectDescription", source);
        Assert.Contains("core:seed_crown", source); Assert.Contains("EvolutionGrowthRequirement", source);
        Assert.Contains("27000", source); Assert.DoesNotContain("희귀도는 선택 후", source);
    }

    public void Dispose() { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
}
