using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldCombatApiTests
{
    private static (ContentCatalog Catalog, ReplayHeader Header) Contract(int hold = 3, int limit = 10)
    {
        var catalog = WaveEstateTests.Wave();
        return (catalog, WorldCombatApi.CreateContract(catalog, WorldCombatRuntimeTests.Options(catalog, hold, limit)));
    }

    private static ReplayCommand Advance(long sequence, int tick) => new(sequence, tick, ReplayCommandKind.Advance);
    private static string DataHash(ReplayHeader header) => header.Options.DataHash;
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<WorldCombatApiException>(action).Code);
    private static ReplayHeader WithPolicy(ContentCatalog catalog, ReplayHeader header, int commands, int bytes)
    {
        var snapshot = header.Options.WorldCombat!.Snapshot;
        return WorldCombatApi.CreateContract(catalog, header.Options with
        { WorldCombat = new(snapshot with { Input = snapshot.Input with { MaximumChunkCommands = commands, MaximumChunkBytes = bytes } }) });
    }

    [Fact]
    public void ContractFreezesNestedInputAndPreservesConfiguredGrace()
    {
        var catalog = WaveEstateTests.Wave(); var options = WorldCombatRuntimeTests.Options(catalog);
        var header = WorldCombatApi.CreateContract(catalog, options);
        var before = WorldCombatCodec.Encode(header.Options.WorldCombat!);
        ((WorldMarker[])options.WorldCombat!.Snapshot.Markers)[0] = options.WorldCombat.Snapshot.Markers[0] with { Health = 999 };
        Assert.Equal(before, WorldCombatCodec.Encode(header.Options.WorldCombat!));
        Assert.Equal(ReplayCodec.WorldCombatVersion, header.FormatVersion);
        Assert.Equal(options.WorldCombat.Snapshot.Input.InputGraceMilliseconds, header.Options.WorldCombat!.Snapshot.Input.InputGraceMilliseconds);
    }

    [Fact]
    public void AbsentContractIsRejectedWithoutChangingLegacyHeaderSelection()
    {
        var catalog = WaveEstateTests.Wave(); var options = WorldCombatRuntimeTests.Options(catalog) with { WorldCombat = null };
        var version = ReplayCodec.Header(options).FormatVersion;
        Error("invalid-contract", () => WorldCombatApi.CreateContract(catalog, options));
        Assert.Equal(version, ReplayCodec.Header(options).FormatVersion);
        Assert.NotEqual(ReplayCodec.WorldCombatVersion, version);
    }

    [Fact]
    public void AcceptedPrefixRestoresExactlyAndRemainsNonterminal()
    {
        var (catalog, header) = Contract();
        var frontier = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        Assert.Equal(1, frontier.Tick); Assert.Equal(1, frontier.NextSequence); Assert.Null(frontier.Result);
        Assert.Equal(Advance(0, 0), Assert.Single(frontier.AppendedCommands));
        var restored = WorldRecoveryCodec.Restore(catalog, DataHash(header), frontier.Recovery);
        Assert.Equal(frontier.StateHash, restored.ComputeStateHash());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 2)]
    public void DuplicateMissingAndWrongTickCommandsCannotRewriteAcceptedFrontier(long sequence, int tick)
    {
        var (catalog, header) = Contract();
        var frontier = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        var bytes = frontier.Recovery.ToArray();
        Error("frontier-conflict", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, frontier.Recovery, new[] { Advance(sequence, tick) }));
        Assert.Equal(bytes, frontier.Recovery);
        Assert.Equal(frontier.StateHash, WorldRecoveryCodec.Restore(catalog, DataHash(header), bytes).ComputeStateHash());
    }

    [Theory]
    [InlineData(ReplayCommandKind.SetInvulnerable)]
    [InlineData(ReplayCommandKind.SetSpawnPermille)]
    [InlineData(ReplayCommandKind.GrantLevel)]
    [InlineData(ReplayCommandKind.HostGrantRerollV1)]
    [InlineData(ReplayCommandKind.HostReviveV1)]
    public void PublicPrefixRejectsReviewAndHostCommands(ReplayCommandKind kind)
    {
        var (catalog, header) = Contract();
        Error("invalid-command", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { new ReplayCommand(0, 0, kind) }));
    }

    [Fact]
    public void ChunkCountBoundaryAcceptsExactMaximumAndRejectsOneMore()
    {
        var (catalog, original) = Contract(); var header = WithPolicy(catalog, original, 1, 65536);
        var accepted = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        Assert.Equal(1, accepted.NextSequence);
        Error("chunk-limit", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0), Advance(1, 1) }));
    }

    [Fact]
    public void EmptyChunkIsNotANewAcceptedPrefix()
    {
        var (catalog, header) = Contract();
        Error("invalid-prefix", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, Array.Empty<ReplayCommand>()));
        var initial = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 0);
        Assert.Equal(0, initial.NextSequence); Assert.Equal(0, initial.Tick); Assert.Empty(initial.AppendedCommands);
    }

    [Fact]
    public void ChunkByteBoundaryUsesActualCanonicalCommandSize()
    {
        var (catalog, original) = Contract(); var command = Advance(0, 0);
        using var wire = new MemoryStream(); ReplayCodec.WriteCommand(wire, command);
        var exact = WithPolicy(catalog, original, 1, checked((int)wire.Length));
        Assert.Equal(1, WorldCombatApi.AcceptPrefix(catalog, DataHash(exact), exact, null, new[] { command }).NextSequence);
        var shortLimit = WithPolicy(catalog, original, 1, checked((int)wire.Length - 1));
        Error("chunk-limit", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(shortLimit), shortLimit, null, new[] { command }));
    }

    [Fact]
    public void InvalidTailCannotCommitTheValidBeginningOfAChunk()
    {
        var (catalog, header) = Contract(hold: 10);
        var accepted = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        var original = accepted.Recovery.ToArray();
        Error("invalid-command", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, accepted.Recovery,
            new[] { Advance(1, 1), new ReplayCommand(2, 2, ReplayCommandKind.ChooseCard, CardId: "test:not-offered") }));
        Assert.Equal(original, accepted.Recovery);
        var resumed = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, accepted.Recovery, new[] { Advance(1, 1) });
        Assert.Equal(2, resumed.Tick); Assert.Equal(2, resumed.NextSequence);
    }

    [Fact]
    public void SameContractHashCannotAuthorizeDifferentIssuedHeader()
    {
        var (catalog, header) = Contract();
        var accepted = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        var changed = WithPolicy(catalog, header, 1, 65536);
        Assert.Equal(header.Options.WorldCombat!.Snapshot.ContractHash, changed.Options.WorldCombat!.Snapshot.ContractHash);
        Error("contract-mismatch", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(changed), changed, accepted.Recovery, new[] { Advance(1, 1) }));
    }

    [Fact]
    public void ConsumedDataAndRecoveryRuntimeMustMatch()
    {
        var (catalog, header) = Contract();
        Error("data-mismatch", () => WorldCombatApi.AcceptPrefix(catalog, WorldCombatCodecTests.Hash('F'), header, null, new[] { Advance(0, 0) }));
        var accepted = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        var document = WorldRecoveryCodec.Decode(accepted.Recovery);
        var runtime = document.WorldRuntimeBytes.ToArray(); runtime[^1] ^= 1;
        var tampered = WorldRecoveryCodec.Encode(document with { WorldRuntimeBytes = runtime });
        Error("invalid-prefix", () => WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, tampered, 2));
    }

    [Fact]
    public void NoInputStopsAtFirstSuccessAndRecordsReplayableCommands()
    {
        var (catalog, header) = Contract();
        var frontier = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 10);
        Assert.NotNull(frontier.Result); Assert.Equal(WorldTerminalReason.ObjectivesCompleted, frontier.Result.Terminal);
        Assert.Equal(3, frontier.Tick); Assert.Equal(3, frontier.AppendedCommands.Count);
        Assert.All(frontier.AppendedCommands, command =>
        { Assert.Equal(ReplayCommandKind.Advance, command.Kind); Assert.Equal(0, command.Input.X); Assert.Equal(0, command.Input.Y); });
        var document = WorldRecoveryCodec.Decode(frontier.Recovery);
        var replay = new WorldReplayDocument(header, document.Commands, Array.Empty<ReplayCheckpoint>(), new(frontier.NextSequence, frontier.Result));
        Assert.Equal(frontier.StateHash, WorldCombatApi.VerifyResult(catalog, DataHash(header), header, frontier.Recovery, replay).StateHash);
        var again = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, frontier.Recovery, 10);
        Assert.Equal(frontier.StateHash, again.StateHash); Assert.Empty(again.AppendedCommands);
        Error("battle-terminal", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, frontier.Recovery, new[] { Advance(3, 3) }));
    }

    [Fact]
    public void NoInputTimeoutDoesNotInventObjectiveSuccess()
    {
        var catalog = WaveEstateTests.Wave(); var options = WorldCombatRuntimeTests.Options(catalog);
        var snapshot = options.WorldCombat!.Snapshot;
        options = options with { WorldCombat = new(snapshot with { Markers = new[] { snapshot.Markers[0] with { X = 0, Y = 0 } } }) };
        var header = WorldCombatApi.CreateContract(catalog, options);
        var frontier = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 10);
        Assert.NotNull(frontier.Result); Assert.Equal(WorldTerminalReason.Timeout, frontier.Result.Terminal);
        Assert.Equal(10, frontier.Tick); Assert.False(frontier.Result.Objectives[0].Completed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(11)]
    public void RequestedFrontierCannotRewindOrExtendBattle(int target)
    {
        var (catalog, header) = Contract();
        var accepted = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        Error("invalid-frontier", () => WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, accepted.Recovery, target));
    }

    [Fact]
    public void LateInputCannotReplaceAnAlreadyConfirmedNoInputTick()
    {
        var (catalog, header) = Contract(hold: 10);
        var frontier = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 2);
        Error("frontier-conflict", () => WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, frontier.Recovery,
            new[] { new ReplayCommand(1, 1, ReplayCommandKind.Advance, new PlayerInput(1000, 0)) }));
        Assert.Equal(2, WorldRecoveryCodec.Decode(frontier.Recovery).Tick);
    }

    [Fact]
    public void NoInputRecordsOrdinalDefaultCardAfterARealKillAndReplaysTheChoice()
    {
        var catalog = WaveEstateTests.Wave();
        var enemy = catalog.WaveRuntime!.Enemies.Keys.OrderBy(id => id, StringComparer.Ordinal).First();
        var enemies = catalog.Enemies.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        enemies[enemy] = enemies[enemy] with { Experience = 1 };
        catalog = catalog with
        {
            Experiment = null,
            Enemies = enemies,
            Tuning = catalog.Tuning with
            {
                World = catalog.Tuning.World with
                { Progression = catalog.Tuning.World.Progression with { BaseExperience = 1, ExperiencePerLevel = 1000 } }
            }
        };
        var options = WorldCombatRuntimeTests.Options(catalog, hold: 10);
        var snapshot = options.WorldCombat!.Snapshot; var marker = snapshot.Markers[0];
        options = options with
        {
            WorldCombat = new(snapshot with
            {
                Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, 1, marker.X + 30, marker.Y, new(1, 0, 0, 0, 0, 1)) },
                Troops = new[] { new WorldTroopType("test:troop", 1, new(100, 100, 0, 0, 100, 1), new[] { new WorldTroopSlot(0, marker.X, marker.Y) }) }
            })
        };
        var header = WorldCombatApi.CreateContract(catalog, options);
        var prefix = WorldCombatApi.AcceptPrefix(catalog, DataHash(header), header, null, new[] { Advance(0, 0) });
        var session = WorldRecoveryCodec.Restore(catalog, DataHash(header), prefix.Recovery);
        Assert.Equal(RunStatus.AwaitingCard, session.View.Status);
        var expectedCard = session.View.CaptureCards().Cards.OrderBy(id => id, StringComparer.Ordinal).First();
        var frontier = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, prefix.Recovery, 2);
        var choice = Assert.Single(frontier.AppendedCommands, command => command.Kind == ReplayCommandKind.ChooseCard);
        Assert.Equal(expectedCard, choice.CardId); Assert.Equal(1, choice.Tick); Assert.Equal(1, choice.Sequence);
        Assert.Equal(2, frontier.Tick);
        Assert.Equal(frontier.StateHash, WorldRecoveryCodec.Restore(catalog, DataHash(header), frontier.Recovery).ComputeStateHash());
    }

    [Fact]
    public void ResultClaimRequiresReexecutionEvenWhenItsEncodingIsValid()
    {
        var (catalog, header) = Contract();
        var accepted = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 1);
        var frontier = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 10);
        var result = Assert.IsType<WorldCombatResult>(frontier.Result);
        var end = new WorldReplayEnd(frontier.NextSequence, result);
        var bytes = ReplayCodec.EncodeWorldEnd(header, end);
        var decoded = ReplayCodec.DecodeWorldEnd(header, bytes);
        Assert.Equal(bytes, ReplayCodec.EncodeWorldEnd(header, decoded));
        var forged = end with { Result = result with { StateHash = WorldCombatCodecTests.Hash('F') } };
        Assert.Equal(forged.Result.StateHash, ReplayCodec.DecodeWorldEnd(header, ReplayCodec.EncodeWorldEnd(header, forged)).Result.StateHash);
        var replay = new WorldReplayDocument(header, WorldRecoveryCodec.Decode(frontier.Recovery).Commands, Array.Empty<ReplayCheckpoint>(), forged);
        Error("invalid-result", () => WorldCombatApi.VerifyResult(catalog, DataHash(header), header, accepted.Recovery, replay));
    }

    [Fact]
    public void ValidReplayCannotReplaceAlreadyConfirmedNoInputWithDifferentPlayerInput()
    {
        var (catalog, header) = Contract(hold: 10);
        var accepted = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 2);
        var commands = new[] { new ReplayCommand(0, 0, ReplayCommandKind.Advance, new PlayerInput(1000, 0)), Advance(1, 1) };
        var alternate = QuitReplay(catalog, header, commands);
        Assert.Equal(alternate.End.Result.StateHash, WorldReplayRunner.Verify(catalog, DataHash(header), alternate).StateHash);
        Assert.NotEqual(accepted.StateHash, alternate.End.Result.StateHash);
        Error("frontier-conflict", () => WorldCombatApi.VerifyResult(catalog, DataHash(header), header, accepted.Recovery, alternate));
    }

    [Fact]
    public void ValidEarlierQuitCannotDiscardAnAcceptedFrontier()
    {
        var (catalog, header) = Contract(hold: 10);
        var accepted = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 2);
        var earlier = QuitReplay(catalog, header, new[] { Advance(0, 0) });
        Assert.Equal(earlier.End.Result.StateHash, WorldReplayRunner.Verify(catalog, DataHash(header), earlier).StateHash);
        Assert.True(earlier.End.AppliedCommands < accepted.NextSequence);
        Error("frontier-conflict", () => WorldCombatApi.VerifyResult(catalog, DataHash(header), header, accepted.Recovery, earlier));
    }

    [Fact]
    public void FinalReplayMayExtendButNotRewriteAnAcceptedPrefix()
    {
        var (catalog, header) = Contract();
        var accepted = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 1);
        var terminal = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, accepted.Recovery, 10);
        var replay = new WorldReplayDocument(header, WorldRecoveryCodec.Decode(terminal.Recovery).Commands,
            Array.Empty<ReplayCheckpoint>(), new(terminal.NextSequence, Assert.IsType<WorldCombatResult>(terminal.Result)));
        var verified = WorldCombatApi.VerifyResult(catalog, DataHash(header), header, accepted.Recovery, replay);
        Assert.Equal(terminal.StateHash, verified.StateHash); Assert.Equal(WorldTerminalReason.ObjectivesCompleted, verified.Terminal);
    }

    [Fact]
    public void AcceptedTerminalCannotBeReplacedByAnotherEndClaim()
    {
        var (catalog, header) = Contract();
        var terminal = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 10);
        var result = Assert.IsType<WorldCombatResult>(terminal.Result);
        var replay = new WorldReplayDocument(header, WorldRecoveryCodec.Decode(terminal.Recovery).Commands,
            Array.Empty<ReplayCheckpoint>(), new(terminal.NextSequence, result with { StateHash = WorldCombatCodecTests.Hash('F') }));
        Error("battle-terminal", () => WorldCombatApi.VerifyResult(catalog, DataHash(header), header, terminal.Recovery, replay));
    }

    private static WorldReplayDocument QuitReplay(ContentCatalog catalog, ReplayHeader header, IReadOnlyList<ReplayCommand> commands)
    {
        var session = new InteractiveSession(catalog, header.Options);
        foreach (var command in commands) { session.Apply(command); }
        session.QuitWorldCombat();
        return new(header, commands, Array.Empty<ReplayCheckpoint>(), new(session.NextSequence, session.GetWorldCombatResult()));
    }

    [Fact]
    public void ResultCodecRejectsTruncationTrailingBytesAndDifferentContract()
    {
        var (catalog, header) = Contract();
        var frontier = WorldCombatApi.AdvanceWithoutInput(catalog, DataHash(header), header, null, 10);
        var end = new WorldReplayEnd(frontier.NextSequence, Assert.IsType<WorldCombatResult>(frontier.Result));
        var bytes = ReplayCodec.EncodeWorldEnd(header, end);
        Assert.Throws<InvalidDataException>(() => ReplayCodec.DecodeWorldEnd(header, bytes[..^1]));
        Assert.Throws<InvalidDataException>(() => ReplayCodec.DecodeWorldEnd(header, bytes.Concat(new byte[] { 0 }).ToArray()));
        var snapshot = header.Options.WorldCombat!.Snapshot;
        var other = WorldCombatApi.CreateContract(catalog, header.Options with { WorldCombat = new(snapshot with { ContractHash = WorldCombatCodecTests.Hash('A') }) });
        Assert.NotEqual(snapshot.ContractHash, other.Options.WorldCombat!.Snapshot.ContractHash);
        Assert.Throws<InvalidDataException>(() => ReplayCodec.DecodeWorldEnd(other, bytes));
    }
}
