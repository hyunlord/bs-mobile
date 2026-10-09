using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class WaveContentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "wave-content-" + Guid.NewGuid().ToString("N"));
    public WaveContentTests()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "data"), "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "data"), file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }
    [Fact]
    public void NewProfileHasExactApprovedScopeAndLeavesHistoricalProfilesIsolated()
    {
        var old = ContentLoader.Load(root, false, "production");
        var historical = ContentLoader.Load(root, false, "first-playable");
        var wave = ContentLoader.Load(root, false, "wave-1a");
        Assert.Null(old.WaveRuntime); Assert.Null(historical.WaveRuntime);
        Assert.Equal(10, historical.Weapons.Count); Assert.Equal(8, historical.Tools.Count);
        Assert.Null(wave.Runtime); Assert.Null(wave.FirstPlayable); Assert.Null(wave.Experiment);
        Assert.Equal(5, wave.Weapons.Count); Assert.Equal(4, wave.Tools.Count); Assert.Equal(7, wave.Enemies.Count);
        Assert.Equal(8, wave.WaveRuntime!.Items.Count); Assert.Equal(3, wave.WaveRuntime.Evolutions.Count);
        Assert.Equal(28, ContentLoader.ReadWaveFile(root).Bindings.Length);
        Assert.Equal(new CanonicalStateHasher().Compute(historical.Tuning), new CanonicalStateHasher().Compute(wave.Tuning));
    }
    [Theory]
    [InlineData("path")]
    [InlineData("revision")]
    [InlineData("id")]
    [InlineData("kind")]
    [InlineData("handler")]
    [InlineData("signature")]
    [InlineData("unknown-field")]
    [InlineData("enum-number")]
    [InlineData("missing-field")]
    [InlineData("zero-cadence")]
    [InlineData("foreign-selection")]
    [InlineData("legacy-mix")]
    [InlineData("recipe")]
    [InlineData("flat-growth")]
    [InlineData("item-equipment")]
    [InlineData("material-target")]
    public void InvalidReferenceOrUnsupportedMappingFailsClosed(string mutation)
    {
        var path = Path.Combine(root, "runtime/wave-1a.json"); var data = JsonNode.Parse(File.ReadAllText(path))!;
        var profilePath = Path.Combine(root, "profiles/wave-1a.json"); var profile = JsonNode.Parse(File.ReadAllText(profilePath))!;
        var binding = data["bindings"]![0]!; var gear = data["definition"]!["gear"]!["core:iron_blade"]!;
        switch (mutation)
        {
            case "path": binding["designRef"]!["catalog"] = "../system-design-v1.json"; break;
            case "revision": binding["designRef"]!["revision"] = "designed-v1"; break;
            case "id": binding["designRef"]!["id"] = "core:seed_bag"; break;
            case "kind": binding["kind"] = "tool"; break;
            case "handler": gear["kind"] = "Homing"; break;
            case "signature": binding["semanticSha256"] = new string('0', 64); break;
            case "unknown-field": gear["mystery"] = 1; break;
            case "enum-number": gear["kind"] = 0; break;
            case "missing-field": gear.AsObject().Remove("damage"); break;
            case "zero-cadence": gear["cooldownTicks"] = 0; break;
            case "foreign-selection": profile["selection"]!["weapons"]![0] = "core:canal_bow"; break;
            case "legacy-mix": profile["tuningFile"] = "first-playable-tuning.json"; break;
            case "flat-growth": foreach (var level in gear["levels"]!.AsArray()) { level!["range"] = gear["range"]!.DeepClone(); level["cooldownTicks"] = gear["cooldownTicks"]!.DeepClone(); } break;
            case "item-equipment": data["definition"]!["items"]!["core:meadow_buckle"]!["equipmentIds"]![0] = "core:seed_bag"; break;
            case "material-target": data["definition"]!["materialTargets"]!["meta:iron"] = "core:carpenter_hammer"; break;
            case "recipe": data["definition"]!["evolutions"]!["core:sowing_sworddance"]!["inputIds"]![0] = "core:ward_orbit"; break;
        }
        File.WriteAllText(path, data.ToJsonString()); File.WriteAllText(profilePath, profile.ToJsonString());
        var error = Record.Exception(() => ContentLoader.Load(root, false, "wave-1a"));
        Assert.True(error is InvalidDataException or System.Text.Json.JsonException, error?.ToString() ?? "Loader accepted invalid content.");
        Assert.Null(ContentLoader.Load(root, false, "production").WaveRuntime);
    }
    [Fact]
    public void CatalogParameterDriftCannotBeEnabledByRewritingOnlyTheRuntimeSignature()
    {
        var path = Path.Combine(root, "system-design-v1.json"); var data = JsonNode.Parse(File.ReadAllText(path))!;
        var blade = data["content"]!.AsArray().Single(r => r!["id"]!.GetValue<string>() == "core:iron_blade")!;
        blade["params"]!["unit:attack-shape"]!["aim"] = "nearest-enemy";
        File.WriteAllText(path, data.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ContentLoader.Load(root, false, "wave-1a"));
    }
    [Fact]
    public void UnityBridgeExportsTheWaveProfileAndAllApprovedCards()
    {
        var source = UnityExport.Generate(root, "wave-1a");
        Assert.Contains("ProfileName = \"wave-1a\"", source);
        Assert.Contains("WaveRuntimeDefinition", source);
        Assert.Contains("core:flood_tusk", source);
        Assert.Contains("core:gathering_loop", source);
        Assert.DoesNotContain("core:winter_hart\", \"", source.Split("public static global::SowSiege.Core.MetaCatalog")[0]);
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
