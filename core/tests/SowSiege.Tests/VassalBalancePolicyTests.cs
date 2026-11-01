using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalBalancePolicyTests
{
    [Fact]
    public void C05OnlyRequiresBothSourcePathsAndExplicitTrailingFlag()
    {
        Assert.False(VassalBalanceCli.C05Only(["vassal-balance", "data", "output"]));
        Assert.False(VassalBalanceCli.C05Only(["vassal-balance", "data", "output", "c05.ssreplay", "frozen-data"]));
        Assert.True(VassalBalanceCli.C05Only(["vassal-balance", "data", "output", "c05.ssreplay", "frozen-data", "--c05-only"]));
        Assert.Throws<ArgumentException>(() => VassalBalanceCli.C05Only(["vassal-balance", "data", "output", "--c05-only"]));
        Assert.Throws<ArgumentException>(() => VassalBalanceCli.C05Only(["vassal-balance", "data", "output", "c05.ssreplay", "--c05-only"]));
        Assert.Throws<ArgumentException>(() => VassalBalanceCli.C05Only(["vassal-balance", "data", "output", "c05.ssreplay", "frozen-data", "--unknown"]));
    }

    [Fact]
    public void MaximumCasesAreValidThreeVassalRostersAndAbsentControl()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var meta = MetaContentLoader.Read(data);
        var cases = VassalBalanceCli.Cases(meta, VassalContentLoader.Load(data));
        Assert.Equal(5, cases.Length);
        Assert.Equal("none", cases[0].Id);
        Assert.Null(cases[0].Options);
        MetaAbility[][] expected =
        [
            [MetaAbility.Attack, MetaAbility.Health, MetaAbility.Movement],
            [MetaAbility.Attack, MetaAbility.Growth, MetaAbility.Experience],
            [MetaAbility.Attack, MetaAbility.Health, MetaAbility.Allies]
        ];
        for (var index = 1; index <= expected.Length; index++)
        {
            var options = cases[index].Options!;
            Assert.Equal(3, options.Snapshot.Roster.Count);
            Assert.All(options.Snapshot.Roster, v => { Assert.Equal(20, v.Level); Assert.Equal(5, v.Rank); Assert.True(v.Eligible); });
            Assert.Equal(expected[index - 1].Order(), options.Snapshot.Effects.Where(e => e.Bonus > 0).Select(e => e.Ability).Order());
            Assert.Equal(VassalRunCodec.Encode(options), VassalRunCodec.Encode(VassalRunCodec.Decode(VassalRunCodec.Encode(options))));
        }
    }

    [Fact]
    public void MiddleCaseUsesLevelTenRankTwoWithoutChangingMaximumUnlockCapacity()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        var cases = VassalBalanceCli.Cases(MetaContentLoader.Read(data), VassalContentLoader.Load(data));
        var middle = Assert.Single(cases, c => c.Id == "middle-attack-health-movement").Options!;
        var maximum = Assert.Single(cases, c => c.Id == "attack-health-movement").Options!;
        Assert.Equal(3, middle.Snapshot.Roster.Count);
        Assert.All(middle.Snapshot.Roster, v => { Assert.Equal(10, v.Level); Assert.Equal(2, v.Rank); Assert.True(v.Eligible); });
        Assert.Equal(maximum.Snapshot.Roster.Select(v => v.Id), middle.Snapshot.Roster.Select(v => v.Id));
        Assert.Equal(maximum.Snapshot.BasisHash, middle.Snapshot.BasisHash);
        Assert.Equal(maximum.Snapshot.EffectiveSlots, middle.Snapshot.EffectiveSlots);
        Assert.Equal(maximum.Snapshot.EffectiveLevelCap, middle.Snapshot.EffectiveLevelCap);
        Assert.Equal(new[] { MetaAbility.Attack, MetaAbility.Health, MetaAbility.Movement },
            middle.Snapshot.Effects.Where(e => e.Bonus > 0).Select(e => e.Ability));
        Assert.All(middle.Snapshot.Effects.Where(e => e.Bonus > 0), effect =>
            Assert.True(effect.Bonus < maximum.Snapshot.Effects.Single(e => e.Ability == effect.Ability).Bonus));
        Assert.Equal(VassalRunCodec.Encode(middle), VassalRunCodec.Encode(VassalRunCodec.Decode(VassalRunCodec.Encode(middle))));
    }

    [Fact]
    public void MovementUsesIdenticalTickIndexedInputsForEveryRoster()
    {
        var expected = new (short X, short Y)[] { (1000, 0), (1000, 1000), (0, 1000), (-1000, 1000), (-1000, 0), (-1000, -1000), (0, -1000), (1000, -1000) };
        for (var phase = 0; phase < expected.Length; phase++)
        {
            foreach (var offset in new[] { 0, 179, 1440 })
            {
                var input = VassalBalanceCli.Movement(phase * 180 + offset);
                Assert.Equal(expected[phase], (input.X, input.Y));
            }
        }
    }

    [Fact]
    public void CardPolicyDependsOnOfferedIdsNotTheirEnumerationOrder()
    {
        Assert.Equal("core:a", VassalBalanceCli.ChooseCard(new[] { "core:z", "core:a", "core:c" }));
        Assert.Equal("core:a", VassalBalanceCli.ChooseCard(new[] { "core:c", "core:a", "core:z" }));
        Assert.Throws<InvalidOperationException>(() => VassalBalanceCli.ChooseCard(Array.Empty<string>()));
    }
}
