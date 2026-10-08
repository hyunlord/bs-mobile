using SowSiege.Core;
using Xunit;
namespace SowSiege.Tests;
public sealed class FirstPlayableTests
{
    internal static ContentCatalog Catalog(string form = "projectile")
    {
        var c = InteractiveTests.Catalog();
        return c with { FirstPlayable = new(1, c.Weapons.Keys.ToDictionary(id => id, _ => new FirstPlayableWeaponDefinition(form, 100, 100, 90, 10, 600, 200, 3)), c.Enemies.Keys.ToDictionary(id => id, _ => new FirstPlayableEnemyDefinition("normal", 0, 0, 1)), Array.Empty<MapEventDefinition>(), c.Runtime!.Evolutions.ToDictionary(p => p.Key, p => p.Value.InputIds.Select(id => new EvolutionRequirement(id, 3)).ToArray()), 100, 25, 256) };
    }
    internal static InteractiveSession Session(ContentCatalog c) => new(c, new(new(30000, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64)));
    [Fact]
    public void NullExtensionPreservesHistoricalInitialInteractiveHash()
    {
        Assert.Equal("B404B93190CCB5A9BDDC23F99CC952A796B7AF3127F253A14F49646CC9796D13", InteractiveTests.Session().ComputeStateHash());
    }
    [Fact]
    public void NewCatalogAndHiddenAttackStateParticipateInHash()
    {
        var c = Catalog(); var a = Session(c); var b = Session(c);
        Assert.Equal(a.ComputeStateHash(), b.ComputeStateHash());
        a.Apply(new(0, 0, ReplayCommandKind.Advance));
        Assert.NotEmpty(a.View.CaptureFirstPlayable()!.Attacks);
        var hash = a.ComputeStateHash(); a.Simulation.World.FirstPlayable!.Attacks[0].Age++;
        Assert.NotEqual(hash, a.ComputeStateHash());
    }
    [Fact]
    public void OfferedRarityIsChosenBeforeSelectionAndLockedAcrossReroll()
    {
        var s = Session(Catalog()); s.Apply(new(0, 0, ReplayCommandKind.GrantLevel));
        var offered = s.View.CaptureFirstPlayable()!.Cards[0];
        s.Apply(new(1, 0, ReplayCommandKind.LockCard, CardId: offered.Id));
        s.Apply(new(2, 0, ReplayCommandKind.RerollCards));
        var kept = s.View.CaptureFirstPlayable()!.Cards.Single(c => c.Id == offered.Id);
        Assert.Equal(offered.Rarity, kept.Rarity); Assert.Equal(offered.UpgradeAmount, kept.UpgradeAmount); Assert.Equal(offered.EvolutionIds, kept.EvolutionIds);
        s.Apply(new(3, 0, ReplayCommandKind.ChooseCard, CardId: offered.Id));
        Assert.Equal(offered.Rarity, s.Simulation.World.Cards.Single().Rarity);
    }
    [Fact]
    public void EvolutionRequiresLevelsRatherThanOnlyOwnership()
    {
        var c = Catalog(); var s = Session(c); var e = c.Runtime!.Evolutions.Values.First();
        foreach (var id in e.InputIds)
        {
            if (!s.Simulation.World.Equipment.Any(x => x.Id == id))
            {
                s.Simulation.World.Equipment.Add(new() { Id = id });
            }
        }

        s.Simulation.Runtime!.UnlockEvolutions(); Assert.DoesNotContain(e.Id, s.Simulation.World.Runtime!.Evolutions);
        foreach (var equipment in s.Simulation.World.Equipment)
        {
            equipment.Level = 3;
        }

        s.Simulation.Runtime.UnlockEvolutions(); Assert.Contains(e.Id, s.Simulation.World.Runtime.Evolutions);
    }
    [Theory]
    [InlineData("sector90")]
    [InlineData("sector180")]
    [InlineData("projectile")]
    [InlineData("volley")]
    [InlineData("orbit")]
    [InlineData("field")]
    [InlineData("piercing")]
    [InlineData("chain")]
    [InlineData("boomerang")]
    [InlineData("nova")]
    public void EveryFormCausesActualDamage(string form)
    {
        var s = Session(Catalog(form)); var w = s.Simulation.World;
        w.Equipment.RemoveAll(e => !s.Catalog.Weapons.ContainsKey(e.Id));
        var enemy = s.Catalog.Enemies.Values.First();
        w.Enemies.Add(new() { Id = w.AllocateId(), Definition = enemy.Id, Position = new(w.Lord.X + (form == "orbit" ? s.Catalog.WeaponCombat!.Weapons[w.Equipment[0].Id].Levels[0].Range : 200), w.Lord.Y), Health = 100000 });
        s.State.Invulnerable = true;
        for (var i = 0; i < 70 && w.PendingCards.Length == 0; i++)
        {
            s.Apply(new(s.NextSequence, w.Tick, ReplayCommandKind.Advance));
        }

        Assert.True(w.WeaponDamage > 0, form);
    }
}
