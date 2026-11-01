using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SowSiege.Content;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class SharedEstateContentTests
{
    [Fact]
    public void HostExtractionPreservesHistoricalCatalogAndAllPrototypeConfigurations()
    {
        var root = FindRoot();
        var meta = MetaContentLoader.Read(root);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        var historical = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "meta-catalog-e281a4fa.json")));
        Assert.True(JsonNode.DeepEquals(historical, JsonSerializer.SerializeToNode(MetaCatalogReader.Read(root), options)));
        foreach (var version in new[] { "m3a-prototype-r3", "m3a-prototype-r4", "m3a-prototype-r5", "m3a-prototype-r6", "m3a-prototype-r7", "m3a-prototype-r8" })
        {
            Assert.Equal(JsonSerializer.Serialize(AccountEstateConfigLoader.Load(root, meta, version)),
                JsonSerializer.Serialize(AccountEstateConfigReader.Load(root, meta, version)));
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "data", "meta")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository data missing.");
    }
}
