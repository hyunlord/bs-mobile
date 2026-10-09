using System.Text.Json.Nodes;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class D1ContentContractTests
{
    [Theory]
    [InlineData("wrong-kind")]
    [InlineData("missing-effect")]
    [InlineData("missing-trigger")]
    [InlineData("unknown-effect-field")]
    public void NormalizedMetadataIsStrictWhileLegacyFixturesRemainReadable(string mutation)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase0-r2", "data");
        var temporary = Path.Combine(Path.GetTempPath(), "bs-d1-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(temporary, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var filename = Path.Combine(temporary, "tools", "seed_bag.json");
            var record = JsonNode.Parse(File.ReadAllText(filename))!;
            record["kind"] = "tool";
            record["effect"] = new JsonObject { ["trigger"] = "activation", ["benefit"] = "growth", ["cost"] = "design-only" };
            File.WriteAllText(filename, record.ToJsonString());
            Assert.NotEmpty(ContentLoader.Load(temporary).Tools);
            if (mutation == "wrong-kind") { record["kind"] = "weapon"; }
            if (mutation == "missing-effect") { record.AsObject().Remove("effect"); }
            if (mutation == "missing-trigger") { record["effect"]!.AsObject().Remove("trigger"); }
            if (mutation == "unknown-effect-field") { record["effect"]!["damage"] = 999; }
            File.WriteAllText(filename, record.ToJsonString());
            Assert.ThrowsAny<Exception>(() => ContentLoader.Load(temporary));
        }
        finally { if (Directory.Exists(temporary)) { Directory.Delete(temporary, true); } }
    }
}
