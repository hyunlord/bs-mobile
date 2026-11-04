using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroDashRuntimeTests
{
    internal static (ContentCatalog Catalog, InteractiveOptions Options) Fixture(
        int enemyX = 1000, int enemyY = 0, int damage = 0, int range = 0,
        Func<HeroActiveOptions, HeroActiveOptions>? configure = null,
        Func<WorldCombatSnapshot, WorldCombatSnapshot>? worldConfigure = null)
    {
        var catalog = WaveEstateTests.Wave();
        catalog = catalog with { Tuning = catalog.Tuning with { World = catalog.Tuning.World with { Map = catalog.Tuning.World.Map with { LordSpeed = 10 } } } };
        var enemy = catalog.WaveRuntime!.Enemies.Keys.OrderBy(x => x, StringComparer.Ordinal).First();
        var active = HeroActiveCodecTests.Active(catalog.Tuning.DefaultHero, catalog.Tuning.World.Progression.StartingWeapon, enemy);
        if (configure is not null) { active = configure(active); }
        catalog = catalog with { HeroActive = active };
        var options = WorldCombatRuntimeTests.Options(catalog, 100, 100);
        var s = options.WorldCombat!.Snapshot;
        var center = new Position(catalog.Tuning.World.Map.Width / 2, catalog.Tuning.World.Map.Height / 2);
        s = s with
        {
            Hero = s.Hero with { Skills = active.Skills.Select(x => new WorldHeroSkill(x.Id, x.Version, x.ContentHash)).ToArray() },
            Objectives = new[] { new WorldObjective("test:defeat", WorldObjectiveKind.DefeatDesignated, "test:cohort", 1, 0, 0), s.Objectives[1] },
            Markers = Array.Empty<WorldMarker>(),
            Cohorts = new[] { new WorldSpawnCohort("test:cohort", enemy, 0, 1, center.X + enemyX, center.Y + enemyY, new(10000, damage, 10000, 0, range, 10)) }
        };
        if (worldConfigure is not null) { s = worldConfigure(s); }
        return (catalog, options with { WorldCombat = new(s), HeroActive = active });
    }

    private sealed class Run
    {
        internal InteractiveSession Session { get; }
        internal List<ReplayCommand> Commands { get; } = new();
        internal WorldState World => Session.Simulation.World;
        internal HeroActiveFrame Frame => Session.CaptureHeroActive();
        internal Position Center { get; }
        internal Run((ContentCatalog Catalog, InteractiveOptions Options) fixture)
        {
            Session = new(fixture.Catalog, fixture.Options); Center = World.Lord;
        }
        internal void Step(short x = 1000, short y = 0)
        {
            var command = new ReplayCommand(Session.NextSequence, World.Tick, ReplayCommandKind.Advance, new(x, y));
            Session.Apply(command); Commands.Add(command);
        }
        internal void Prepare(short x = 1000, short y = 0)
        {
            Step(x, y); Step(x, y);
            Assert.True(Frame.Active); Assert.Equal(1, Frame.ActivationSequence);
        }
    }

    [Fact]
    public void ThinWallStopsDashBeforeFarEndpointAndConsumesCooldown()
    {
        var baseCatalog = WaveEstateTests.Wave(); var center = baseCatalog.Tuning.World.Map;
        var wallX = center.Width / 2 + 70; var centerY = center.Height / 2;
        var run = new Run(Fixture(configure: a => a with { Walls = new[] { new HeroActiveWall("test:wall", wallX, centerY - 20, wallX + 1, centerY + 20) } }));
        run.Prepare(); var ready = run.Frame.CooldownReadyTick;
        run.Step(); Assert.Equal(wallX, run.World.Lord.X); Assert.False(run.Frame.Active);
        Assert.Equal(ready, run.Frame.CooldownReadyTick); Assert.Equal(50, run.Frame.Distance);
        run.Step(); Assert.Equal(wallX, run.World.Lord.X); Assert.Equal(0, run.Frame.PreparationTicks);
        run.Step(-1000, 0); Assert.Equal(wallX - 10, run.World.Lord.X);
    }

    [Fact]
    public void PartialWallStopResetsPreparationWithoutActivationOrCooldown()
    {
        var map = WaveEstateTests.Wave().Tuning.World.Map;
        var wallX = map.Width / 2 + 15; var y = map.Height / 2;
        var run = new Run(Fixture(configure: a => a with
        {
            Definitions = new[] { a.Definitions[0] with { Dash = a.Definitions[0].Dash! with { PreparationTicks = 2 } } },
            Walls = new[] { new HeroActiveWall("test:wall", wallX, y - 20, wallX + 1, y + 20) }
        }));
        run.Step(); Assert.Equal(run.Center.X + 10, run.World.Lord.X); Assert.Equal(1, run.Frame.PreparationTicks);
        run.Step(); Assert.Equal(wallX, run.World.Lord.X);
        Assert.Equal(0, run.Frame.PreparationTicks); Assert.False(run.Frame.Active);
        Assert.Equal(0, run.Frame.ActivationSequence); Assert.Equal(0, run.Frame.CooldownReadyTick);
        run.Step(-1000, 0); Assert.Equal(wallX - 10, run.World.Lord.X);
        Assert.Equal(1, run.Frame.PreparationTicks); Assert.False(run.Frame.Active);
        Assert.Equal(0, run.Frame.ActivationSequence); Assert.Equal(0, run.Frame.CooldownReadyTick);
    }

    [Fact]
    public void EnemyBodyStopsDashWithoutOutgoingDamage()
    {
        var run = new Run(Fixture(enemyX: 80)); run.Prepare();
        var enemy = Assert.Single(run.World.Enemies); var health = enemy.Health;
        run.Step(); Assert.Equal(run.Center.X + 79, run.World.Lord.X);
        Assert.False(run.Frame.Active); Assert.Equal(health, enemy.Health);
    }

    [Fact]
    public void MidsegmentEnemyAttackHitsWithoutDashInvulnerability()
    {
        var run = new Run(Fixture(enemyX: 80, enemyY: 10, damage: 7, range: 11)); run.Prepare();
        var health = run.World.LordHealth;
        run.Step(); Assert.Equal(run.Center.X + 120, run.World.Lord.X);
        Assert.Equal(health - 7, run.World.LordHealth);
        Assert.Single(run.World.WaveRuntime!.Events, e => e.Tick == 2 && e.Kind == "lord-hit");
    }

    [Fact]
    public void PathAndEndpointOverlapConsumeOneAttackAndCooldown()
    {
        var run = new Run(Fixture(enemyX: 120, enemyY: 10, damage: 7, range: 11)); run.Prepare();
        var health = run.World.LordHealth; run.Step();
        Assert.Equal(health - 7, run.World.LordHealth);
        Assert.Equal(12, Assert.Single(run.World.Enemies).AttackTick);
        Assert.Single(run.World.WaveRuntime!.Events, e => e.Tick == 2 && e.Kind == "lord-hit");
        run.Step(); Assert.Equal(health - 7, run.World.LordHealth);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloserTroopOrMarkerKeepsPriorityOverSweptHero(bool marker)
    {
        var run = new Run(Fixture(enemyX: 80, enemyY: 10, damage: 7, range: 11, worldConfigure: s =>
        {
            var enemy = s.Cohorts[0];
            return marker
                ? s with { Markers = new[] { new WorldMarker("test:target", enemy.X, enemy.Y, 1, 100, false) } }
                : s with { Troops = new[] { new WorldTroopType("test:troop", 1, new(100, 0, 0, 0, 0, 10), new[] { new WorldTroopSlot(0, enemy.X, enemy.Y) }) } };
        }));
        run.Prepare(); var health = run.World.LordHealth; run.Step();
        Assert.Equal(health, run.World.LordHealth);
        if (marker) { Assert.Equal(93, run.Session.State.WorldCombat!.Markers[0].Health); }
        else { Assert.Equal(93, run.Session.State.WorldCombat!.Slots[0].Health); }
    }

    [Fact]
    public void WallBeforeAttackRangePreventsHitBehindWall()
    {
        var map = WaveEstateTests.Wave().Tuning.World.Map;
        var wallX = map.Width / 2 + 40; var y = map.Height / 2;
        var run = new Run(Fixture(enemyX: 80, enemyY: 10, damage: 7, range: 11,
            configure: a => a with { Walls = new[] { new HeroActiveWall("test:wall", wallX, y - 20, wallX + 1, y + 20) } }));
        run.Prepare(); var health = run.World.LordHealth; run.Step();
        Assert.Equal(wallX, run.World.Lord.X); Assert.Equal(health, run.World.LordHealth);
    }

    [Fact]
    public void InputReleaseEndsBeforeMotionAndDoesNotRefundCooldown()
    {
        var run = new Run(Fixture()); run.Prepare(); var position = run.World.Lord; var ready = run.Frame.CooldownReadyTick;
        run.Step(0, 0); Assert.Equal(position, run.World.Lord); Assert.False(run.Frame.Active);
        Assert.Equal(ready, run.Frame.CooldownReadyTick); Assert.Equal(0, run.Frame.PreparationTicks);
        run.Step(); Assert.Equal(position.X + 10, run.World.Lord.X); Assert.Equal(1, run.Frame.ActivationSequence);
    }

    [Fact]
    public void FatalPathAttackClearsActiveState()
    {
        var run = new Run(Fixture(enemyX: 80, enemyY: 10, damage: 100000, range: 11)); run.Prepare(); run.Step();
        Assert.Equal(0, run.World.LordHealth); Assert.False(run.Frame.Active);
        Assert.Equal(WorldTerminalReason.HeroDefeated, run.Session.GetWorldCombatResult().Terminal);
    }

    [Fact]
    public void RemainingDistanceClipsFinalTickAndExpiryDoesNotCatchUpAttacks()
    {
        var run = new Run(Fixture(configure: a => a with { Definitions = new[] { a.Definitions[0] with { Dash = a.Definitions[0].Dash! with { MaximumDistance = 250 } } } }));
        run.Prepare(); run.Step(); run.Step(); run.Step();
        Assert.Equal(run.Center.X + 270, run.World.Lord.X); Assert.Equal(250, run.Frame.Distance); Assert.False(run.Frame.Active);
        run.Step(); Assert.Equal(run.Center.X + 280, run.World.Lord.X); Assert.Equal(1, run.Frame.ActivationSequence);
    }

    [Fact]
    public void FixedDirectionCarriesAxisRemainderAcrossTicksAndRecovery()
    {
        var run = new Run(Fixture(configure: a => a with { Definitions = new[] { a.Definitions[0] with { Dash = a.Definitions[0].Dash! with { SpeedPerTick = 101, MaximumDistance = 400, DurationTicks = 4 } } } }));
        run.Prepare(1000, 333); var origin = run.World.Lord; run.Step(1000, -1000);
        Assert.Equal(new Position(origin.X + 101, origin.Y + 33), run.World.Lord); Assert.Equal(633, run.Frame.RemainderY);
        var bytes = WorldRecoveryCodec.Capture(run.Session, run.Commands);
        var restored = WorldRecoveryCodec.Restore(run.Session.Catalog, run.Session.Options.DataHash, bytes);
        Assert.Equal(run.Frame, restored.CaptureHeroActive());
        run.Step(1000, -1000); restored.Apply(run.Commands[^1]);
        Assert.Equal(new Position(origin.X + 202, origin.Y + 67), run.World.Lord);
        Assert.Equal(run.Session.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(run.Session), WorldCombatAbsentStateCompatibilityTests.CaptureHistoricalState(restored));
    }

    [Fact]
    public void NoInputFrontierReexecutesReleaseAndCannotRewriteAcceptedDash()
    {
        var run = new Run(Fixture()); run.Prepare(); var options = run.Session.Options;
        var header = WorldCombatApi.CreateContract(run.Session.Catalog, options);
        var prefix = WorldCombatApi.AcceptPrefix(run.Session.Catalog, options.DataHash, header, null, run.Commands);
        var advanced = WorldCombatApi.AdvanceWithoutInput(run.Session.Catalog, options.DataHash, header, prefix.Recovery, 3);
        var restored = WorldRecoveryCodec.Restore(run.Session.Catalog, options.DataHash, advanced.Recovery);
        Assert.False(restored.CaptureHeroActive().Active); Assert.Equal(run.World.Lord, restored.Simulation.World.Lord);
        var error = Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.AcceptPrefix(run.Session.Catalog, options.DataHash, header,
            advanced.Recovery, new[] { new ReplayCommand(2, 2, ReplayCommandKind.Advance, new(1000, 0)) }));
        Assert.Equal("frontier-conflict", error.Code);
    }

    [Fact]
    public void ReencodedRuntimeTamperingFailsPrefixReexecution()
    {
        var run = new Run(Fixture()); run.Prepare(); run.Step();
        var recovery = WorldRecoveryCodec.Capture(run.Session, run.Commands); var candidate = WorldRecoveryCodec.Decode(recovery);
        var active = (byte[])candidate.HeroActiveRuntimeBytes!.Clone(); active[^1] ^= 1;
        var changed = WorldRecoveryCodec.Encode(candidate with { HeroActiveRuntimeBytes = active });
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Restore(run.Session.Catalog, run.Session.Options.DataHash, changed));
    }

    [Fact]
    public void StartingWeaponClockAdvancesWhileOnlyItsNewActivationIsSuppressed()
    {
        var run = new Run(Fixture()); run.Prepare();
        var gear = Assert.Single(run.World.Equipment, x => x.Id == run.Session.Options.HeroActive!.AttackDefinitionId);
        gear.ReadyTick = run.World.Tick; var before = run.World.WaveRuntime!.Events.Count;
        run.Step(); Assert.True(gear.ReadyTick > 2);
        Assert.DoesNotContain(run.World.WaveRuntime.Events.Skip(before), e => e.Source == gear.Id && e.Kind == "attack");
        Assert.True(run.Frame.Active);
    }

    [Fact]
    public void EvolvedStartingSlotIsSuppressedAndResumesOneScheduledAttack()
    {
        var run = new Run(Fixture());
        var recipe = Assert.Single(run.Session.Catalog.WaveRuntime!.Evolutions.Values, e => e.Kind == WaveEvolutionKind.PlantingArc);
        Assert.Equal(run.Session.Options.HeroActive!.StartingSlotId, recipe.InputIds[0]);
        run.World.Equipment.Clear();
        foreach (var id in recipe.InputIds) { run.World.Equipment.Add(new() { Id = id }); }
        run.World.WaveRuntime!.Evolutions.Add(recipe.Id);
        run.Prepare();
        var weapon = run.World.Equipment[0]; weapon.ReadyTick = run.World.Tick;
        run.Step(); Assert.True(weapon.ReadyTick > 2);
        Assert.DoesNotContain(run.World.WaveRuntime.Events, e => e.Tick == 2 && e.Source == recipe.Id && e.Kind == "attack");
        run.Step(0, 0); Assert.False(run.Frame.Active);
        Assert.DoesNotContain(run.World.WaveRuntime.Events, e => e.Tick == 3 && e.Source == recipe.Id && e.Kind == "attack");
        weapon.ReadyTick = run.World.Tick;
        run.Step(0, 0);
        Assert.Single(run.World.WaveRuntime.Events, e => e.Tick == 4 && e.Source == recipe.Id && e.Kind == "attack");
    }

    private static (ContentCatalog Catalog, InteractiveOptions Options) Rebind(
        (ContentCatalog Catalog, InteractiveOptions Options) fixture, ContentCatalog catalog)
    {
        var snapshot = fixture.Options.WorldCombat!.Snapshot;
        return (catalog, fixture.Options with
        {
            WorldCombat = new(snapshot with
            { Provenance = snapshot.Provenance with { ProfileHash = PortableStateCodec.HashCatalog(catalog) } })
        });
    }

    [Fact]
    public void OtherSlotLaunchesAndExistingProjectileMovesDuringDash()
    {
        var fixture = Fixture(enemyX: 600, enemyY: 100); var wave = fixture.Catalog.WaveRuntime!;
        const string other = "core:ember_wand";
        var gear = wave.Gear.ToDictionary(x => x.Key, x => x.Value);
        gear[other] = gear[other] with
        {
            Range = 2000,
            Speed = 1,
            Count = 1,
            Levels = gear[other].Levels!.Select(x => x with { Range = 2000, Speed = 1, Count = 1 }).ToArray()
        };
        var run = new Run(Rebind(fixture, fixture.Catalog with { WaveRuntime = wave with { Gear = gear } }));
        run.Prepare(); run.World.Equipment.Add(new() { Id = other, ReadyTick = run.World.Tick });
        run.Step(); Assert.True(run.Frame.Active);
        Assert.Single(run.World.WaveRuntime!.Events, e => e.Tick == 2 && e.Source == other && e.Kind == "projectile-launched");
        var projectile = Assert.Single(run.World.WaveRuntime.Projectiles, p => p.Source == other);
        var position = projectile.Position; run.Step();
        Assert.Contains(projectile, run.World.WaveRuntime.Projectiles); Assert.NotEqual(position, projectile.Position);
        Assert.True(run.Frame.Active);
    }

    [Fact]
    public void BothInputEvolutionBranchAlsoSuppressesStartingSlotLineage()
    {
        var fixture = Fixture(); var wave = fixture.Catalog.WaveRuntime!;
        var recipe = Assert.Single(wave.Evolutions.Values, e => e.Kind == WaveEvolutionKind.ShelteredPlot);
        var slot = fixture.Options.HeroActive!.StartingSlotId;
        var seed = recipe.InputIds[0];
        var programs = wave.Programs!.ToDictionary(x => x.Key, x => x.Value);
        var slotUnits = programs[slot].Params.ToDictionary(x => x.Key, x => x.Value);
        slotUnits["unit:remnant-create"] = programs[seed].Params["unit:remnant-create"];
        programs[slot] = new(slotUnits);
        var gear = wave.Gear.ToDictionary(x => x.Key, x => x.Value);
        gear[slot] = gear[slot] with
        {
            WorkTicks = gear[seed].WorkTicks,
            WorkRadius = gear[seed].WorkRadius,
            Capacity = gear[seed].Capacity,
            RewardExperience = gear[seed].RewardExperience
        };
        recipe = recipe with { InputIds = new[] { slot, recipe.InputIds[1] } };
        var evolutions = wave.Evolutions.ToDictionary(x => x.Key, x => x.Value); evolutions[recipe.Id] = recipe;
        var run = new Run(Rebind(fixture, fixture.Catalog with { WaveRuntime = wave with { Evolutions = evolutions, Programs = programs, Gear = gear } }));
        run.World.Equipment.Clear(); foreach (var id in recipe.InputIds) { run.World.Equipment.Add(new() { Id = id }); }
        run.World.WaveRuntime!.Evolutions.Add(recipe.Id); run.Prepare();
        var first = run.World.Equipment[0]; first.ReadyTick = run.World.Tick;
        run.Step(); Assert.True(first.ReadyTick > 2);
        Assert.DoesNotContain(run.World.WaveRuntime.Events, e => e.Tick == 2 && e.Source == recipe.Id && e.Kind == "attack");
        run.Step(0, 0); first.ReadyTick = run.World.Tick; run.Step(0, 0);
        Assert.Single(run.World.WaveRuntime.Events, e => e.Tick == 4 && e.Source == recipe.Id && e.Kind == "attack");
        Assert.Contains(run.World.WaveRuntime.Work, w => w.Kind == "grain" && w.Source == slot && w.ParentId >= 0);
    }

    private static WorldReplayDocument TerminalReplay(Run run)
    {
        using var stream = new MemoryStream(); ReplayCodec.WriteHeader(stream, ReplayCodec.Header(run.Session.Options));
        foreach (var command in run.Commands) { ReplayCodec.WriteCommand(stream, command); }
        ReplayCodec.WriteWorldEnd(stream, new(run.Session.NextSequence, run.Session.GetWorldCombatResult()));
        stream.Position = 0; return ReplayCodec.ReadWorld(stream);
    }

    [Fact]
    public void VerifiedActiveTerminalExtendsAcceptedPrefixAndRejectsAnotherValidHistory()
    {
        var fixture = Fixture(worldConfigure: s => s with
        { Objectives = new[] { s.Objectives[0], s.Objectives[1] with { LimitTicks = 6 } } });
        var run = new Run(fixture); run.Prepare();
        var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        var accepted = WorldCombatApi.AcceptPrefix(fixture.Catalog, fixture.Options.DataHash, header, null, run.Commands);
        for (var tick = 2; tick < 6; tick++) { run.Step(); }
        var replay = TerminalReplay(run);
        var verified = WorldCombatApi.VerifyResult(fixture.Catalog, fixture.Options.DataHash, header, accepted.Recovery, replay);
        Assert.Equal(WorldTerminalReason.Timeout, verified.Terminal); Assert.Equal(run.Session.ComputeStateHash(), verified.StateHash);
        var terminal = WorldRecoveryCodec.Capture(run.Session, run.Commands);
        Assert.Equal(verified.StateHash, WorldCombatApi.VerifyResult(fixture.Catalog, fixture.Options.DataHash, header, terminal, replay).StateHash);
        var altered = replay with { End = replay.End with { Result = replay.End.Result with { StateHash = new string('F', 64) } } };
        Assert.Equal("battle-terminal", Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.VerifyResult(
            fixture.Catalog, fixture.Options.DataHash, header, terminal, altered)).Code);
        var alternate = new Run(fixture);
        for (var tick = 0; tick < 6; tick++) { alternate.Step(-1000, 0); }
        var alternateReplay = TerminalReplay(alternate);
        Assert.Equal(alternate.Session.ComputeStateHash(), WorldReplayRunner.Verify(fixture.Catalog, fixture.Options.DataHash, alternateReplay).StateHash);
        Assert.NotEqual(verified.StateHash, alternateReplay.End.Result.StateHash);
        Assert.Equal("frontier-conflict", Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.VerifyResult(
            fixture.Catalog, fixture.Options.DataHash, header, accepted.Recovery, alternateReplay)).Code);
    }

    [Theory]
    [InlineData("hero")]
    [InlineData("skills")]
    [InlineData("cohorts")]
    [InlineData("missing-time")]
    [InlineData("duplicate-time")]
    public void MalformedActiveWorldReturnsStructuredContractFailure(string field)
    {
        var fixture = Fixture(); var snapshot = fixture.Options.WorldCombat!.Snapshot;
        var changed = field switch
        {
            "hero" => snapshot with { Hero = null! },
            "skills" => snapshot with { Hero = snapshot.Hero with { Skills = null! } },
            "cohorts" => snapshot with { Cohorts = null! },
            "missing-time" => snapshot with { Objectives = new[] { snapshot.Objectives[0] } },
            _ => snapshot with { Objectives = new[] { snapshot.Objectives[0], snapshot.Objectives[1], snapshot.Objectives[1] with { Id = "test:time2" } } }
        };
        var options = fixture.Options with { WorldCombat = new(changed) };
        Assert.Throws<ArgumentException>(() => ReplayCodec.Header(options));
        var error = Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.CreateContract(fixture.Catalog, options));
        Assert.Equal("invalid-contract", error.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CatalogAndIssuedActiveOptionsMustBePresentTogether(bool removeIssued)
    {
        var fixture = Fixture();
        var catalog = removeIssued ? fixture.Catalog : fixture.Catalog with { HeroActive = null };
        var options = removeIssued ? fixture.Options with { HeroActive = null } : fixture.Options;
        var snapshot = options.WorldCombat!.Snapshot;
        options = options with { WorldCombat = new(snapshot with { Provenance = snapshot.Provenance with { ProfileHash = PortableStateCodec.HashCatalog(catalog) } }) };
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog, options));
        var error = Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.CreateContract(catalog, options));
        Assert.Equal("invalid-contract", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ActiveSkillCannotAlsoContributePassiveRawEffects(int raw)
    {
        var fixture = Fixture(worldConfigure: s => WithSkillEffect(s, "test:dash", new string('A', 64), raw));
        Assert.Throws<ArgumentException>(() => ReplayCodec.Header(fixture.Options));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(fixture.Catalog, fixture.Options));
        var error = Assert.Throws<WorldCombatApiException>(() => WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options));
        Assert.Equal("invalid-contract", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void ExplicitPassiveSkillMayContributeRawEffectsAlongsideDash(int raw)
    {
        var fixture = Fixture(configure: a => a with
        {
            Skills = a.Skills.Append(new HeroSkillBinding("test:passive", 1, new string('B', 64), HeroSkillKind.Passive)).ToArray()
        }, worldConfigure: s => WithSkillEffect(s, "test:passive", new string('B', 64), raw));
        var header = WorldCombatApi.CreateContract(fixture.Catalog, fixture.Options);
        Assert.Equal(raw, header.Options.WorldCombat!.Snapshot.Effects[0].Raw);
        var run = new Run(fixture); run.Prepare(); Assert.True(run.Frame.Active);
    }

    private static WorldCombatSnapshot WithSkillEffect(WorldCombatSnapshot snapshot, string id, string hash, int raw)
    {
        var effects = snapshot.Effects.ToArray(); var effect = effects[0];
        effects[0] = effect with
        {
            Raw = raw,
            Bonus = Math.Min(raw, effect.Cap),
            Sources = new[] { new WorldRawEffect(WorldEffectSourceKind.Hero, id, hash, raw) }
        };
        return snapshot with { Effects = effects };
    }
}
