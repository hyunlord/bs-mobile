using System.Text.Json.Nodes;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class S4ContentTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void ExplicitS4ProfileLoadsBoundedPoolWithRealMixedActions()
    {
        var catalog = ContentLoader.Load(Data, false, "s4-stage-one");
        Assert.Equal(4, catalog.Weapons.Count);
        Assert.Equal(4, catalog.Tools.Count);
        Assert.Equal(6, catalog.Enemies.Count);
        var runtime = Assert.IsType<SowSiege.Core.RuntimeCatalog>(catalog.Runtime);
        Assert.Equal(4, runtime.Charters.Count);
        Assert.Equal(12, runtime.Items.Count);
        Assert.Equal(2, runtime.Evolutions.Count);
        Assert.Equal(new[] { "building", "people" }, runtime.Equipment["core:hedge_drum"].GrowthActions.Select(g => g.Target));
        Assert.Equal(5, runtime.Tuning.WeaponSlots);
        Assert.Equal(4, runtime.Tuning.ToolSlots);
        Assert.Equal(3, catalog.Tuning.World.Progression.ToolSlots);
        Assert.Null(ContentLoader.Load(Data).Runtime);
        var dummy = ContentLoader.Load(Data, true, "s4-stage-one");
        Assert.Equal(6, dummy.Tools.Count);
        Assert.True(dummy.Heroes.ContainsKey("test:scout"));
        Assert.True(dummy.Estates.ContainsKey("test:moor"));
        var profile = ContentLoader.LoadProfile(Data, "s4-stage-one");
        var cards = ContentLoader.CardCatalog(Data, profile, false, catalog);
        Assert.Equal(12, cards.Length);
        Assert.DoesNotContain(cards, c => c.Kind == "item");
        var effects = ContentLoader.EffectCatalog(Data, profile, false, catalog);
        Assert.NotEmpty(effects);
        Assert.All(effects, e => Assert.False(string.IsNullOrWhiteSpace(e.Unit)));
    }

    [Theory]
    [InlineData("missing-projection")]
    [InlineData("unknown-projection-field")]
    [InlineData("unknown-operation")]
    [InlineData("zero-effect")]
    [InlineData("pulse-attribution")]
    [InlineData("modifier-cost")]
    [InlineData("wrong-condition")]
    [InlineData("candidate")]
    [InlineData("legacy-profile")]
    public void ExecutableProjectionBoundaryRejectsInvalidData(string mutation)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bs-s4-loader-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.GetFiles(Data, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(directory, Path.GetRelativePath(Data, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var filePath = Path.Combine(directory, "charters/guarded_harvest.json");
            var record = JsonNode.Parse(File.ReadAllText(filePath))!;
            if (mutation == "missing-projection")
            {
                record.AsObject().Remove("runtimeProjection");
            }

            if (mutation == "unknown-projection-field")
            {
                record["runtimeProjection"]!["anything"] = true;
            }

            if (mutation == "unknown-operation")
            {
                record["runtimeProjection"]!["effects"]![0]!["operation"] = "eval";
            }

            if (mutation == "pulse-attribution")
            {
                record["runtimeProjection"]!["effects"]![0]!["subject"] = "tool-growth";
            }

            if (mutation == "modifier-cost")
            {
                record["runtimeProjection"]!["effects"]![1]!["foodCost"] = 1;
            }

            if (mutation == "zero-effect")
            {
                record["runtimeProjection"]!["effects"]![0]!["amount"] = 0;
            }

            if (mutation == "wrong-condition")
            {
                record["runtimeProjection"]!["effects"]![0]!["conditions"]![0]!["value"] = "absent-tag";
            }

            if (mutation == "candidate")
            {
                record["designStatus"] = "candidate";
            }

            File.WriteAllText(filePath, record.ToJsonString());
            if (mutation == "legacy-profile")
            {
                filePath = Path.Combine(directory, "profiles/s2-baseline.json");
                var profile = JsonNode.Parse(File.ReadAllText(filePath))!;
                profile["selection"]!["weapons"]![0] = "core:harvest_scythe";
                File.WriteAllText(filePath, profile.ToJsonString());
            }
            var exception = Record.Exception(() => ContentLoader.Load(directory, false, mutation == "legacy-profile" ? "s2-baseline" : "s4-stage-one"));
            Assert.True(exception is InvalidDataException or System.Text.Json.JsonException, exception?.ToString() ?? "Invalid content was accepted.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
