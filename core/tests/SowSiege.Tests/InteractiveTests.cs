using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class InteractiveTests
{
    internal static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "production");
    internal static InteractiveSession Session() { var c = Catalog(); return new(c, new(new(30000, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64))); }
    [Fact]
    public void PortableRandomMatchesSeededNet8IncludingBoundarySeedsAndLimits()
    {
        foreach (var seed in new[] { int.MinValue, int.MaxValue, -1, 0, 1, 30000 })
        {
            var expected = new Random(seed); var actual = new PortableRandom(seed);
            for (var i = 0; i < 10000; i++) { var limit = new[] { 0, 1, 2, 1000, int.MaxValue }[i % 5]; Assert.Equal(expected.Next(limit), actual.Next(limit)); }
            Assert.Equal(10000, actual.Draws);
        }
    }
    [Fact]
    public void InvalidAndPausedCommandsDoNotMutateStateAndSnapshotsAreDetached()
    {
        var s = Session(); var initial = s.ComputeStateHash();
        Assert.Throws<ArgumentException>(() => s.Apply(new(1, 0, ReplayCommandKind.Advance, default, null, 0)));
        Assert.Equal(initial, s.ComputeStateHash());
        s.Apply(new(0, 0, ReplayCommandKind.GrantLevel, default, null, 0));
        var frame = s.View.CaptureFrame(); Assert.Equal(RunStatus.AwaitingCard, frame.Status);
        var hash = s.ComputeStateHash();
        Assert.Throws<InvalidOperationException>(() => s.Apply(new(1, 0, ReplayCommandKind.Advance, default, null, 0)));
        Assert.Equal(hash, s.ComputeStateHash());
        var cards = s.View.CaptureCards(); var id = cards.Cards[0];
        s.Apply(new(1, 0, ReplayCommandKind.LockCard, default, id, 0));
        s.Apply(new(2, 0, ReplayCommandKind.RerollCards, default, null, 0));
        Assert.Contains(id, s.View.CaptureCards().Cards);
        s.Apply(new(3, 0, ReplayCommandKind.ChooseCard, default, id, 0));
        s.Apply(new(4, 0, ReplayCommandKind.Advance, new(1000, 0), null, 0));
        Assert.Equal(0, frame.Tick); Assert.Equal(1, s.View.CaptureFrame().Tick);
        Assert.True(s.View.CaptureFrame().Lord.Position.X > frame.Lord.Position.X);
    }
    [Fact]
    public void CanonicalHashIncludesHiddenCooldownAndCatalogMutation()
    {
        var s = Session(); var hash = s.ComputeStateHash(); s.Simulation.World.Equipment[0].ReadyTick++;
        Assert.NotEqual(hash, s.ComputeStateHash());
        var c = Catalog(); var run = new InteractiveSession(c, new(new(1, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64)));
        hash = run.ComputeStateHash(); c.Tuning.World.Farms.StageTicks[0]++;
        Assert.NotEqual(hash, run.ComputeStateHash());
    }
    [Fact]
    public void InitialCatalogIdentityBindsCachedEnemyDefinitionsAfterCallerMutation()
    {
        var a = Catalog(); var b = Catalog(); var dictionary = (IDictionary<string, EnemyDefinition>)b.Enemies; var id = dictionary.Keys.First(); var original = dictionary[id];
        dictionary[id] = original with { Health = original.Health + 1 };
        var options = new InteractiveOptions(new(30000, a.Tuning.DefaultHero, a.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64));
        var first = new InteractiveSession(a, options); var second = new InteractiveSession(b, options);
        dictionary[id] = original;
        Assert.NotEqual(first.ComputeStateHash(), second.ComputeStateHash());
    }

    [Fact]
    public void CatalogEnumerationOrderIsBoundWithoutChangingLegacyRules()
    {
        var a = Catalog(); var b = Catalog(); b = b with { Runtime = b.Runtime! with { Evolutions = b.Runtime.Evolutions.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) } };
        Assert.NotEqual(a.Runtime!.Evolutions.Keys, b.Runtime!.Evolutions.Keys);
        var options = new InteractiveOptions(new(30000, a.Tuning.DefaultHero, a.Tuning.DefaultEstate, "mixed", ManualCards: true), AimMode.Movement, new string('a', 64));
        Assert.NotEqual(new InteractiveSession(a, options).ComputeStateHash(), new InteractiveSession(b, options).ComputeStateHash());
    }

}
