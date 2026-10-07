using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class CardControlTests
{
    [Fact]
    public void LockPersistsRerollBanExcludesAndBudgetsAreConsumedDeterministically()
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), true);
        var first = Run(catalog);
        var second = Run(catalog);
        Assert.Equal(first.Hash, second.Hash);
    }

    private static SimulationResult Run(ContentCatalog catalog)
    {
        var simulation = new Simulation(catalog, new(42, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true));
        simulation.World.Experience = catalog.Tuning.World.Progression.BaseExperience;
        simulation.Tick();
        var before = simulation.PendingCards;
        Assert.Equal(3, before.Cards.Length);
        var locked = before.Cards[0];
        simulation.LockCard(locked);
        simulation.RerollCards();
        Assert.Contains(locked, simulation.PendingCards.Cards);
        Assert.Equal(before.Rerolls - 1, simulation.PendingCards.Rerolls);
        Assert.Equal(before.Locks - 1, simulation.PendingCards.Locks);
        var banned = simulation.PendingCards.Cards.First(id => id != locked);
        simulation.BanCard(banned);
        Assert.DoesNotContain(banned, simulation.PendingCards.Cards);
        Assert.Contains(locked, simulation.PendingCards.Cards);
        Assert.Equal(before.Bans - 1, simulation.PendingCards.Bans);
        Assert.Throws<InvalidOperationException>(() => simulation.Tick());
        simulation.ChooseCard(locked);
        Assert.Empty(simulation.PendingCards.Cards);
        simulation.Tick();
        return simulation.Result();
    }
}
