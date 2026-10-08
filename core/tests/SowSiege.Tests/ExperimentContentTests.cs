using System.Text.Json.Nodes;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class ExperimentContentTests
{
    [Theory]
    [InlineData("unsafe-path")]
    [InlineData("missing-config")]
    [InlineData("unknown-member")]
    [InlineData("missing-enemy")]
    [InlineData("duplicate-enemy")]
    [InlineData("zero-quadratic")]
    [InlineData("duplicate-category")]
    [InlineData("collapsed-circuit")]
    [InlineData("zero-period")]
    public void LoaderRejectsInvalidExperimentInputs(string mutation)
    {
        var root = Path.Combine(Path.GetTempPath(), "s4b-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(AppContext.BaseDirectory, "data");
            foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var profilePath = Path.Combine(root, "profiles/s4b-01.json"); var tuningPath = Path.Combine(root, "tuning-s4b-01.json");
            var profile = JsonNode.Parse(File.ReadAllText(profilePath))!; var tuning = JsonNode.Parse(File.ReadAllText(tuningPath))!;
            switch (mutation)
            {
                case "unsafe-path": profile["experiment"]!["tuningFile"] = "../tuning-s4b-01.json"; break;
                case "missing-config": tuning.AsObject().Remove("experiment"); break;
                case "unknown-member": tuning["mystery"] = 1; break;
                case "missing-enemy": tuning["enemyOverrides"]!.AsArray().RemoveAt(0); break;
                case "duplicate-enemy": tuning["enemyOverrides"]![1]!["id"] = tuning["enemyOverrides"]![0]!["id"]!.GetValue<string>(); break;
                case "zero-quadratic": tuning["experiment"]!["experience"]!["quadratic"] = 0; break;
                case "duplicate-category": tuning["experiment"]!["mixedCategoryOrder"]![1] = "weapon"; break;
                case "collapsed-circuit": tuning["experiment"]!["movement"]!["circuitOffsets"] = new JsonArray(JsonNode.Parse("{\"x\":0,\"y\":0}"), JsonNode.Parse("{\"x\":0,\"y\":0}")); break;
                case "zero-period": tuning["experiment"]!["movement"]!["decisionPeriodTicks"] = 0; break;
            }
            File.WriteAllText(profilePath, profile.ToJsonString()); File.WriteAllText(tuningPath, tuning.ToJsonString());
            Assert.ThrowsAny<Exception>(() => ContentLoader.Load(root, false, "s4b-01"));
        }
        finally { if (Directory.Exists(root)) { Directory.Delete(root, true); } }
    }
}
