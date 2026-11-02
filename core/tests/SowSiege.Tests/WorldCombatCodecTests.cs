using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldCombatCodecTests
{
    [Fact]
    public void CheckedInOptInDataUsesTheActualCoreSnapshotContract()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "opt-in", "world-combat", "world-combat-v1.json")));
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower));
        var snapshot = document.RootElement.GetProperty("snapshot").Deserialize<WorldCombatSnapshot>(jsonOptions);
        Assert.NotNull(snapshot);
        var options = new WorldCombatOptions(snapshot);
        var encoded = WorldCombatCodec.Encode(options);
        Assert.Equal(encoded, WorldCombatCodec.Encode(WorldCombatCodec.Decode(encoded)));
        Assert.Equal(3000, snapshot.Input.InputGraceMilliseconds);
        Assert.Equal(WorldCombatMode.DirectPve, snapshot.Mode);
    }

    internal static string Hash(char value) => new(value, 64);
    internal static WorldCombatOptions Options()
    {
        var stats = new WorldCombatStats(10, 3, 1, 2, 10, 1);
        var effects = Enum.GetValues<MetaAbility>().Select(ability => new WorldResolvedEffect(ability, 5, 10, 5, true, true, true,
            new[] { new WorldRawEffect(WorldEffectSourceKind.Hero, "test:hero", Hash('A'), 5) })).ToArray();
        return new(new(Hash('E'), WorldCombatMode.DirectPve,
            new("test:profile", Hash('B'), "world-combat-v1", 1, Hash('C'), Hash('D'), 1, 1, 1, 30, 30001, PortableRandom.Algorithm),
            new(0, 0, 100, 100), new("test:hero", Hash('A'), Array.Empty<WorldHeroSkill>()), new(3000, 60, 65536),
            new[] { new WorldObjective("test:defeat", WorldObjectiveKind.DefeatDesignated, "test:cohort", 1, 0, 0),
                new WorldObjective("test:hold", WorldObjectiveKind.HoldMarker, "test:marker", 0, 2, 0),
                new WorldObjective("test:time", WorldObjectiveKind.TimeLimit, "", 0, 0, 60) },
            new[] { new WorldTroopType("test:troop", 1, stats, new[] { new WorldTroopSlot(0, 5, 5) }) },
            new[] { new WorldSpawnCohort("test:cohort", "test:enemy", 0, 1, 10, 10, stats) },
            new[] { new WorldMarker("test:marker", 5, 5, 10, 20, true) }, effects));
    }

    internal static WorldCombatOptions WithVassals(WorldCombatOptions options, VassalRunOptions vassals)
    {
        var effects = options.Snapshot.Effects.Select(e => e with
        {
            Raw = checked(e.Raw + vassals.Snapshot.Effects[(int)e.Ability].Raw),
            Bonus = Math.Min(e.Cap, checked(e.Raw + vassals.Snapshot.Effects[(int)e.Ability].Raw)),
            Sources = e.Sources.Concat(new[] { new WorldRawEffect(WorldEffectSourceKind.Vassal, vassals.Snapshot.RulesId,
                VassalRunCodec.Hash(vassals), vassals.Snapshot.Effects[(int)e.Ability].Raw) }).ToArray()
        }).ToArray();
        return new(options.Snapshot with { Effects = effects });
    }

    [Fact]
    public void CanonicalSnapshotRoundTripsAndFreezesEveryNestedList()
    {
        var options = Options(); var bytes = WorldCombatCodec.Encode(options);
        var decoded = WorldCombatCodec.Decode(bytes);
        Assert.Equal(bytes, WorldCombatCodec.Encode(decoded));
        Assert.Equal(WorldCombatCodec.Hash(options), WorldCombatCodec.Hash(decoded));
        var frozen = WorldCombatCodec.Freeze(options);
        ((WorldTroopSlot[])options.Snapshot.Troops[0].Slots)[0] = new(0, 99, 99);
        ((WorldRawEffect[])options.Snapshot.Effects[0].Sources)[0] = new(WorldEffectSourceKind.Hero, "test:hero", Hash('A'), 0);
        Assert.Equal(bytes, WorldCombatCodec.Encode(frozen));
        Assert.IsAssignableFrom<IList<WorldTroopSlot>>(frozen.Snapshot.Troops[0].Slots);
        Assert.Throws<NotSupportedException>(() => ((IList<WorldTroopSlot>)frozen.Snapshot.Troops[0].Slots)[0] = new(0, 99, 99));
    }

    [Fact]
    public void ExplicitZeroTroopsRemainAWorldSnapshot()
    {
        var s = Options().Snapshot;
        var options = new WorldCombatOptions(s with { Troops = Array.Empty<WorldTroopType>() });
        Assert.Empty(WorldCombatCodec.Decode(WorldCombatCodec.Encode(options)).Snapshot.Troops);
        Assert.NotEqual(WorldCombatCodec.Hash(Options()), WorldCombatCodec.Hash(options));
    }

    [Theory]
    [InlineData(WorldCombatMode.AutoPve)]
    [InlineData(WorldCombatMode.CityPvp)]
    public void AutoModesCarryDistinctProvenance(WorldCombatMode mode)
    {
        var s = Options().Snapshot;
        var options = new WorldCombatOptions(s with { Mode = mode, Provenance = s.Provenance with { RulesId = "army-auto-v1" } });
        Assert.Equal(mode, WorldCombatCodec.Decode(WorldCombatCodec.Encode(options)).Snapshot.Mode);
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.Encode(new(s with { Mode = mode })));
    }

    public static IEnumerable<object[]> InvalidSnapshots()
    {
        var s = Options().Snapshot;
        yield return new object[] { s with { ContractHash = Hash('e') } };
        yield return new object[] { s with { Mode = (WorldCombatMode)99 } };
        yield return new object[] { s with { Provenance = s.Provenance with { RulesVersion = 2 } } };
        yield return new object[] { s with { Provenance = s.Provenance with { RngAlgorithm = "unknown" } } };
        yield return new object[] { s with { Provenance = s.Provenance with { ProfileId = "test:\u00E9" } } };
        yield return new object[] { s with { Battlefield = s.Battlefield with { MinX = -1 } } };
        yield return new object[] { s with { Input = s.Input with { InputGraceMilliseconds = 0 } } };
        yield return new object[] { s with { Input = s.Input with { MaximumChunkCommands = int.MaxValue } } };
        yield return new object[] { s with { Objectives = s.Objectives.Reverse().ToArray() } };
        yield return new object[] { s with { Objectives = new[] { s.Objectives[0], s.Objectives[0], s.Objectives[2] } } };
        yield return new object[] { s with { Objectives = new[] { s.Objectives[0] with { RequiredCount = 2 }, s.Objectives[1], s.Objectives[2] } } };
        yield return new object[] { s with { Objectives = new[] { s.Objectives[0], s.Objectives[1] with { HoldTicks = 61 }, s.Objectives[2] } } };
        yield return new object[] { s with { Objectives = new[] { s.Objectives[0], s.Objectives[1], s.Objectives[2] with { TargetId = "test:unexpected" } } } };
        yield return new object[] { s with { Troops = new[] { s.Troops[0] with { N = -1 } } } };
        yield return new object[] { s with { Troops = new[] { s.Troops[0] with { N = int.MaxValue } } } };
        yield return new object[] { s with { Troops = new[] { s.Troops[0] with { Slots = new[] { new WorldTroopSlot(1, 5, 5) } } } } };
        yield return new object[] { s with { Troops = new[] { s.Troops[0] with { Stats = s.Troops[0].Stats with { Health = 0 } } } } };
        yield return new object[] { s with { Cohorts = new[] { s.Cohorts[0] with { SpawnTick = 60 } } } };
        yield return new object[] { s with { Cohorts = new[] { s.Cohorts[0] with { X = 101 } } } };
        yield return new object[] { s with { Markers = new[] { s.Markers[0] with { Radius = -1 } } } };
        yield return new object[] { s with { Effects = s.Effects.Reverse().ToArray() } };
        yield return new object[] { s with { Effects = Array.Empty<WorldResolvedEffect>() } };
    }

    [Theory]
    [MemberData(nameof(InvalidSnapshots))]
    public void RejectsNoncanonicalOrInconsistentSnapshot(WorldCombatSnapshot snapshot) =>
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.Encode(new(snapshot)));

    [Fact]
    public void RawEffectsAreSummedBeforeOneCap()
    {
        var s = Options().Snapshot; var effects = s.Effects.ToArray(); var effect = effects[0];
        effects[0] = effect with { Raw = 15, Cap = 12, Bonus = 12, Sources = new[] { effect.Sources[0], new WorldRawEffect(WorldEffectSourceKind.Research, "test:research", Hash('F'), 10) } };
        var options = new WorldCombatOptions(s with { Effects = effects });
        Assert.Equal(12, WorldCombatCodec.Decode(WorldCombatCodec.Encode(options)).Snapshot.Effects[0].Bonus);
        effects[0] = effects[0] with { Bonus = 15 };
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.Encode(options));
        effects[0] = effect with { Raw = int.MaxValue, Bonus = 10, Sources = new[] { effect.Sources[0] with { Raw = int.MaxValue }, new WorldRawEffect(WorldEffectSourceKind.Research, "test:research", Hash('F'), 1) } };
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.Encode(options));
    }

    [Fact]
    public void DuplicateSourcesAndUnknownHeroProvenanceAreRejected()
    {
        var s = Options().Snapshot; var effects = s.Effects.ToArray(); var effect = effects[0];
        effects[0] = effect with { Raw = 10, Bonus = 10, Sources = new[] { effect.Sources[0], effect.Sources[0] } };
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.Encode(new(s with { Effects = effects })));
        effects[0] = effect with { Sources = new[] { effect.Sources[0] with { SourceId = "test:other" } } };
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.Encode(new(s with { Effects = effects })));
    }

    [Fact]
    public void VassalRawContributionIsBoundToItsUnchangedPayload()
    {
        var vassals = VassalRunTests.Options(MetaAbility.Attack); var s = Options().Snapshot;
        var effects = s.Effects.Select(e => e with
        {
            Raw = checked(e.Raw + vassals.Snapshot.Effects[(int)e.Ability].Raw),
            Bonus = Math.Min(e.Cap, checked(e.Raw + vassals.Snapshot.Effects[(int)e.Ability].Raw)),
            Sources = new[] { e.Sources[0], new WorldRawEffect(WorldEffectSourceKind.Vassal, vassals.Snapshot.RulesId, VassalRunCodec.Hash(vassals), vassals.Snapshot.Effects[(int)e.Ability].Raw) }
        }).ToArray();
        var options = new WorldCombatOptions(s with { Effects = effects });
        WorldCombatCodec.ValidateVassals(options, vassals);
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.ValidateVassals(options, null));
        Assert.Throws<ArgumentException>(() => WorldCombatCodec.ValidateVassals(options, VassalRunTests.Options(MetaAbility.Health)));
    }

    [Fact]
    public void AllTruncatedPayloadsAndTrailingBytesAreRejected()
    {
        var bytes = WorldCombatCodec.Encode(Options());
        for (int length = 0; length < bytes.Length; length++)
        { Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(bytes[..length])); }
        Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(new byte[WorldCombatCodec.MaximumBytes + 1]));
    }

    [Theory]
    [InlineData(0, int.MaxValue)]
    [InlineData(19, 2)]
    [InlineData(23, 0)]
    [InlineData(27, -1)]
    public void ResignedMalformedWireStillFailsClosed(int offset, int value)
    {
        // 4-byte text length + 15-byte magic, schema, flags, then contract-hash text length.
        var bytes = WorldCombatCodec.Encode(Options()); BitConverter.GetBytes(value).CopyTo(bytes, offset);
        Resign(bytes); Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(bytes));
    }

    [Fact]
    public void AlteredBodyCannotReuseTheOriginalChecksum()
    {
        var bytes = WorldCombatCodec.Encode(Options()); bytes[40] ^= 1;
        Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(bytes));
    }

    [Fact]
    public void ResignedTrailingBytesAndNoncanonicalBooleanAreRejected()
    {
        var original = WorldCombatCodec.Encode(Options());
        var trailing = new byte[original.Length + 1];
        original.AsSpan(0, original.Length - 32).CopyTo(trailing);
        Resign(trailing);
        Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(trailing));
        var bytes = WorldCombatCodec.Encode(Options());
        int markerText = LastText(bytes, "test:marker");
        bytes[markerText + "test:marker".Length + 16] = 2;
        Resign(bytes);
        Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(bytes));
    }

    [Theory]
    [InlineData(28, int.MaxValue)]
    [InlineData(28, -1)]
    [InlineData(32, 1)]
    public void ResignedTroopSlotCountAndOrdinalAreChecked(int relativeOffset, int value)
    {
        var bytes = WorldCombatCodec.Encode(Options());
        int troopText = LastText(bytes, "test:troop");
        BitConverter.GetBytes(value).CopyTo(bytes, troopText + "test:troop".Length + relativeOffset);
        Resign(bytes);
        Assert.Throws<InvalidDataException>(() => WorldCombatCodec.Decode(bytes));
    }

    private static int LastText(byte[] bytes, string text)
    {
        var pattern = System.Text.Encoding.UTF8.GetBytes(text);
        for (int i = bytes.Length - pattern.Length; i >= 0; i--)
        {
            if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern)) { return i; }
        }
        throw new InvalidOperationException("Test wire marker is absent.");
    }

    private static void Resign(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
}
