using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroZoneIntegrationTests
{
    private sealed class Run
    {
        internal InteractiveSession Session { get; }
        internal List<ReplayCommand> Commands { get; } = new();
        internal HeroZoneFrame Frame => Session.CaptureHeroZones();
        internal Run(HeroZoneStateTests.Scenario fixture) { Session = new(fixture.Catalog, fixture.Options); }
        internal void Step(short x = 0, short y = 0)
        {
            var command = new ReplayCommand(Session.NextSequence, Session.Simulation.World.Tick, ReplayCommandKind.Advance, new(x, y));
            Session.Apply(command); Commands.Add(command);
        }
        internal WorldReplayDocument Finish()
        {
            using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(Session.Options));
            foreach (var command in Commands) { ReplayCodec.WriteCommand(bytes, command); }
            ReplayCodec.WriteWorldEnd(bytes, new(Session.NextSequence, Session.GetWorldCombatResult()));
            bytes.Position = 0; return ReplayCodec.ReadWorld(bytes);
        }
    }

    [Fact]
    public void ActualEnemyMovementUsesOriginalSpeedAndOpensTellAtExactTick()
    {
        var f = HeroZoneStateTests.Fixture(); var run = new Run(f); var origin = run.Session.Simulation.World.Lord;
        run.Step(); Assert.Empty(run.Frame.Zones);
        Assert.Equal(origin.X + 40, Assert.Single(run.Session.Simulation.World.Enemies).Position.X);
        run.Step(); var tell = Assert.Single(run.Frame.Zones); Assert.False(tell.Active);
        Assert.Equal(new WorldPoint(origin.X + 40, origin.Y), tell.Center);
        run.Step(); Assert.False(Assert.Single(run.Frame.Zones).Active);
        Assert.Equal(origin.X + 20, Assert.Single(run.Session.Simulation.World.Enemies).Position.X);
        run.Step(); Assert.True(Assert.Single(run.Frame.Zones).Active);
        Assert.Equal(origin.X + 13, Assert.Single(run.Session.Simulation.World.Enemies).Position.X);
        Assert.Single(run.Frame.Sources);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void TellAndActiveFrontiersRestoreExactStateAndContinueIdentically(int ticks)
    {
        var f = HeroZoneStateTests.Fixture(); var run = new Run(f);
        for (var tick = 0; tick < ticks; tick++) { run.Step(); }
        var header = WorldCombatApi.CreateContract(f.Catalog, f.Options);
        var accepted = WorldCombatApi.AcceptPrefix(f.Catalog, f.Options.DataHash, header, null, run.Commands);
        var restored = WorldRecoveryCodec.Restore(f.Catalog, f.Options.DataHash, accepted.Recovery);
        Assert.Equal(WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(run.Session), WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(restored));
        Assert.Equal(run.Session.State.HeroActive!.Encode(), restored.State.HeroActive!.Encode());
        Assert.Equal(accepted.Recovery, WorldRecoveryCodec.Capture(restored, run.Commands));
        run.Step(); restored.Apply(run.Commands[^1]);
        Assert.Equal(run.Session.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void NoInputFrontierActivatesZoneAndRejectsReplacementOfAcceptedHistory()
    {
        var f = HeroZoneStateTests.Fixture(); var header = WorldCombatApi.CreateContract(f.Catalog, f.Options);
        var accepted = WorldCombatApi.AdvanceWithoutInput(f.Catalog, f.Options.DataHash, header, null, 4);
        var restored = WorldRecoveryCodec.Restore(f.Catalog, f.Options.DataHash, accepted.Recovery);
        Assert.True(Assert.Single(restored.CaptureHeroZones().Zones).Active);
        Assert.Single(restored.CaptureHeroZones().Sources);
        Assert.Equal("frontier-conflict", Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.AcceptPrefix(
            f.Catalog, f.Options.DataHash, header, accepted.Recovery,
            new[] { new ReplayCommand(1, 1, ReplayCommandKind.Advance, new(1000, 0)) })).Code);
    }

    [Fact]
    public void RuntimeTamperingAndIssuedDefinitionReplacementFailClosed()
    {
        var f = HeroZoneStateTests.Fixture(); var run = new Run(f);
        for (var tick = 0; tick < 4; tick++) { run.Step(); }
        var candidate = WorldRecoveryCodec.Decode(WorldRecoveryCodec.Capture(run.Session, run.Commands));
        var runtime = (byte[])candidate.HeroActiveRuntimeBytes!.Clone(); runtime[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Restore(f.Catalog, f.Options.DataHash,
            WorldRecoveryCodec.Encode(candidate with { HeroActiveRuntimeBytes = runtime })));
        var changed = HeroZoneStateTests.Change(f.Options.HeroActive!, 0, p => p with { SlowPermille = 301 });
        Assert.Throws<ArgumentException>(() => new InteractiveSession(f.Catalog, f.Options with { HeroActive = changed }));
    }

    [Fact]
    public void TerminalVerificationKeepsAcceptedZoneHistoryAndClearsRuntime()
    {
        var f = HeroZoneStateTests.Fixture(configureWorld: s => s with
        { Objectives = new[] { s.Objectives[0], s.Objectives[1] with { LimitTicks = 6 } } });
        var run = new Run(f); var header = WorldCombatApi.CreateContract(f.Catalog, f.Options);
        for (var tick = 0; tick < 4; tick++) { run.Step(); }
        var accepted = WorldCombatApi.AcceptPrefix(f.Catalog, f.Options.DataHash, header, null, run.Commands);
        run.Step(); run.Step(); Assert.Empty(run.Frame.Zones); Assert.Empty(run.Frame.Sources);
        var result = WorldCombatApi.VerifyResult(f.Catalog, f.Options.DataHash, header, accepted.Recovery, run.Finish());
        Assert.Equal(WorldTerminalReason.Timeout, result.Terminal);
        var alternate = new Run(f);
        for (var tick = 0; tick < 6; tick++) { alternate.Step(1000); }
        var other = alternate.Finish();
        Assert.Equal(alternate.Session.ComputeStateHash(), WorldReplayRunner.Verify(f.Catalog, f.Options.DataHash, other).StateHash);
        Assert.NotEqual(result.StateHash, other.End.Result.StateHash);
        Assert.Equal("frontier-conflict", Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.VerifyResult(
            f.Catalog, f.Options.DataHash, header, accepted.Recovery, other)).Code);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(100, false)]
    public void HoldCountsPhysicalEnemiesRatherThanZonesOrSlowSources(int radius, bool completes)
    {
        var f = HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0, p => p with { TellTicks = 0 }),
            configureWorld: s => s with
            {
                Objectives = new[] { new WorldObjective("test:hold", WorldObjectiveKind.HoldMarker, "test:marker", 0, 3, 0), s.Objectives[1] },
                Markers = new[] { new WorldMarker("test:marker", s.Battlefield.MaxX / 2, s.Battlefield.MaxY / 2, radius, 100, true) },
                Cohorts = new[] { s.Cohorts[0] with { Stats = s.Cohorts[0].Stats with { Speed = 1 } } }
            });
        var run = new Run(f); run.Step(); run.Step(); Assert.Single(run.Frame.Sources); run.Step();
        Assert.Equal(completes ? 3 : 0, run.Session.CaptureWorldCombat().Objectives[0].Progress);
        if (completes)
        { Assert.Equal(WorldTerminalReason.ObjectivesCompleted, run.Session.GetWorldCombatResult().Terminal); Assert.Empty(run.Frame.Zones); }
        else { Assert.Null(run.Session.CaptureWorldCombat().Terminal); Assert.Single(run.Frame.Sources); }
    }

    [Fact]
    public void LethalEncounterAttackClearsActiveZonesInTheSameTick()
    {
        var f = HeroZoneStateTests.Fixture(configureWorld: s => s with
        {
            Cohorts = new[]
            {
                s.Cohorts[0],
                s.Cohorts[0] with
                {
                    Id = "test:lethal", SpawnTick = 4,
                    X = s.Battlefield.MaxX / 2, Y = s.Battlefield.MaxY / 2,
                    Stats = s.Cohorts[0].Stats with { Attack = 1000000, Range = 100 }
                }
            }
        });
        var run = new Run(f);
        for (var tick = 0; tick < 4; tick++) { run.Step(); }
        Assert.True(Assert.Single(run.Frame.Zones).Active);
        run.Step(); Assert.Equal(0, run.Session.Simulation.World.LordHealth);
        Assert.NotNull(run.Session.CaptureWorldCombat().Terminal);
        Assert.Empty(run.Frame.Zones); Assert.Empty(run.Frame.Sources);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void QuitClearsBothPendingTellAndActiveZone(int ticks)
    {
        var run = new Run(HeroZoneStateTests.Fixture());
        for (var tick = 0; tick < ticks; tick++) { run.Step(); }
        Assert.Single(run.Frame.Zones); run.Session.QuitWorldCombat();
        Assert.Empty(run.Frame.Zones); Assert.Empty(run.Frame.Sources);
        Assert.Equal(WorldTerminalReason.Quit, run.Session.GetWorldCombatResult().Terminal);
        Assert.Throws<InvalidOperationException>(() => WorldRecoveryCodec.Capture(run.Session, run.Commands));
    }
}
