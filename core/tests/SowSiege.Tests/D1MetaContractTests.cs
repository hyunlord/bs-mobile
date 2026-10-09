using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class D1MetaContractTests
{
    private static readonly string[] Groups = { "materials", "chapters", "manorBuildings", "vassals", "challenges" };
    private static string Source => Path.Combine(AppContext.BaseDirectory, "data", "meta", "progression.json");

    [Fact]
    public void MetadataProjectsWithoutChangingAnyExecutableValue()
    {
        var source = JsonNode.Parse(File.ReadAllText(Source))!.AsObject();
        foreach (var group in Groups)
        {
            foreach (var node in source[group]!.AsArray())
            {
                var record = node!.AsObject();
                Assert.True(record.ContainsKey("concept"));
                foreach (var field in new[] { "kind", "tags", "designStatus", "concept", "effect" }) { record.Remove(field); }
                if (group == "manorBuildings") { record["effect"] = record["manorEffect"]!.DeepClone(); record.Remove("manorEffect"); }
            }
        }

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
        var expected = source.Deserialize<MetaCatalog>(options);
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(MetaContentLoader.Read(Path.Combine(AppContext.BaseDirectory, "data"))));
    }

    [Theory]
    [InlineData("materials", "missing")]
    [InlineData("chapters", "effect-missing")]
    [InlineData("manorBuildings", "effect-unknown")]
    [InlineData("vassals", "unknown")]
    [InlineData("challenges", "empty-tags")]
    [InlineData("materials", "wrong-kind")]
    [InlineData("chapters", "wrong-status")]
    public void InvalidDesignMetadataIsRejectedBeforeCoreProjection(string group, string corruption)
    {
        var source = JsonNode.Parse(File.ReadAllText(Source))!.AsObject();
        var record = source[group]![0]!.AsObject();
        if (corruption == "missing") { record.Remove("concept"); }
        if (corruption == "effect-missing") { record["effect"] = new JsonObject { ["trigger"] = "trigger", ["benefit"] = "benefit" }; }
        if (corruption == "effect-unknown") { record["effect"] = new JsonObject { ["trigger"] = "trigger", ["benefit"] = "benefit", ["cost"] = "cost", ["typo"] = "invalid" }; }
        if (corruption == "unknown") { record["typo"] = true; }
        if (corruption == "empty-tags") { record["tags"] = new JsonArray(); }
        if (corruption == "wrong-kind") { record["kind"] = "chapter"; }
        if (corruption == "wrong-status") { record["designStatus"] = "pretend-implemented"; }
        var root = Path.Combine(Path.GetTempPath(), "sowsiege-d1-meta-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "meta"));
            File.WriteAllText(Path.Combine(root, "meta", "progression.json"), source.ToJsonString());
            Assert.Throws<InvalidDataException>(() => MetaContentLoader.Read(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
