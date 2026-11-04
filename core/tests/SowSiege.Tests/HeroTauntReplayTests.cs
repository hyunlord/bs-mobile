using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroTauntReplayTests
{
    [Fact]
    public void ActiveClaimsAndChargesRestoreByPrefixWithExactStateAndRuntimeBytes()
    {
        var fixture = HeroTauntRuntimeTests.Fixture(2); var run = new HeroTauntRuntimeTests.Run(fixture); run.Activate(2);
        var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        var accepted = WorldCombatApi.AcceptPrefix(fixture.Catalog, fixture.Options.DataHash, header, null, run.Commands);
        var restored = WorldRecoveryCodec.Restore(fixture.Catalog, fixture.Options.DataHash, accepted.Recovery);
        Assert.Equal(WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(run.Session), WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(restored));
        Assert.Equal(run.Session.State.HeroActive!.Encode(), restored.State.HeroActive!.Encode());
        Assert.Equal(accepted.Recovery, WorldRecoveryCodec.Capture(restored, run.Commands));
        Assert.Equal(2, restored.CaptureHeroActiveEffects().Taunts.Count);
        run.Step(); restored.Apply(run.Commands[^1]);
        Assert.Equal(run.Session.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void NoInputFrontierActivatesAutomaticTauntAndCannotBeReplacedByMovement()
    {
        var fixture = HeroTauntRuntimeTests.Fixture(); var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        var accepted = WorldCombatApi.AdvanceWithoutInput(fixture.Catalog, fixture.Options.DataHash, header, null, 2);
        var restored = WorldRecoveryCodec.Restore(fixture.Catalog, fixture.Options.DataHash, accepted.Recovery);
        Assert.Single(restored.CaptureHeroActiveEffects().Taunts);
        var error = Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.AcceptPrefix(fixture.Catalog, fixture.Options.DataHash,
            header, accepted.Recovery, new[] { new ReplayCommand(1, 1, ReplayCommandKind.Advance, new(1000, 0)) }));
        Assert.Equal("frontier-conflict", error.Code);
    }

    [Fact]
    public void RuntimeAndIssuedDefinitionTamperingFailClosed()
    {
        var fixture = HeroTauntRuntimeTests.Fixture(); var run = new HeroTauntRuntimeTests.Run(fixture); run.Activate();
        var recovery = WorldRecoveryCodec.Capture(run.Session, run.Commands); var candidate = WorldRecoveryCodec.Decode(recovery);
        var runtime = (byte[])candidate.HeroActiveRuntimeBytes!.Clone(); runtime[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Restore(fixture.Catalog, fixture.Options.DataHash,
            WorldRecoveryCodec.Encode(candidate with { HeroActiveRuntimeBytes = runtime })));
        var changed = HeroTauntCodecTests.Change(fixture.Options.HeroActive!, 0, p => p with { StopRadius = p.StopRadius + 1 });
        Assert.Throws<ArgumentException>(() => new InteractiveSession(fixture.Catalog, fixture.Options with { HeroActive = changed }));
    }

    [Fact]
    public void QuitClearsTauntInstancesAndCannotBecomeResumableRecovery()
    {
        var run = new HeroTauntRuntimeTests.Run(HeroTauntRuntimeTests.Fixture()); run.Activate();
        run.Session.QuitWorldCombat(); Assert.Empty(run.Frame.Taunts);
        Assert.Equal(WorldTerminalReason.Quit, run.Session.GetWorldCombatResult().Terminal);
        Assert.Throws<InvalidOperationException>(() => WorldRecoveryCodec.Capture(run.Session, run.Commands));
    }

    private static WorldReplayDocument Finish(HeroTauntRuntimeTests.Run run)
    {
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(run.Session.Options));
        foreach (var command in run.Commands) { ReplayCodec.WriteCommand(bytes, command); }
        ReplayCodec.WriteWorldEnd(bytes, new(run.Session.NextSequence, run.Session.GetWorldCombatResult()));
        bytes.Position = 0; return ReplayCodec.ReadWorld(bytes);
    }

    [Fact]
    public void TerminalVerificationPreservesAcceptedClaimsHistoryAndClearsEffects()
    {
        var fixture = HeroTauntRuntimeTests.Fixture(configureWorld: s => s with
        { Objectives = new[] { s.Objectives[0], s.Objectives[1] with { LimitTicks = 6 } } });
        var run = new HeroTauntRuntimeTests.Run(fixture); run.Activate();
        var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        var accepted = WorldCombatApi.AcceptPrefix(fixture.Catalog, fixture.Options.DataHash, header, null, run.Commands);
        for (var tick = 2; tick < 6; tick++) { run.Step(); }
        Assert.Empty(run.Frame.Taunts);
        var replay = Finish(run);
        var result = WorldCombatApi.VerifyResult(fixture.Catalog, fixture.Options.DataHash, header, accepted.Recovery, replay);
        Assert.Equal(WorldTerminalReason.Timeout, result.Terminal); Assert.Equal(run.Session.ComputeStateHash(), result.StateHash);
        var alternate = new HeroTauntRuntimeTests.Run(fixture);
        for (var tick = 0; tick < 6; tick++) { alternate.Step(1000, 0); }
        var otherReplay = Finish(alternate);
        Assert.Equal(alternate.Session.ComputeStateHash(), WorldReplayRunner.Verify(fixture.Catalog, fixture.Options.DataHash, otherReplay).StateHash);
        Assert.NotEqual(result.StateHash, otherReplay.End.Result.StateHash);
        Assert.Equal("frontier-conflict", Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.VerifyResult(
            fixture.Catalog, fixture.Options.DataHash, header, accepted.Recovery, otherReplay)).Code);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(100, false)]
    public void HoldMarkerCountsPhysicalEnemyPositionNotTauntClaim(int markerRadius, bool completes)
    {
        var fixture = HeroTauntRuntimeTests.Fixture(configureWorld: s =>
        {
            var centerX = s.Battlefield.MaxX / 2; var centerY = s.Battlefield.MaxY / 2;
            return s with
            {
                Objectives = new[] { new WorldObjective("test:hold", WorldObjectiveKind.HoldMarker, "test:marker", 0, 3, 0), s.Objectives[1] },
                Markers = new[] { new WorldMarker("test:marker", centerX, centerY, markerRadius, 100, true) }
            };
        });
        var run = new HeroTauntRuntimeTests.Run(fixture); run.Activate(); run.Step();
        Assert.Equal(completes ? 3 : 0, run.Session.CaptureWorldCombat().Objectives[0].Progress);
        if (completes) { Assert.Equal(WorldTerminalReason.ObjectivesCompleted, run.Session.GetWorldCombatResult().Terminal); }
        else { Assert.Null(run.Session.CaptureWorldCombat().Terminal); Assert.Single(Assert.Single(run.Frame.Taunts).Claims); }
    }
}
