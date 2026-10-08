using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class FirstPlayableAttackViewTests
{
    [Theory]
    [InlineData("nova")]
    [InlineData("projectile")]
    [InlineData("piercing")]
    public void DeferredAttacksBecomeVisibleOnlyAfterTheirFirstProcessedTick(string form)
    {
        var session = FirstPlayableTests.Session(FirstPlayableTests.Catalog(form));
        session.Apply(new(0, 0, ReplayCommandKind.Advance));
        var world = session.Simulation.World;
        var attack = world.FirstPlayable!.Attacks[0];
        attack.Age = 0; attack.Phase = 2;
        for (var age = 0; age <= 4; age++)
        {
            var hash = session.ComputeStateHash();
            var view = session.View.CaptureFirstPlayable()!.Attacks.Single(a => a.Id == attack.Id);
            Assert.Equal(hash, session.ComputeStateHash());
            Assert.Equal(age > 2, view.IsActive);
            if (form == "nova")
            {
                Assert.Equal(Math.Min(session.Catalog.WeaponCombat!.Weapons[attack.Source].Levels[attack.Level - 1].Range,
                    Math.Max(0, age - 2) * 100), view.PresentationRadius);
            }
            else { Assert.Equal(100, view.PresentationRadius); }
            if (age < 4) { session.Apply(new(session.NextSequence, world.Tick, ReplayCommandKind.Advance)); }
        }
    }

    [Fact]
    public void NovaPresentationRadiusCapsAtItsEquippedLevelRange()
    {
        var session = FirstPlayableTests.Session(FirstPlayableTests.Catalog("nova"));
        session.Apply(new(0, 0, ReplayCommandKind.Advance));
        var attack = session.Simulation.World.FirstPlayable!.Attacks[0];
        attack.Age = 80; attack.Phase = 2;
        var view = session.View.CaptureFirstPlayable()!.Attacks.Single(a => a.Id == attack.Id);
        Assert.True(view.IsActive);
        Assert.Equal(session.Catalog.WeaponCombat!.Weapons[attack.Source].Levels[attack.Level - 1].Range, view.PresentationRadius);
        Assert.Equal(100, view.Radius);
    }
}
