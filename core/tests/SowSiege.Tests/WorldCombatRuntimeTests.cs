using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldCombatRuntimeTests
{
    internal static InteractiveOptions Options(ContentCatalog catalog, int hold = 3, int limit = 10)
    {
        var s = WorldCombatCodecTests.Options().Snapshot;
        var map = catalog.Tuning.World.Map;
        var x = map.Width / 2; var y = map.Height / 2;
        var effects = s.Effects.Select(e => e with { Raw = 0, Bonus = 0, Sources = e.Sources.Select(raw => raw with { SourceId = catalog.Tuning.DefaultHero, Raw = 0 }).ToArray() }).ToArray();
        var world = new WorldCombatOptions(s with
        {
            Provenance = s.Provenance with { ProfileHash = PortableStateCodec.HashCatalog(catalog), TickRate = catalog.Tuning.TickRate },
            Battlefield = new(0, 0, map.Width, map.Height), Hero = s.Hero with { Id = catalog.Tuning.DefaultHero },
            Objectives = new[] { s.Objectives[1] with { HoldTicks = hold }, s.Objectives[2] with { LimitTicks = limit } },
            Troops = Array.Empty<WorldTroopType>(), Cohorts = Array.Empty<WorldSpawnCohort>(),
            Markers = new[] { s.Markers[0] with { X = x, Y = y, Radius = 10 } }, Effects = effects
        });
        return new(new(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement,
            WorldCombatCodecTests.Hash('D'), WorldCombat: world);
    }
    private static ReplayCommand Advance(InteractiveSession session)
    {
        var command = new ReplayCommand(session.NextSequence, session.Simulation.World.Tick, ReplayCommandKind.Advance);
        session.Apply(command); return command;
    }
    private static InteractiveOptions Change(InteractiveOptions options, Func<WorldCombatSnapshot, WorldCombatSnapshot> change) => options with { WorldCombat = new(change(options.WorldCombat!.Snapshot)) };

    [Fact]
    public void InclusiveRadiusRequiresContinuousTicksAndPublishesPermanentTerminal()
    {
        var catalog = WaveEstateTests.Wave(); var session = new InteractiveSession(catalog, Options(catalog));
        var world = session.Simulation.World; var center = world.Lord;
        world.Lord = new(center.X + 10, center.Y); Advance(session);
        Assert.Equal(1, session.CaptureWorldCombat().Objectives[0].Progress);
        world.Lord = new(center.X + 11, center.Y); Advance(session);
        Assert.Equal(0, session.CaptureWorldCombat().Objectives[0].Progress);
        world.Lord = center; Advance(session); Advance(session); Advance(session);
        var result = session.GetWorldCombatResult(); Assert.Equal(WorldTerminalReason.ObjectivesCompleted, result.Terminal);
        Assert.Throws<InvalidOperationException>(() => session.GetSummary());
        Assert.Equal(5, result.TickOrRound); Assert.Empty(result.Troops);
        session.QuitWorldCombat(); Assert.Equal(result.StateHash, session.GetWorldCombatResult().StateHash);
        Assert.Throws<InvalidOperationException>(() => Advance(session));
    }

    [Theory]
    [InlineData(false, false, WorldTerminalReason.ObjectivesCompleted)]
    [InlineData(true, false, WorldTerminalReason.HeroDefeated)]
    [InlineData(false, true, WorldTerminalReason.RequiredMarkerDestroyed)]
    [InlineData(true, true, WorldTerminalReason.HeroDefeated)]
    public void FailurePrecedesSuccessAndSuccessPrecedesTimeout(bool heroDead, bool markerDead, WorldTerminalReason expected)
    {
        var catalog = WaveEstateTests.Wave(); var session = new InteractiveSession(catalog, Options(catalog, 1, 1));
        if (heroDead) { session.Simulation.World.LordHealth = 0; }
        if (markerDead) { session.State.WorldCombat!.Markers[0].Health = 0; }
        Advance(session); Assert.Equal(expected, session.GetWorldCombatResult().Terminal);
        Assert.Equal(1, session.GetWorldCombatResult().TickOrRound);
    }

    [Fact]
    public void TimeoutOccursAfterFinalAllowedAdvanceAndNoLaterInputCanMutateState()
    {
        var catalog = WaveEstateTests.Wave(); var session = new InteractiveSession(catalog, Options(catalog, 3, 3));
        session.Simulation.World.Lord = new(0, 0);
        Advance(session); Advance(session); Assert.Null(session.CaptureWorldCombat().Terminal);
        Advance(session); Assert.Equal(WorldTerminalReason.Timeout, session.GetWorldCombatResult().Terminal);
        var hash = session.ComputeStateHash(); Assert.Throws<InvalidOperationException>(() => Advance(session)); Assert.Equal(hash, session.ComputeStateHash());
    }

    [Fact]
    public void CohortMembershipCountsActualKillsOnceAndStandingSlotsAttackInsideSimulation()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog);
        var enemy = catalog.WaveRuntime!.Enemies.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
        options = Change(options, s => s with
        {
            Objectives = new[] { new WorldObjective("test:defeat", WorldObjectiveKind.DefeatDesignated, "test:cohort", 1, 0, 0), s.Objectives[1] },
            Troops = new[] { new WorldTroopType("test:troop", 1, new(20, 100, 0, 0, 100, 1), new[] { new WorldTroopSlot(0, 100, 100) }) },
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, 1, 100, 100, new(10, 0, 0, 0, 0, 1)) }
        });
        var session = new InteractiveSession(catalog, options); Advance(session);
        var result = session.GetWorldCombatResult(); Assert.Equal(WorldTerminalReason.ObjectivesCompleted, result.Terminal);
        Assert.Equal(1, result.Objectives[0].Progress); Assert.Equal(new WorldTroopResult("test:troop", 1, 0), Assert.Single(result.Troops));
        Assert.Equal(1, session.Simulation.World.SpawnedEnemies);
        var battle = session.State.WorldCombat!; var encoded = battle.Encode();
        var killed = session.Simulation.World.WaveRuntime!.Events.Single(e => e.Kind == "enemy-killed").SubjectId;
        battle.OnEnemyDefeated(killed); battle.OnEnemyDefeated(int.MaxValue);
        Assert.Equal(encoded, battle.Encode());
    }

    [Fact]
    public void EnemyDamageDisablesAStandingSlotOnceAndQuitPreservesOtherSurvivors()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog, 10, 10);
        var enemy = catalog.WaveRuntime!.Enemies.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
        options = Change(options, s => s with
        {
            Troops = new[] { new WorldTroopType("test:troop", 2, new(10, 0, 0, 0, 0, 1), new[] { new WorldTroopSlot(0, 100, 100), new WorldTroopSlot(1, 200, 200) }) },
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, 1, 100, 100, new(100, 10, 0, 0, 0, 1)) }
        });
        var session = new InteractiveSession(catalog, options); Advance(session);
        var battle = session.State.WorldCombat!; Assert.True(session.CaptureWorldCombat().Slots[0].Disabled);
        battle.DamageSlot(0, 100); battle.Effects.RegisterGroup(int.MaxValue, 100);
        Assert.Equal(0, session.CaptureWorldCombat().Slots[0].Health);
        session.QuitWorldCombat(); Assert.Equal(new WorldTroopResult("test:troop", 2, 1), Assert.Single(session.GetWorldCombatResult().Troops));
    }

    [Fact]
    public void ActualEnemyAttackDestroysRequiredMarkerAndWinsOverHoldCompletion()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog, 1, 1);
        var enemy = catalog.WaveRuntime!.Enemies.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
        options = Change(options, s => s with
        {
            Markers = new[] { s.Markers[0] with { X = s.Markers[0].X + 10, Radius = 10, Health = 1 } },
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, 1, s.Markers[0].X + 10, s.Markers[0].Y, new(100, 1, 0, 0, 0, 1)) }
        });
        var session = new InteractiveSession(catalog, options); Advance(session);
        Assert.Equal(0, Assert.Single(session.CaptureWorldCombat().Markers).Health);
        Assert.Equal(WorldTerminalReason.RequiredMarkerDestroyed, session.GetWorldCombatResult().Terminal);
    }

    [Fact]
    public void RecoveryReexecutesPrefixAndRejectsEditedRuntimeOrMissingCommands()
    {
        var catalog = WaveEstateTests.Wave(); var session = new InteractiveSession(catalog, Options(catalog));
        var commands = new[] { Advance(session) }; var bytes = WorldRecoveryCodec.Capture(session, commands);
        var restored = WorldRecoveryCodec.Restore(catalog, session.Options.DataHash, bytes);
        Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
        Advance(session); Advance(restored); Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
        var document = WorldRecoveryCodec.Decode(bytes); var tampered = document.WorldRuntimeBytes.ToArray(); tampered[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Restore(catalog, session.Options.DataHash, WorldRecoveryCodec.Encode(document with { WorldRuntimeBytes = tampered })));
        Assert.ThrowsAny<Exception>(() => WorldRecoveryCodec.Capture(session, Array.Empty<ReplayCommand>()));
    }

    [Fact]
    public void WorldEndIsRecomputedAndFalseResultIsRejected()
    {
        var catalog = WaveEstateTests.Wave(); var session = new InteractiveSession(catalog, Options(catalog, 2, 3));
        var commands = new[] { Advance(session), Advance(session) }; var result = session.GetWorldCombatResult();
        var replay = new WorldReplayDocument(ReplayCodec.Header(session.Options), commands, Array.Empty<ReplayCheckpoint>(), new(session.NextSequence, result));
        Assert.Equal(result.StateHash, WorldReplayRunner.Verify(catalog, session.Options.DataHash, replay).StateHash);
        Assert.Throws<InvalidDataException>(() => WorldReplayRunner.Verify(catalog, session.Options.DataHash, replay with { End = replay.End with { Result = result with { StateHash = WorldCombatCodecTests.Hash('F') } } }));
    }

    [Fact]
    public void EffectCapAndEligibilityAreAppliedOnceAndFractionalProgressIsHashed()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog);
        options = Change(options, s => s with { Effects = s.Effects.Select(e => e with
        {
            Raw = 1500, Cap = 500, Bonus = 500, StandingEligible = false,
            Sources = e.Sources.Select(raw => raw with { Raw = 1500 }).ToArray()
        }).ToArray() });
        var session = new InteractiveSession(catalog, options); var effects = session.State.WorldCombat!.Effects;
        Assert.Equal(15, effects.Scale(10, MetaAbility.Attack));
        Assert.Equal(10, effects.Scale(10, MetaAbility.Attack, WorldEffectTarget.Standing));
        var initial = session.ComputeStateHash(); Assert.Equal(1, effects.AwardExperience("enemy", 1));
        Assert.NotEqual(initial, session.ComputeStateHash()); Assert.Equal(2, effects.AwardExperience("enemy", 1));
    }
    [Fact]
    public void CombinedVassalEffectReplacesLegacyApplicationAndRecoveryPreservesBothStates()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog);
        var vassals = VassalRunTests.Options(MetaAbility.Health, MetaAbility.Growth, MetaAbility.Experience);
        var world = WorldCombatCodecTests.WithVassals(options.WorldCombat!, vassals);
        options = options with { WorldCombat = world, Vassals = vassals };
        var session = new InteractiveSession(catalog, options);
        var expected = checked((int)((long)catalog.Tuning.World.Map.LordHealth * (PlayerInput.Scale + world.Snapshot.Effects[(int)MetaAbility.Health].Bonus) / PlayerInput.Scale));
        Assert.Equal(expected, session.View.CaptureFrame().Lord.Health);
        var vassalBytes = session.State.Vassals!.Encode();
        var commands = new[] { Advance(session) }; var bytes = WorldRecoveryCodec.Capture(session, commands);
        Assert.Equal(vassalBytes, session.State.Vassals.Encode());
        var restored = WorldRecoveryCodec.Restore(catalog, options.DataHash, bytes);
        Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void QuitVerifiesCommandFrontierCheckpointBeforeTerminalAndCannotBeResumed(int count)
    {
        var catalog = WaveEstateTests.Wave(); var session = new InteractiveSession(catalog, Options(catalog));
        var commands = new List<ReplayCommand>();
        for (var i = 0; i < count; i++) { commands.Add(Advance(session)); }
        var checkpoint = new ReplayCheckpoint(session.NextSequence, session.Simulation.World.Tick, session.ComputeStateHash());
        session.QuitWorldCombat(); var result = session.GetWorldCombatResult();
        var replay = new WorldReplayDocument(ReplayCodec.Header(session.Options), commands,
            new[] { checkpoint }, new(session.NextSequence, result));
        Assert.Equal(result.StateHash, WorldReplayRunner.Verify(catalog, session.Options.DataHash, replay).StateHash);
        Assert.Throws<InvalidDataException>(() => WorldReplayRunner.Verify(catalog, session.Options.DataHash,
            replay with { Checkpoints = new[] { checkpoint with { StateHash = result.StateHash } } }));
        Assert.Throws<InvalidOperationException>(() => WorldRecoveryCodec.Capture(session, commands));
    }

    [Fact]
    public void ScheduledLivingCohortInterruptsAnAlreadyStartedHold()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog, 4, 8);
        var enemy = catalog.WaveRuntime!.Enemies.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
        options = Change(options, s => s with { Cohorts = new[]
        {
            new WorldSpawnCohort("test:cohort", enemy, 2, 1, s.Markers[0].X, s.Markers[0].Y, new(100, 0, int.MaxValue, 0, 0, 1))
        } });
        var session = new InteractiveSession(catalog, options); Advance(session); Advance(session);
        Assert.Equal(2, session.CaptureWorldCombat().Objectives[0].Progress); Assert.Equal(0, session.Simulation.World.SpawnedEnemies);
        Advance(session); Assert.Equal(0, session.CaptureWorldCombat().Objectives[0].Progress);
        Assert.Equal(1, session.Simulation.World.SpawnedEnemies); Advance(session); Assert.Equal(1, session.Simulation.World.SpawnedEnemies);
    }

    [Fact]
    public void WorldCatalogAndRuntimeSnapshotsRejectSubstitutionAndIgnoreCallerMutation()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog);
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog, Change(options, s => s with
        { Provenance = s.Provenance with { ProfileHash = WorldCombatCodecTests.Hash('F') } })));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog, Change(options, s => s with
        { Provenance = s.Provenance with { ContentHash = WorldCombatCodecTests.Hash('F') } })));
        var session = new InteractiveSession(catalog, options); var original = session.ComputeStateHash();
        ((WorldMarker[])options.WorldCombat!.Snapshot.Markers)[0] = options.WorldCombat.Snapshot.Markers[0] with { Health = int.MaxValue };
        Assert.Equal(original, session.ComputeStateHash());
    }

    [Fact]
    public void CohortMovementConsumesExistingWetAndStopStatusWithoutChangingSnapshotSpeed()
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog, 10, 10);
        var enemyId = catalog.WaveRuntime!.Enemies.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
        options = Change(options, s => s with { Cohorts = new[]
        { new WorldSpawnCohort("test:cohort", enemyId, 0, 1, 100, 100, new(100, 0, int.MaxValue, 100, 0, 1)) } });
        var session = new InteractiveSession(catalog, options); Advance(session);
        var enemy = Assert.Single(session.Simulation.World.Enemies); var action = session.Simulation.World.WaveRuntime!.EnemyActions[enemy.Id];
        var before = enemy.Position; action.WetUntil = 10; Advance(session);
        Assert.Equal(before.X + Math.Max(1, 100 / catalog.WaveRuntime.Behavior.WetSpeedDivisor), enemy.Position.X);
        before = enemy.Position; action.StopUntil = session.Simulation.World.Tick + 1; Advance(session);
        Assert.Equal(before, enemy.Position); Advance(session); Assert.NotEqual(before, enemy.Position);
        Assert.Equal(100, session.Options.WorldCombat!.Snapshot.Cohorts[0].Stats.Speed);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3, 7)]
    [InlineData(10, 0)]
    public void HeroAndStandingAttacksUseTheSameIssuedEnemyDefense(int defense, int eachDamage)
    {
        var (catalog, _, world, originalEnemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var snapshot = Options(catalog).WorldCombat!.Snapshot;
        var battle = new WorldCombatState(new(snapshot with
        {
            Battlefield = new(0, 0, 1000, 1000), Markers = new[] { snapshot.Markers[0] with { X = 500, Y = 500 } },
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", originalEnemy.Definition, 0, 1, 600, 500, new(100, 0, defense, 0, 0, 1)) },
            Troops = new[] { new WorldTroopType("test:troop", 1, new(10, 10, 0, 0, 100, 1), new[] { new WorldTroopSlot(0, 600, 500) }) }
        }));
        world.Enemies.Clear();
        var system = new WaveRuntimeSystem(catalog, world, new(1, true), new() { WorldCombat = battle });
        system.Tick(world.Lord);
        Assert.Equal(100 - eachDamage * 2, Assert.Single(world.Enemies).Health);
        Assert.Equal(eachDamage, world.WeaponDamage); Assert.Equal(eachDamage, world.AllyDamage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownIssuedHeroOrEstateIsRejectedBeforeSimulationConstruction(bool hero)
    {
        var catalog = WaveEstateTests.Wave(); var options = Options(catalog);
        if (hero)
        {
            options = Change(options, s => s with
            {
                Hero = s.Hero with { Id = "test:missing" }, Effects = s.Effects.Select(e => e with
                { Sources = e.Sources.Select(source => source with { SourceId = "test:missing" }).ToArray() }).ToArray()
            });
            options = options with { Run = options.Run with { HeroId = "test:missing" } };
        }
        else { options = options with { Run = options.Run with { EstateId = "test:missing" } }; }
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog, options));
    }

}
