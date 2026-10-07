using System.Text.Json.Nodes;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class ContentProfileTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void CandidatePoolDoesNotExpandRuntimeSelection()
    {
        var catalog = ContentLoader.Load(Data);
        Assert.Equal(3, catalog.Tools.Count);
        Assert.Equal(3, catalog.Weapons.Count);
        Assert.Equal(4, catalog.Enemies.Count);
        Assert.Single(catalog.Heroes);
        Assert.Single(catalog.Estates);
        var fixtures = ContentLoader.Load(Data, true);
        Assert.Equal(5, fixtures.Tools.Count);
        Assert.Equal(2, fixtures.Heroes.Count);
        Assert.Equal(2, fixtures.Estates.Count);
    }

    [Theory]
    [InlineData(false, "A", "5ECF650A1CC17E4F43318FA93F1CCBE0AB1A9502F026B2EAF2E0BF15B6403DB2")]
    [InlineData(false, "B", "A8E97DE9C3A4DE366D209E01C94366746D21495BFF6D16580833DE518D5B6806")]
    [InlineData(false, "C", "87D5616F91E7863AE9BD8AB57A23E6318035EB4ACA4F5A0470214CDBFC474C4A")]
    [InlineData(true, "A", "49CD876A2AC043114D0DFF856362CF7E1389892F17CBFB5C6E80D3EC4C2CFECA")]
    [InlineData(true, "B", "123CF41134F5D1D3B6925AB970A30A984B85CA8079FB2043D8CAFC1C246B0C76")]
    [InlineData(true, "C", "A16A9F0CEE46D1DC5D43EC450D030302730AE29AC4D7654662E742FA2A2F0358")]
    public void FullRunPreservesCommittedS2GoldenHash(bool dummy, string rule, string expected)
    {
        var catalog = ContentLoader.Load(Data, dummy);
        Assert.Equal(21600, catalog.Tuning.DurationTicks);
        var result = SimulationTests.Finish(catalog, new(42, dummy ? "test:scout" : "core:founder", dummy ? "test:moor" : "core:meadow", "mixed", rule));
        Assert.Equal(expected, result.Hash);
    }

    [Fact]
    public void UnsafeProfilePathIsRejected() => Assert.Throws<InvalidDataException>(() => ContentLoader.Load(Data, false, "../tuning"));

    [Theory]
    [InlineData("candidate")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("wrong-kind")]
    public void ProfileAndMetadataBoundaryRejectsInvalidContent(string mutation)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "bs-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var file in Directory.GetFiles(Data, "*.json", SearchOption.AllDirectories))
            {
                var target = Path.Combine(temporary, Path.GetRelativePath(Data, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var fileName = mutation == "wrong-kind" ? "profiles/s2-baseline.json" : "tools/seed_bag.json";
            var filePath = Path.Combine(temporary, fileName);
            var record = JsonNode.Parse(File.ReadAllText(filePath))!;
            if (mutation == "candidate")
            {
                record["designStatus"] = "candidate";
            }

            if (mutation == "unknown")
            {
                record["unmappedMetadata"] = "must fail";
            }

            if (mutation == "missing")
            {
                record.AsObject().Remove("concept");
            }

            if (mutation == "wrong-kind")
            {
                record["selection"]!["tools"]![0] = "core:founder";
            }

            File.WriteAllText(filePath, record.ToJsonString());
            Assert.ThrowsAny<Exception>(() => ContentLoader.Load(temporary));
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, true);
            }
        }
    }
}
