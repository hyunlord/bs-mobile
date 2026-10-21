using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// ADR0058 implementation step 3: weighted wave card offers, first-slot evolutions and selection caps.
public sealed class WaveCardOfferTests
{
    private static readonly string Hash = new('A', 64);
    private static ContentCatalog Catalog(WaveCardOfferDefinition? offer = null)
    {
        var c = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), false, "wave-1a");
        return c with { WaveRuntime = c.WaveRuntime! with { CardOffer = offer } };
    }
    private static InteractiveSession Session(ContentCatalog c, int seed = 30001) =>
        new(c, new(new(seed, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, Hash));
    private static string[] Grant(InteractiveSession s)
    {
        s.Apply(new(s.NextSequence, s.Simulation.World.Tick, ReplayCommandKind.GrantLevel));
        return s.Simulation.World.PendingCards.ToArray();
    }
    private static void Choose(InteractiveSession s, string id) => s.Apply(new(s.NextSequence, s.Simulation.World.Tick, ReplayCommandKind.ChooseCard, CardId: id));
    private static WaveCardOfferDefinition Cards(params (string Id, WaveCardOfferCard Card)[] cards) => new(Cards: cards.ToDictionary(c => c.Id, c => c.Card));

    [Fact]
    public void ExplicitDefaultRulesDrawExactlyLikeNoRules()
    {
        var c0 = Catalog();
        var defaults = new WaveCardOfferDefinition("none", 1, 1, c0.WaveRuntime!.Gear.Keys.ToDictionary(id => id, _ => new WaveCardOfferCard(1)));
        string Play(ContentCatalog c)
        {
            var s = Session(c);
            for (var i = 0; i < 20; i++) { var offer = Grant(s); if (offer.Length == 0) { break; } Choose(s, offer[^1]); }
            return string.Join("|", s.Simulation.World.Cards.Select(card => string.Join(",", card.Offered))) + s.ComputeStateHash();
        }
        // The codec skips default rules, so even the catalog hash and with it the full state hash match.
        Assert.Equal(Play(c0), Play(Catalog(defaults)));
    }

    [Fact]
    public void ZeroWeightCardsAreNeverOfferedAndOwnedGearWeightZeroStopsReoffers()
    {
        var never = "core:storm_fork";
        var s = Session(Catalog(Cards((never, new WaveCardOfferCard(0)))));
        for (var i = 0; i < 40; i++) { var offer = Grant(s); Assert.DoesNotContain(never, offer); if (offer.Length == 0) { break; } Choose(s, offer[0]); }

        var noReoffer = Session(Catalog(new WaveCardOfferDefinition(OwnedGearWeight: 0)));
        for (var i = 0; i < 40; i++)
        {
            var owned = noReoffer.Simulation.World.Equipment.Select(e => e.Id).ToHashSet();
            var offer = Grant(noReoffer);
            Assert.DoesNotContain(offer, owned.Contains);
            if (offer.Length == 0) { break; }
            Choose(noReoffer, offer[0]);
        }
    }

    [Fact]
    public void MaxPicksRemovesACardOnceItWasChosenThatManyTimes()
    {
        var capped = "core:iron_blade";
        var s = Session(Catalog(Cards((capped, new WaveCardOfferCard(MaxPicks: 2)))));
        var picks = 0;
        for (var i = 0; i < 60; i++)
        {
            var offer = Grant(s);
            if (picks >= 2) { Assert.DoesNotContain(capped, offer); }
            if (offer.Length == 0) { break; }
            var take = offer.Contains(capped) && picks < 2 ? capped : offer[0];
            if (take == capped) { picks++; }
            Choose(s, take);
        }
        Assert.Equal(2, s.Simulation.World.Cards.Count(card => card.Chosen == capped));
    }

    [Fact]
    public void FirstSlotPutsAnUnlockedEvolutionFirstAndOtherwiseDrawsAsBefore()
    {
        var sowing = Catalog().WaveRuntime!.Evolutions.Values.Single(e => e.Kind == WaveEvolutionKind.PlantingArc);
        var firstSlot = new WaveCardOfferDefinition(EvolutionPriority: "first-slot");
        var missing = 0;
        foreach (var seed in Enumerable.Range(30001, 12))
        {
            var withRule = Session(Catalog(firstSlot), seed);
            var without = Session(Catalog(), seed);
            Assert.Equal(Grant(without), Grant(withRule));
            Choose(withRule, withRule.Simulation.World.PendingCards[0]); Choose(without, without.Simulation.World.PendingCards[0]);
            foreach (var s in new[] { withRule, without })
            {
                foreach (var id in sowing.InputIds.Where(id => !s.Simulation.World.Equipment.Exists(e => e.Id == id))) { s.Simulation.World.Equipment.Add(new() { Id = id, Level = 1 }); }
                s.Simulation.World.WaveRuntime!.BladePlotKill = true;
            }
            Assert.Equal(sowing.Id, Grant(withRule)[0]);
            if (!Grant(without).Contains(sowing.Id)) { missing++; }
        }
        Assert.True(missing > 0, "without first-slot the evolution is only one candidate among many");
    }

    [Fact]
    public void WeightedRulesAreDeterministicAndChangeTheDraw()
    {
        string Play(ContentCatalog c)
        {
            var s = Session(c);
            for (var i = 0; i < 30; i++) { var offer = Grant(s); if (offer.Length == 0) { break; } Choose(s, offer[0]); }
            return s.ComputeStateHash();
        }
        var weighted = Catalog(new WaveCardOfferDefinition(UnownedGearWeight: 5));
        Assert.Equal(Play(weighted), Play(weighted));
        Assert.NotEqual(Play(Catalog()), Play(weighted));
    }
}
