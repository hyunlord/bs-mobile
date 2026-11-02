using System.Text;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class GatheringContentTests
{
    [Fact]
    public void ShippedPolicyIsLoadedFromDataAndResourceReferencesResolve()
    {
        var content = GatheringContent.Load(ContentEstateTests.Root());
        Assert.Equal(new GatheringPolicy(600, 1, 10, 600000), content.Policy);
        Assert.Equal(3, content.Materials.Count);
        Assert.Equal("meta:iron", content.Materials["meta:iron"]);
        Assert.Equal(64, content.Hash.Length);
    }
    [Theory]
    [InlineData("\"stock\": 600", "\"stock\": 0")]
    [InlineData("\"capacityPerSoldier\": 10", "\"capacityPerSoldier\": -1")]
    [InlineData("\"respawnMilliseconds\": 600000", "\"respawnMilliseconds\": 0")]
    [InlineData("\"kind\": \"slg-gathering\"", "\"kind\": \"other\"")]
    public void InvalidPoliciesFailClosed(string original, string replacement)
    {
        string path = Path.Combine(ContentEstateTests.Root(), "data", GatheringContent.DataPath);
        string json = File.ReadAllText(path);
        Assert.Contains(original, json);
        Assert.Throws<InvalidDataException>(() => GatheringContent.Parse(Encoding.UTF8.GetBytes(json.Replace(original, replacement, StringComparison.Ordinal))));
    }
}
