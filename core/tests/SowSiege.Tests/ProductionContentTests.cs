using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class ProductionContentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "production-content-" + Guid.NewGuid().ToString("N"));

    public ProductionContentTests()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "data");
        foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var target = Path.Combine(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [Fact]
    public void ProductionResolvesTheSameCoreCatalogAsTheAcceptedCandidate()
    {
        var production = ContentLoader.Load(root, false, "production");
        var candidate = ContentLoader.Load(root, false, "weapon-growth-79");
        var hash = new CanonicalStateHasher();
        Assert.Equal(hash.Compute(candidate), hash.Compute(production));
        Assert.NotEqual(ContentLoader.ProfileHash(root, "production"), ContentLoader.ProfileHash(root, "weapon-growth-79"));
    }

    [Theory]
    [InlineData(20000, "weapon", "C")]
    [InlineData(20001, "random", "A")]
    public void ProductionAndCandidateHaveIdenticalGameplay(int seed, string policy, string rule)
    {
        var production = ContentLoader.Load(root, false, "production");
        var candidate = ContentLoader.Load(root, false, "weapon-growth-79");
        var options = new RunOptions(seed, production.Tuning.DefaultHero, production.Tuning.DefaultEstate, policy, rule, Movement: "circuit");
        var expected = SimulationTests.Finish(candidate, options);
        var actual = SimulationTests.Finish(production, options);
        Assert.Equal(expected.Hash, actual.Hash);
        Assert.Equal(new CanonicalStateHasher().Compute(expected), new CanonicalStateHasher().Compute(actual));
    }

    [Fact]
    public void LevelOneEditPropagatesToAllProfilesWithoutEnablingLegacyGrowth()
    {
        var file = Path.Combine(root, "weapons/iron_blade.json");
        var weapon = JsonNode.Parse(File.ReadAllText(file))!;
        weapon["growth"]!["levels"]![0]!["damage"] = 23;
        File.WriteAllText(file, weapon.ToJsonString());
        foreach (var profile in new[] { "production", "weapon-growth-79", "s2-baseline", "s4-stage-one", "s4b-01", "s4b-02" })
        {
            var catalog = ContentLoader.Load(root, false, profile);
            Assert.Equal(23, catalog.Weapons["core:iron_blade"].Activation.Damage);
            if (profile is "production" or "weapon-growth-79") { Assert.Equal(23, catalog.WeaponCombat!.Weapons["core:iron_blade"].Levels[0].Damage); }
            else { Assert.Null(catalog.WeaponCombat); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalTestWeaponsAreValidatedBeforeSelection(bool invalid)
    {
        var weapon = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "weapons/iron_blade.json")))!;
        weapon["id"] = "test:canonical_blade";
        if (invalid) { weapon["growth"]!["levels"]![0]!["damage"] = 0; }
        Directory.CreateDirectory(Path.Combine(root, "test/weapons"));
        File.WriteAllText(Path.Combine(root, "test/weapons/canonical_blade.json"), weapon.ToJsonString());
        var profileFile = Path.Combine(root, "profiles/production.json");
        var profile = JsonNode.Parse(File.ReadAllText(profileFile))!;
        profile["testSelection"]!["weapons"]!.AsArray().Add("test:canonical_blade");
        File.WriteAllText(profileFile, profile.ToJsonString());
        if (invalid) { Assert.ThrowsAny<Exception>(() => ContentLoader.Load(root, false, "production")); }
        else
        {
            Assert.False(ContentLoader.Load(root, false, "production").WeaponCombat!.Weapons.ContainsKey("test:canonical_blade"));
            Assert.True(ContentLoader.Load(root, true, "production").WeaponCombat!.Weapons.ContainsKey("test:canonical_blade"));
        }
    }

    [Fact]
    public void ConfigurationDirectorySymlinkCannotEscapeTheContentRoot()
    {
        var source = Path.Combine(root, "experiments");
        var target = Path.Combine(root, "real-experiments");
        Directory.Move(source, target);
        Directory.CreateSymbolicLink(source, target);
        Assert.Throws<InvalidDataException>(() => ContentLoader.Load(root, false, "weapon-growth-79"));
    }

    [Theory]
    [InlineData("duplicate-number")]
    [InlineData("missing-growth")]
    [InlineData("null-growth")]
    [InlineData("malformed-level")]
    [InlineData("profile-version")]
    [InlineData("manifest-version")]
    [InlineData("manifest-extra")]
    [InlineData("manifest-duplicate")]
    [InlineData("path-parent")]
    [InlineData("path-absolute")]
    [InlineData("path-backslash")]
    [InlineData("gameplay-and-experiment")]
    [InlineData("gameplay-and-baseline")]
    [InlineData("missing-enemy")]
    [InlineData("duplicate-category")]
    [InlineData("explicit-null")]
    [InlineData("legacy-duplicate")]
    public void ProductionRejectsAmbiguousOrInvalidSources(string mutation)
    {
        var weaponFile = Path.Combine(root, "weapons/iron_blade.json");
        var profileFile = Path.Combine(root, "profiles/production.json");
        var manifestFile = Path.Combine(root, "experiments/weapon-growth-79.json");
        var weapon = JsonNode.Parse(File.ReadAllText(weaponFile))!;
        var profile = JsonNode.Parse(File.ReadAllText(profileFile))!;
        var manifest = JsonNode.Parse(File.ReadAllText(manifestFile))!;
        if (mutation.StartsWith("manifest", StringComparison.Ordinal)) { profile["weaponCombat"]!["definitionsFile"] = "experiments/weapon-growth-79.json"; }
        switch (mutation)
        {
            case "duplicate-number": weapon["activation"]!["damage"] = 22; break;
            case "missing-growth": weapon.AsObject().Remove("growth"); break;
            case "null-growth": weapon["growth"] = null; break;
            case "malformed-level": weapon["growth"]!["levels"]![0]!["damage"] = 0; break;
            case "profile-version": profile["weaponCombat"]!["contractVersion"] = 3; break;
            case "manifest-version": manifest["contractVersion"] = 1; break;
            case "manifest-extra": manifest["weapons"]!.AsArray().Add("core:absent"); break;
            case "manifest-duplicate": manifest["weapons"]![1] = manifest["weapons"]![0]!.DeepClone(); break;
            case "path-parent": profile["weaponCombat"]!["definitionsFile"] = "experiments/../weapon-growth-79.json"; break;
            case "path-absolute": profile["weaponCombat"]!["definitionsFile"] = "/tmp/weapon-growth-79.json"; break;
            case "path-backslash": profile["weaponCombat"]!["definitionsFile"] = "experiments\\weapon-growth-79.json"; break;
            case "gameplay-and-experiment": profile["experiment"] = JsonNode.Parse("{\"contractVersion\":1,\"tuningFile\":\"experiments/tuning-s4b-02.json\"}"); break;
            case "gameplay-and-baseline": profile["tuningFile"] = "experiments/tuning-s2-baseline.json"; break;
            case "missing-enemy": profile["gameplay"]!["enemyOverrides"]!.AsArray().RemoveAt(0); break;
            case "duplicate-category": profile["gameplay"]!["experiment"]!["mixedCategoryOrder"]![1] = "weapon"; break;
            case "explicit-null": weapon["activation"]!["damage"] = null; break;
            case "legacy-duplicate":
                profile["weaponCombat"]!["contractVersion"] = 1;
                profile["weaponCombat"]!["definitionsFile"] = "experiments/weapon-growth-79.json";
                manifest["contractVersion"] = 1;
                var definitions = new JsonObject();
                foreach (var id in profile["selection"]!["weapons"]!.AsArray())
                {
                    var source = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "weapons", id!.GetValue<string>().Split(':')[1] + ".json")))!;
                    definitions[id.GetValue<string>()] = source["growth"]!.DeepClone();
                }
                manifest["weapons"] = definitions;
                break;
        }
        File.WriteAllText(weaponFile, weapon.ToJsonString());
        File.WriteAllText(profileFile, profile.ToJsonString());
        File.WriteAllText(manifestFile, manifest.ToJsonString());
        Assert.ThrowsAny<Exception>(() => ContentLoader.Load(root, false, "production"));
    }

    public void Dispose() => Directory.Delete(root, true);
}
