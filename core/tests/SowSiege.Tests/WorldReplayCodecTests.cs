using System.Text;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldReplayCodecTests
{
    private static string Hash(char value = 'A') => new(value, 64);
    private static InteractiveOptions Options(WorldCombatOptions? world = null)
    {
        world ??= WorldCombatCodecTests.Options();
        return new(new(world.Snapshot.Provenance.Seed, world.Snapshot.Hero.Id, "test:estate", "manual", ManualCards: true),
            AimMode.Movement, Hash(), WorldCombat: world);
    }

    private static WorldReplayEnd End(InteractiveOptions options, int tick = 0, long sequence = 0)
    {
        var s = options.WorldCombat!.Snapshot;
        return new(sequence, new(s.ContractHash, s.Mode, s.Provenance.RulesHash, s.Provenance.ContentHash,
            WorldTerminalReason.Quit, tick, s.Objectives.Select(o => new WorldObjectiveResult(o.Id,
                o.Kind == WorldObjectiveKind.TimeLimit ? tick : 0, o.Kind == WorldObjectiveKind.TimeLimit && tick == o.LimitTicks)).ToArray(),
            s.Troops.Select(t => new WorldTroopResult(t.Id, t.N, 0)).ToArray(), Hash('F')));
    }

    private static byte[] Encode(InteractiveOptions options, WorldReplayEnd? end = null, params ReplayCommand[] commands)
    {
        using var stream = new MemoryStream(); ReplayCodec.WriteHeader(stream, ReplayCodec.Header(options));
        foreach (var command in commands) { ReplayCodec.WriteCommand(stream, command); }
        ReplayCodec.WriteWorldEnd(stream, end ?? End(options)); return stream.ToArray();
    }

    private static byte[] HeaderBytes(ReplayHeader header)
    {
        using var stream = new MemoryStream(); ReplayCodec.WriteHeader(stream, header); return stream.ToArray();
    }

    private static WorldReplayDocument Decode(byte[] bytes) => ReplayCodec.ReadWorld(new MemoryStream(bytes, false));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FormatNineKeepsEstateAndMeleeIndependent(bool estate, bool melee)
    {
        var options = Options() with
        {
            Estate = estate ? new(1, "test:loadout", Hash('B')) : null,
            MeleeAim = melee ? new() : null,
            Feel = new(true, true, true),
            Field = new(),
            HostRewards = new(true, null)
        };
        var bytes = Encode(options); var replay = Decode(bytes);
        Assert.Equal(9, replay.Header.FormatVersion);
        Assert.Equal(estate, replay.Header.Options.Estate is not null);
        Assert.Equal(melee, replay.Header.Options.MeleeAim is not null);
        Assert.Equal(bytes, Encode(replay.Header.Options, replay.End, replay.Commands.ToArray()));
    }

    [Fact]
    public void WorldAndUnchangedVassalPayloadRoundTripTogether()
    {
        var vassals = VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health); var s = WorldCombatCodecTests.Options().Snapshot;
        var effects = s.Effects.Select(e => e with
        {
            Raw = checked(e.Raw + vassals.Snapshot.Effects[(int)e.Ability].Raw),
            Bonus = Math.Min(e.Cap, checked(e.Raw + vassals.Snapshot.Effects[(int)e.Ability].Raw)),
            Sources = new[] { e.Sources[0], new WorldRawEffect(WorldEffectSourceKind.Vassal, vassals.Snapshot.RulesId,
                VassalRunCodec.Hash(vassals), vassals.Snapshot.Effects[(int)e.Ability].Raw) }
        }).ToArray();
        var options = Options(new(s with { Effects = effects })) with { Vassals = vassals };
        var bytes = Encode(options); var replay = Decode(bytes);
        Assert.Equal(VassalRunCodec.Encode(vassals), VassalRunCodec.Encode(replay.Header.Options.Vassals!));
        Assert.Equal(bytes, Encode(replay.Header.Options, replay.End));
        Assert.Throws<ArgumentException>(() => Encode(options with { Vassals = VassalRunTests.Options(MetaAbility.Experience) }));
        Assert.Throws<ArgumentException>(() => Encode(options with { Vassals = null }));
    }

    [Fact]
    public void ZeroTroopsAreExplicitWorldAndNeverLegacyAbsence()
    {
        var s = WorldCombatCodecTests.Options().Snapshot;
        var options = Options(new(s with { Troops = Array.Empty<WorldTroopType>() }));
        Assert.Equal(9, ReplayCodec.Header(options).FormatVersion);
        Assert.Equal(1, ReplayCodec.Header(options with { WorldCombat = null }).FormatVersion);
        Assert.Empty(Decode(Encode(options)).End.Result.Troops);
        Assert.NotEqual(HeaderBytes(ReplayCodec.Header(options)), HeaderBytes(ReplayCodec.Header(options with { WorldCombat = null })));
    }

    [Fact]
    public void ManuallyDisabledFeelWritesCanonicalAbsentBytes()
    {
        var canonical = ReplayCodec.Header(Options());
        var disabled = canonical with { Options = canonical.Options with { Feel = new(false, false, false) } };
        Assert.Equal(HeaderBytes(canonical), HeaderBytes(disabled));
    }

    [Fact]
    public void LegacyAndWorldReadersCannotSilentlySubstituteResults()
    {
        var options = Options();
        Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(new MemoryStream(Encode(options))));
        using var legacy = new MemoryStream(); ReplayCodec.WriteHeader(legacy, ReplayCodec.Header(options with { WorldCombat = null }));
        ReplayCodec.WriteEnd(legacy, new(0, 0, ReplayEndKind.Quit, Hash())); legacy.Position = 0;
        Assert.Throws<InvalidDataException>(() => ReplayCodec.ReadWorld(legacy));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(WaveEstateTests.Catalog(), options));
    }

    [Fact]
    public void HeaderBindsSeedHeroAndProtocolVersions()
    {
        var options = Options();
        Assert.Throws<ArgumentException>(() => Encode(options with { Run = options.Run with { Seed = 7 } }));
        Assert.Throws<ArgumentException>(() => Encode(options with { Run = options.Run with { HeroId = "test:other" } }));
        var header = ReplayCodec.Header(options);
        Assert.Throws<ArgumentException>(() => HeaderBytes(header with { RulesVersion = 2 }));
        Assert.Throws<ArgumentException>(() => HeaderBytes(header with { StateCodecVersion = 2 }));
        Assert.Throws<ArgumentException>(() => HeaderBytes(header with { RngAlgorithm = "unknown" }));
        Assert.Throws<ArgumentException>(() => HeaderBytes(header with { FormatVersion = 8 }));
        Assert.Throws<ArgumentException>(() => HeaderBytes(header with { Options = options with { WorldCombat = null } }));
    }

    public static IEnumerable<object[]> InvalidEndClaims()
    {
        var options = Options(); var e = End(options); var r = e.Result;
        yield return new object[] { e with { AppliedCommands = -1 } };
        yield return new object[] { e with { AppliedCommands = long.MaxValue } };
        yield return new object[] { e with { Result = r with { ContractHash = Hash('B') } } };
        yield return new object[] { e with { Result = r with { RulesHash = Hash('B') } } };
        yield return new object[] { e with { Result = r with { ContentHash = Hash('B') } } };
        yield return new object[] { e with { Result = r with { StateHash = Hash('a') } } };
        yield return new object[] { e with { Result = r with { Mode = WorldCombatMode.AutoPve } } };
        yield return new object[] { e with { Result = r with { Mode = (WorldCombatMode)99 } } };
        yield return new object[] { e with { Result = r with { Terminal = (WorldTerminalReason)99 } } };
        yield return new object[] { e with { Result = r with { Terminal = WorldTerminalReason.AutoVictory } } };
        yield return new object[] { e with { Result = r with { Terminal = WorldTerminalReason.Timeout } } };
        yield return new object[] { e with { Result = r with { Terminal = WorldTerminalReason.ObjectivesCompleted } } };
        yield return new object[] { e with { Result = r with { TickOrRound = -1 } } };
        yield return new object[] { e with { Result = r with { TickOrRound = 61 } } };
        yield return new object[] { e with { Result = r with { Objectives = r.Objectives.Reverse().ToArray() } } };
        yield return new object[] { e with { Result = r with { Objectives = new[] { r.Objectives[0], r.Objectives[0], r.Objectives[2] } } } };
        yield return new object[] { e with { Result = r with { Objectives = Array.Empty<WorldObjectiveResult>() } } };
        yield return new object[] { e with { Result = r with { Objectives = new[] { r.Objectives[0] with { Progress = -1 }, r.Objectives[1], r.Objectives[2] } } } };
        yield return new object[] { e with { Result = r with { Objectives = new[] { r.Objectives[0] with { Progress = 2 }, r.Objectives[1], r.Objectives[2] } } } };
        yield return new object[] { e with { Result = r with { Objectives = new[] { r.Objectives[0] with { Completed = true }, r.Objectives[1], r.Objectives[2] } } } };
        yield return new object[] { e with { Result = r with { Troops = Array.Empty<WorldTroopResult>() } } };
        yield return new object[] { e with { Result = r with { Troops = new[] { r.Troops[0] with { N = 2 } } } } };
        yield return new object[] { e with { Result = r with { Troops = new[] { r.Troops[0] with { K = -1 } } } } };
        yield return new object[] { e with { Result = r with { Troops = new[] { r.Troops[0] with { K = 2 } } } } };
        yield return new object[] { e with { Result = r with { Troops = new[] { r.Troops[0], r.Troops[0] } } } };
    }

    [Theory]
    [MemberData(nameof(InvalidEndClaims))]
    public void ResultClaimsMustMatchIssuedContract(WorldReplayEnd claim) =>
        Assert.Throws<ArgumentException>(() => ReplayCodec.ValidateWorldEnd(ReplayCodec.Header(Options()), claim));

    [Fact]
    public void TerminalFrontierAndCheckpointOrderAreChecked()
    {
        var options = Options(); var command = new ReplayCommand(0, 0, ReplayCommandKind.Advance);
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options));
        ReplayCodec.WriteCheckpoint(bytes, new(0, 0, Hash())); ReplayCodec.WriteCommand(bytes, command);
        ReplayCodec.WriteCheckpoint(bytes, new(1, 1, Hash('B'))); ReplayCodec.WriteWorldEnd(bytes, End(options, 1, 1));
        var replay = Decode(bytes.ToArray()); Assert.Equal(2, replay.Checkpoints.Count); Assert.Single(replay.Commands);
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 0, 1), command)));
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 1, 0), command)));
        using var duplicate = new MemoryStream(); ReplayCodec.WriteHeader(duplicate, ReplayCodec.Header(options));
        ReplayCodec.WriteCheckpoint(duplicate, new(0, 0, Hash())); ReplayCodec.WriteCheckpoint(duplicate, new(0, 0, Hash()));
        ReplayCodec.WriteWorldEnd(duplicate, End(options)); Assert.Throws<InvalidDataException>(() => Decode(duplicate.ToArray()));
        using var wrongTick = new MemoryStream(); ReplayCodec.WriteHeader(wrongTick, ReplayCodec.Header(options));
        ReplayCodec.WriteCheckpoint(wrongTick, new(0, 1, Hash())); ReplayCodec.WriteWorldEnd(wrongTick, End(options));
        Assert.Throws<InvalidDataException>(() => Decode(wrongTick.ToArray()));
    }

    [Theory]
    [InlineData(ReplayCommandKind.SetInvulnerable, 1)]
    [InlineData(ReplayCommandKind.SetSpawnPermille, 1)]
    [InlineData(ReplayCommandKind.GrantLevel, 0)]
    [InlineData(ReplayCommandKind.HostGrantRerollV1, 1)]
    [InlineData(ReplayCommandKind.HostReviveV1, 0)]
    public void ReviewAndUnissuedHostCommandsCannotEnterWorldReplay(ReplayCommandKind kind, int value)
    {
        var options = Options();
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 0, 1), new ReplayCommand(0, 0, kind, Value: value))));
    }

    [Fact]
    public void DuplicateMissingAndReversedCommandsAreRejected()
    {
        var options = Options();
        var first = new ReplayCommand(0, 0, ReplayCommandKind.Advance); var second = new ReplayCommand(1, 1, ReplayCommandKind.Advance);
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 2, 2), first, first)));
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 1, 1), second)));
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 2, 2), second, first)));
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 1, 1), first with { Sequence = long.MaxValue })));
    }

    [Fact]
    public void LastAllowedTickIsAcceptedButLaterInputIsRejected()
    {
        var options = Options();
        var commands = Enumerable.Range(0, 60).Select(t => new ReplayCommand(t, t, ReplayCommandKind.Advance)).ToArray();
        var end = End(options, 60, 60); end = end with { Result = end.Result with { Terminal = WorldTerminalReason.Timeout } };
        Assert.Equal(60, Decode(Encode(options, end, commands)).End.Result.TickOrRound);
        var late = commands.Append(new(60, 60, ReplayCommandKind.Advance)).ToArray();
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options, End(options, 61, 61), late)));
    }

    [Fact]
    public void SuccessAtLastTickAndActualLossCountsRemainSeparateClaims()
    {
        var options = Options(); var s = options.WorldCombat!.Snapshot;
        var end = End(options, 60, 60);
        end = end with
        {
            Result = end.Result with
            {
                Terminal = WorldTerminalReason.ObjectivesCompleted,
                Objectives = s.Objectives.Select(o => new WorldObjectiveResult(o.Id,
                    o.Kind == WorldObjectiveKind.DefeatDesignated ? o.RequiredCount : o.Kind == WorldObjectiveKind.HoldMarker ? o.HoldTicks : o.LimitTicks, true)).ToArray()
            }
        };
        var commands = Enumerable.Range(0, 60).Select(t => new ReplayCommand(t, t, ReplayCommandKind.Advance)).ToArray();
        Assert.Equal(WorldTerminalReason.ObjectivesCompleted, Decode(Encode(options, end, commands)).End.Result.Terminal);
        var defeated = End(options);
        defeated = defeated with { Result = defeated.Result with { Terminal = WorldTerminalReason.HeroDefeated } };
        Assert.Equal(0, Decode(Encode(options, defeated)).End.Result.Troops[0].K);
        defeated = defeated with { Result = defeated.Result with { Troops = new[] { defeated.Result.Troops[0] with { K = 1 } } } };
        Assert.Equal(1, Decode(Encode(options, defeated)).End.Result.Troops[0].K);
    }

    [Fact]
    public void AutomaticModeCannotBeReadAsDirectReplay()
    {
        var s = WorldCombatCodecTests.Options().Snapshot;
        var options = Options(new(s with { Mode = WorldCombatMode.AutoPve, Provenance = s.Provenance with { RulesId = "army-auto-v1" } }));
        Assert.Throws<InvalidDataException>(() => Decode(Encode(options)));
    }

    [Fact]
    public void TimeProgressCannotDisagreeWithFrontierAndHoldCannotPrecedeElapsedTime()
    {
        var options = Options(); var header = ReplayCodec.Header(options); var end = End(options, 1, 1);
        var timeId = options.WorldCombat!.Snapshot.Objectives.Single(o => o.Kind == WorldObjectiveKind.TimeLimit).Id;
        var changed = end with
        {
            Result = end.Result with
            { Objectives = end.Result.Objectives.Select(o => o.Id == timeId ? o with { Progress = 0 } : o).ToArray() }
        };
        Assert.Throws<ArgumentException>(() => ReplayCodec.ValidateWorldEnd(header, changed));
        var holdId = options.WorldCombat.Snapshot.Objectives.Single(o => o.Kind == WorldObjectiveKind.HoldMarker).Id;
        changed = end with
        {
            Result = end.Result with
            { Objectives = end.Result.Objectives.Select(o => o.Id == holdId ? o with { Progress = 2, Completed = true } : o).ToArray() }
        };
        Assert.Throws<ArgumentException>(() => ReplayCodec.ValidateWorldEnd(header, changed));
    }

    [Fact]
    public void LimitAndCompletedGoalsCannotBeReplacedByLaterQuitOrTimeout()
    {
        var options = Options(); var header = ReplayCodec.Header(options); var end = End(options, 60, 60);
        Assert.Throws<ArgumentException>(() => ReplayCodec.ValidateWorldEnd(header, end));
        var goals = options.WorldCombat!.Snapshot.Objectives.Select(o => new WorldObjectiveResult(o.Id,
            o.Kind == WorldObjectiveKind.DefeatDesignated ? o.RequiredCount : o.Kind == WorldObjectiveKind.HoldMarker ? o.HoldTicks : o.LimitTicks, true)).ToArray();
        end = end with { Result = end.Result with { Terminal = WorldTerminalReason.Timeout, Objectives = goals } };
        Assert.Throws<ArgumentException>(() => ReplayCodec.ValidateWorldEnd(header, end));
        ReplayCodec.ValidateWorldEnd(header, end with { Result = end.Result with { Terminal = WorldTerminalReason.HeroDefeated } });
        ReplayCodec.ValidateWorldEnd(header, end with { Result = end.Result with { Terminal = WorldTerminalReason.RequiredMarkerDestroyed } });
    }

    [Fact]
    public void TruncatedReplayAndTrailingBytesAreRejected()
    {
        var bytes = Encode(Options());
        for (var length = 0; length < bytes.Length; length++)
        { Assert.Throws<InvalidDataException>(() => Decode(bytes[..length])); }
        Assert.Throws<InvalidDataException>(() => Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
    }

    [Theory]
    [InlineData("schema", 2)]
    [InlineData("feel", 2)]
    [InlineData("field", 2)]
    [InlineData("host", 2)]
    [InlineData("estate", 2)]
    [InlineData("melee", 2)]
    [InlineData("vassals", 2)]
    [InlineData("world-length", -1)]
    [InlineData("world-length", 0)]
    [InlineData("world-length", int.MaxValue)]
    public void MalformedEnvelopeFieldsAreRejected(string field, int value)
    {
        var bytes = Encode(Options()); var offset = EnvelopeOffset(bytes);
        var relative = field switch { "schema" => 0, "feel" => 5, "field" => 6, "host" => 7, "estate" => 8, "melee" => 9, "vassals" => 10, _ => 11 };
        if (field is "schema" or "world-length") { BitConverter.GetBytes(value).CopyTo(bytes, offset + relative); }
        else { bytes[offset + relative] = (byte)value; }
        Assert.Throws<InvalidDataException>(() => Decode(bytes));
    }

    [Theory]
    [InlineData("tag", 3)]
    [InlineData("tag", 99)]
    [InlineData("length", -1)]
    [InlineData("length", int.MaxValue)]
    [InlineData("schema", 2)]
    [InlineData("terminal", 99)]
    [InlineData("tick", -1)]
    [InlineData("objective-count", int.MaxValue)]
    [InlineData("completed", 2)]
    public void MalformedWorldEndFieldsAreRejected(string field, int value)
    {
        var options = Options(); var bytes = Encode(options); var positions = EndOffsets(bytes, HeaderBytes(ReplayCodec.Header(options)).Length);
        if (field == "completed") { bytes[positions[field]] = (byte)value; }
        else { BitConverter.GetBytes(value).CopyTo(bytes, positions[field]); }
        Assert.Throws<InvalidDataException>(() => Decode(bytes));
    }

    private static int EnvelopeOffset(byte[] bytes)
    {
        using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        SkipText(r); r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); SkipText(r); r.ReadInt32();
        for (var i = 0; i < 5; i++) { SkipText(r); }
        r.ReadBoolean(); SkipText(r); r.ReadInt32(); SkipText(r);
        return checked((int)r.BaseStream.Position);
    }

    private static Dictionary<string, int> EndOffsets(byte[] bytes, int start)
    {
        using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8); r.BaseStream.Position = start;
        var offsets = new Dictionary<string, int> { ["tag"] = start, ["length"] = start + 4 };
        r.ReadInt32(); r.ReadInt32(); SkipText(r); offsets["schema"] = checked((int)r.BaseStream.Position);
        r.ReadInt32(); r.ReadInt64(); SkipText(r); r.ReadInt32(); SkipText(r); SkipText(r);
        offsets["terminal"] = checked((int)r.BaseStream.Position); r.ReadInt32();
        offsets["tick"] = checked((int)r.BaseStream.Position); r.ReadInt32();
        offsets["objective-count"] = checked((int)r.BaseStream.Position); r.ReadInt32();
        SkipText(r); r.ReadInt32(); offsets["completed"] = checked((int)r.BaseStream.Position);
        return offsets;
    }

    private static void SkipText(BinaryReader reader)
    {
        if (reader.ReadBoolean())
        {
            var length = reader.ReadInt32();
            reader.BaseStream.Position += length;
        }
    }
}
