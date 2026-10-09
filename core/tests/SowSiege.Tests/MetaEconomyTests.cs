using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaEconomyTests
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

            return directory?.FullName ?? throw new InvalidOperationException("Repository missing.");
        }
    }

    [Fact]
    public void LedgerConservesMaterialsAndFirstDayCannotClaimPreinstallIdle()
    {
        var meta = MetaContentLoader.Load(Root);
        var content = ContentLoader.Load(Path.Combine(Root, "data"), profileName: "first-playable");
        var types = new[] { new EconomyArchetype("fixture", 2) };
        static EconomyRunObservation Outcome(ContentCatalog c, int seed) => new(new(c.Tuning.DurationTicks, c.Tuning.DurationTicks, true, true, false, 30, 100, 12, 30, 500, 100), seed.ToString());
        var rows = MetaEconomySimulation.Run(meta, content, 21, types, Outcome);
        Assert.Equal(63, rows.Count);
        Assert.All(rows.First().Credited.Values, value => Assert.Equal(0, value));
        var balances = meta.Materials.ToDictionary(m => m.Id, _ => 0);
        foreach (var row in rows)
        {
            foreach (var material in meta.Materials)
            {
                Assert.Equal(balances[material.Id] + row.Credited[material.Id] - row.Spent[material.Id], row.Wallet[material.Id]);
                Assert.InRange(row.Wallet[material.Id], 0, material.WalletCap);
                Assert.True(row.Overflow[material.Id] >= 0);
                balances[material.Id] = row.Wallet[material.Id];
            }
        }

        Assert.Equal(MetaEconomySimulation.Csv(rows, meta, "fixture"), MetaEconomySimulation.Csv(MetaEconomySimulation.Run(meta, content, 21, types, Outcome), meta, "fixture"));
    }

    [Fact]
    public void SurvivalWithoutBossKillDoesNotProduceChapterProgression()
    {
        var meta = MetaContentLoader.Load(Root);
        var content = ContentLoader.Load(Path.Combine(Root, "data"), profileName: "first-playable");
        var rows = MetaEconomySimulation.Run(meta, content, 2, new[] { new EconomyArchetype("fixture", 1) },
            (c, _) => new(new(c.Tuning.DurationTicks, c.Tuning.DurationTicks, true, false, false, 10, 10, 0, 0, 5, 3), "fixture"));
        Assert.All(rows, row => Assert.Equal(0, row.HighestCleared));
        Assert.All(rows.Where(row => row.Event == "run"), row => Assert.Contains("boss-not-defeated", row.Bottleneck));
    }
}
