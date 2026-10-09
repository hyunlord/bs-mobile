using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaContentTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? throw new InvalidOperationException("Repository data missing.");
        }
    }

    [Fact]
    public void CanonicalMetaLoadsWithAllProgressionAndStartingEquipment()
    {
        var c = MetaContentLoader.Load(Root);
        Assert.Equal(10, c.Chapters.Length);
        Assert.Equal(6, c.Vassals.Length);
        Assert.Equal(20, c.Challenges.Length);
        Assert.Equal(48, c.InitialContentIds.Concat(c.Challenges.SelectMany(x => x.UnlockContentIds)).Distinct().Count());
        Assert.Equal(c, MetaContentLoader.Load(Path.Combine(Root, "data")), new CatalogComparer());
    }

    [Theory]
    [InlineData("boss")]
    [InlineData("unlock")]
    [InlineData("idle")]
    public void InvalidReferencesAndUnboundedEconomyAreRejected(string corruption)
    {
        var c = MetaContentLoader.Load(Root);
        if (corruption == "boss")
        {
            c.Chapters[0] = c.Chapters[0] with { BossId = "core:raider" };
        }

        if (corruption == "unlock")
        {
            c.Challenges[0] = c.Challenges[0] with { UnlockContentIds = Array.Empty<string>() };
        }

        if (corruption == "idle")
        {
            c = c with { Economy = c.Economy with { MaximumIdleCapSeconds = 1000000 } };
        }

        var roles = c.Materials.Select(x => x.ArtRole).Concat(c.ManorBuildings.Select(x => x.ArtRole)).Concat(c.Vassals.Select(x => x.ArtRole)).Concat(c.Chapters.SelectMany(x => x.Terrain.Select(t => t.ArtRole))).ToHashSet();
        Assert.Throws<InvalidDataException>(() => MetaContentLoader.Validate(c, ContentLoader.Load(Path.Combine(Root, "data"), profileName: "first-playable"), roles, c.Vassals.Select(x => x.Id).ToHashSet()));
    }

    private sealed class CatalogComparer : IEqualityComparer<MetaCatalog>
    {
        public bool Equals(MetaCatalog? x, MetaCatalog? y) => System.Text.Json.JsonSerializer.Serialize(x) == System.Text.Json.JsonSerializer.Serialize(y);
        public int GetHashCode(MetaCatalog obj) => obj.ContractVersion;
    }
}
