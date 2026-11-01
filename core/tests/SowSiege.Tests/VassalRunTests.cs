using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalRunTests
{
    internal static VassalRunOptions Options(params MetaAbility[] abilities) => OptionsAt(20, 5, abilities);

    private static VassalRunOptions OptionsAt(int level, int rank, params MetaAbility[] abilities)
    {
        var source = MetaEngineTests.Catalog();
        var meta = source with
        {
            Economy = source.Economy with { BaseVassalSlots = 1, MaximumVassalSlots = 3, BarracksLevelsPerSlot = 4, BaseVassalLevelCap = 5, LevelCapPerForgeLevel = 2 },
            ManorBuildings = source.ManorBuildings.Select(b => b with { MaxLevel = 8 }).ToArray(),
            Vassals = source.Vassals.Select(v => v with { MaxRank = 5, RankCosts = [1, 2, 3, 4, 5] }).ToArray()
        };
        var state = MetaEngine.NewGame(meta);
        foreach (var b in meta.ManorBuildings)
        {
            state.ManorLevels[b.Id] = 8;
        }
        foreach (var id in state.Vassals.Keys.ToArray())
        {
            state.Vassals[id] = new(level, rank, 0, true);
        }
        state = state with { ActiveVassalIds = abilities.Select(a => meta.Vassals.Single(v => v.Ability == a).Id).ToArray() };
        var rules = SowSiege.Sim.VassalContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data")) with
        {
            Vassals = meta.Vassals.OrderBy(v => v.Id, StringComparer.Ordinal).Select(v => new VassalDefinition(v.Id, v.Ability)).ToArray()
        };
        return VassalRun.Create(meta, state, rules);
    }

    [Fact]
    public void MaximumRosterUsesApprovedCapsAndRoundTrips()
    {
        var options = Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience);
        Assert.Equal(97, options.Snapshot.Effects[(int)MetaAbility.Attack].Bonus);
        Assert.Equal(165, options.Snapshot.Effects[(int)MetaAbility.Health].Raw);
        Assert.Equal(150, options.Snapshot.Effects[(int)MetaAbility.Health].Bonus);
        var bytes = VassalRunCodec.Encode(options);
        Assert.Equal(bytes, VassalRunCodec.Encode(VassalRunCodec.Decode(bytes)));
        Assert.Throws<ArgumentException>(() => Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Growth, MetaAbility.Allies));
    }

    [Fact]
    public void EnabledEmptyRosterRetainsSnapshotAndRejectsTampering()
    {
        var options = Options();
        Assert.Empty(options.Snapshot.Roster);
        Assert.All(options.Snapshot.Effects, e => Assert.Equal(0, e.Bonus));
        var effects = options.Snapshot.Effects.ToArray(); effects[0] = effects[0] with { Bonus = 1 };
        Assert.Throws<ArgumentException>(() => VassalRun.Validate(options with { Snapshot = options.Snapshot with { Effects = effects } }));
        Assert.ThrowsAny<Exception>(() => VassalRunCodec.Decode(VassalRunCodec.Encode(options).Concat(new byte[] { 0 }).ToArray()));
    }

    [Fact]
    public void GrainAndExperienceAccumulateSeparatelyAndResetOnlyExplicitly()
    {
        var state = new VassalRunState(Options(MetaAbility.Growth, MetaAbility.Experience));
        Assert.Equal(1092, Enumerable.Range(0, 1000).Sum(_ => state.AdvanceGrain(1, 1)));
        Assert.Equal(1063, Enumerable.Range(0, 1000).Sum(_ => state.AwardExperience("enemy", 1)));
        Assert.Equal(1, state.AwardExperience("growth", 1));
        Assert.Equal(63, state.ExperienceRemainders["growth"]);
        state.AdvanceGrain(2, 1); state.RemoveGrain(2);
        Assert.False(state.GrainRemainders.ContainsKey(2));
        Assert.Throws<ArgumentException>(() => state.AwardExperience("tax", 1));
    }

    [Fact]
    public void GroupHealthCannotBeAmplifiedTwiceAndZeroDamageStaysZero()
    {
        var state = new VassalRunState(Options(MetaAbility.Allies, MetaAbility.Attack));
        Assert.Equal(113, state.RegisterGroup(4, 100));
        Assert.Equal(113, state.GroupMaximumHealth(4));
        Assert.Throws<ArgumentException>(() => state.RegisterGroup(4, 113));
        Assert.Equal(0, state.Scale(0, MetaAbility.Attack));
        Assert.Equal(1, state.Scale(1, MetaAbility.Attack));
        Assert.Equal(109, state.Scale(100, MetaAbility.Attack));
        Assert.Throws<OverflowException>(() => state.Scale(int.MaxValue, MetaAbility.Attack));
    }

    [Fact]
    public void ZeroEntityIdIsLegalAndOrphanStateIsRejected()
    {
        var state = new VassalRunState(Options(MetaAbility.Growth, MetaAbility.Allies));
        state.AdvanceGrain(0, 1); state.RegisterGroup(0, 10);
        state.ValidateEntities([0], [0]);
        Assert.Throws<ArgumentException>(() => state.ValidateEntities([], [0]));
        Assert.Throws<ArgumentException>(() => state.ValidateEntities([0], []));
    }

    [Fact]
    public void FrozenDefinitionsAndRosterDoNotFollowCallerChanges()
    {
        var original = Options(MetaAbility.Attack);
        var roster = original.Snapshot.Roster.ToArray(); var definitions = original.Rules.Vassals.ToArray();
        var caller = original with { Snapshot = original.Snapshot with { Roster = roster }, Rules = original.Rules with { Vassals = definitions } };
        var frozen = VassalRun.Freeze(caller); var bytes = VassalRunCodec.Encode(frozen);
        roster[0] = roster[0] with { Level = 1 }; definitions[0] = definitions[0] with { Id = "changed" };
        Assert.Equal(bytes, VassalRunCodec.Encode(frozen));
        Assert.All(frozen.Rules.Vassals, d => Assert.StartsWith("vassal-", d.Id));
    }

    [Fact]
    public void UnknownFlagsAndCoefficientsChangedWithoutSnapshotAreRejected()
    {
        var options = Options(); var bytes = VassalRunCodec.Encode(options);
        using (var stream = new MemoryStream(bytes))
        using (var reader = new BinaryReader(stream))
        using (var writer = new BinaryWriter(stream))
        {
            int length = reader.ReadInt32(); stream.Position += length + sizeof(int); writer.Write(3u);
        }
        Assert.Throws<InvalidDataException>(() => VassalRunCodec.Decode(bytes));
        var rules = options.Rules.Effects.ToArray(); rules[0] = rules[0] with { Base = 11 };
        Assert.Throws<ArgumentException>(() => VassalRun.Validate(options with { Rules = options.Rules with { Effects = rules } }));
    }

    [Theory]
    [InlineData(1, 0, 10, 20, 5)]
    [InlineData(5, 0, 22, 40, 9)]
    [InlineData(20, 5, 97, 150, 39)]
    public void ApprovedBoundaryLevelsUseIndependentBonuses(int level, int rank, int attack, int health, int movement)
    {
        var s = OptionsAt(level, rank, MetaAbility.Attack, MetaAbility.Health, MetaAbility.Movement).Snapshot;
        Assert.Equal(attack, s.Effects[(int)MetaAbility.Attack].Bonus);
        Assert.Equal(health, s.Effects[(int)MetaAbility.Health].Bonus);
        Assert.Equal(movement, s.Effects[(int)MetaAbility.Movement].Bonus);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(21, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 6)]
    public void InvalidAccountLevelOrRankIsRejected(int level, int rank) =>
        Assert.Throws<ArgumentException>(() => OptionsAt(level, rank, MetaAbility.Attack));

    [Fact]
    public void DuplicateRosterIsNotSilentlyReduced() =>
        Assert.Throws<ArgumentException>(() => Options(MetaAbility.Attack, MetaAbility.Attack));

    [Fact]
    public void OptInFileMatchesSixAccountDefinitions()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "data");
        var rules = SowSiege.Sim.VassalContentLoader.Load(root);
        var meta = SowSiege.Sim.MetaContentLoader.Read(root);
        var options = VassalRun.Create(meta, MetaEngine.NewGame(meta), rules);
        Assert.Equal(6, rules.Vassals.Count);
        Assert.Single(options.Snapshot.Roster);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(
            Path.Combine(root, "opt-in", "vassals", "vassal-run-v1.json")))), options.Snapshot.ContentHash);
    }
}
