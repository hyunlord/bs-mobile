using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// ADR0064 Core slice: opt-in permanent light, sleeping buildings, dark T1 and replay format 7.
// Building IDs are test dummies (P8): Core carries no estate building identity.
public sealed class WaveEstateTests
{
    private static readonly string DataHash = new('A', 64);
    private const string Tower = "test:estate_tower", Well = "test:estate_well", Granary = "test:estate_granary";

    internal static WaveEstateLightDefinition Light(int darkPermille = 500, int towerTicks = 90) => new(WaveEstateLight.Version, "test:estate_home", 2400, 3000, darkPermille, 50, 120,
        new Dictionary<string, WaveEstateBuildingDefinition>
        {
            [Tower] = new(Tower, "test:design/tower", WaveEstateWake.Channel, 600, 2500, towerTicks, WaveEstateResource.Food, 0, 0),
            [Well] = new(Well, "test:design/well", WaveEstateWake.Spend, 600, 2500, 0, WaveEstateResource.Food, 3, 15),
            [Granary] = new(Granary, "test:design/granary", WaveEstateWake.Both, 600, 2500, 60, WaveEstateResource.Timber, 2, 15)
        },
        new Dictionary<string, WaveEstateSlot[]>
        {
            ["L1"] = new[] { new WaveEstateSlot(Tower, 3000, 6000), new WaveEstateSlot(Well, 3000, 6000), new WaveEstateSlot(Granary, 8000, 12000) },
            ["L3"] = new[] { new WaveEstateSlot(Tower, 4000, 5000), new WaveEstateSlot(Granary, 9000, 9000) }
        });
    private static WaveRuntimeFrame? Wave(InteractiveSession s) => ((IWaveRunView)s.View).CaptureWaveRuntime();
    internal static ContentCatalog Wave() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
    internal static ContentCatalog Catalog(WaveEstateLightDefinition? light = null)
    {
        var c = Wave();
        return c with { WaveRuntime = c.WaveRuntime! with { EstateLight = light ?? Light() } };
    }
    internal static InteractiveOptions Options(ContentCatalog c, string loadout = "L1", int seed = 30001, bool field = true, HostRewardRunOptions? rewards = null) =>
        new(new(seed, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, DataHash,
            Field: field ? new FieldOptions() : null, HostRewards: rewards, Estate: new(WaveEstateLight.Version, loadout, PortableStateCodec.HashCatalog(c)));

    private sealed class Run
    {
        public readonly InteractiveSession Session;
        public readonly List<ReplayCommand> Commands = new();
        public Run(ContentCatalog c, InteractiveOptions o) { Session = new(c, o); Apply(ReplayCommandKind.SetInvulnerable, 1); }
        public WorldState World => Session.Simulation.World;
        public WaveRuntimeState Wave => World.WaveRuntime!;
        public WaveEstateBuilding Building(string id) => Wave.Estate!.Single(b => b.DefinitionId == id);
        public void Apply(ReplayCommandKind kind, int value = 0, string? card = null)
        {
            var command = new ReplayCommand(Session.NextSequence, World.Tick, kind, CardId: card, Value: value);
            Session.Apply(command); Commands.Add(command);
        }
        // Advances whole ticks; offered cards are taken in offer order so level-ups never stall the run.
        public void Advance(int ticks)
        {
            for (var i = 0; i < ticks;)
            {
                if (Session.View.Status == RunStatus.AwaitingCard) { Apply(ReplayCommandKind.ChooseCard, card: Session.View.CaptureCards().Cards[0]); continue; }
                Apply(ReplayCommandKind.Advance); i++;
            }
        }
        // Test-only teleport: the lord stands still under zero input, so standing inside a circle needs no path planning.
        public void Stand(Position at) => World.Lord = at;
    }

    [Fact]
    public void RunsWithoutEstateOptionsOwnNoEstateStateFrameOrFacts()
    {
        var plain = Wave();
        Assert.Null(plain.WaveRuntime!.EstateLight);
        var withLight = Catalog();
        foreach (var c in new[] { plain, withLight })
        {
            var s = new InteractiveSession(c, new(new(30001, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "manual", ManualCards: true), AimMode.Movement, DataHash, Field: new()));
            s.Apply(new(0, 0, ReplayCommandKind.Advance));
            Assert.Null(s.Simulation.World.WaveRuntime!.Estate);
            Assert.Null(s.State.EstateLoadout);
            var wave = Wave(s)!;
            Assert.Null(wave.Estate);
            Assert.Null(MetaRunAdapter.FromWave(s.GetSummary(), s.View.CaptureFrame(), wave, false).DarkFinish);
            Assert.NotEqual(ReplayCodec.EstateVersion, ReplayCodec.Header(s.Options).FormatVersion);
        }
    }

    // Non-interactive runs hash their whole state as JSON; the opt-in estate members must not appear when they are off.
    [Fact]
    public void JsonStateOfRunsWithoutEstateHasNoEstateMembers()
    {
        var c = Wave();
        var simulation = SimulationFactory.Create(c, new RunOptions(7, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed"));
        simulation.Tick();
        var json = System.Text.Json.JsonSerializer.Serialize(simulation.World.WaveRuntime, HostJson.CreateOptions(includeFields: true));
        Assert.DoesNotContain("\"Estate", json);
    }

    [Fact]
    public void HomeIsLitFromTheFirstTickAndSlotsArePlacedDeterministicallyWithinTheirRings()
    {
        var c = Catalog();
        var run = new Run(c, Options(c));
        var home = run.World.Estate;
        var estate = run.Wave.Estate!;
        Assert.Equal(4, estate.Count);
        Assert.Equal(WaveEstatePhase.Active, estate[0].Phase);
        Assert.Equal(home, estate[0].Position);
        Assert.All(estate.Skip(1), b => Assert.Equal(WaveEstatePhase.Asleep, b.Phase));
        var slots = Light().Loadouts["L1"];
        for (var i = 0; i < slots.Length; i++)
        {
            var distance = Math.Sqrt(estate[i + 1].Position.DistanceSquared(home));
            Assert.InRange(distance, slots[i].MinRadius * 0.98, slots[i].MaxRadius * 1.01);
        }
        Assert.True(WaveEstateSystem.Lit(c.WaveRuntime!, estate, home));
        Assert.False(WaveEstateSystem.Lit(c.WaveRuntime!, estate, estate[1].Position));
        var frame = Wave(run.Session)!.Estate!;
        Assert.Equal("L1", frame.LoadoutId);
        Assert.Equal(WaveEstateGround.Restored, WaveEstateLight.Ground(frame, new WorldPoint(home.X + 20000, home.Y), new WorldPoint(home.X + 100, home.Y)));
        Assert.Equal(WaveEstateGround.Fallow, WaveEstateLight.Ground(frame, new WorldPoint(home.X + 20000, home.Y), new WorldPoint(home.X + 20000, home.Y + 2000)));
        Assert.Equal(WaveEstateGround.Dark, WaveEstateLight.Ground(frame, new WorldPoint(home.X + 20000, home.Y), new WorldPoint(home.X + 10000, home.Y + 10000)));

        var again = new Run(c, Options(c));
        Assert.Equal(estate.Select(b => b.Position), again.Wave.Estate!.Select(b => b.Position));
        var other = new Run(c, Options(c, seed: 30002));
        Assert.NotEqual(estate.Select(b => b.Position), other.Wave.Estate!.Select(b => b.Position));
        Assert.Empty(run.Wave.Work);
    }

    [Fact]
    public void ChannelPausesOutsideTheCircleCompletesOnceAndTheLightNeverShrinks()
    {
        var c = Catalog();
        var run = new Run(c, Options(c));
        var tower = run.Building(Tower);
        run.Stand(tower.Position);
        run.Advance(30);
        Assert.Equal(WaveEstatePhase.Waking, tower.Phase);
        Assert.Equal(30, tower.Progress);
        run.Stand(new(tower.Position.X + 5000, tower.Position.Y));
        run.Advance(40);
        Assert.Equal(30, tower.Progress);
        Assert.Equal(WaveEstatePhase.Waking, tower.Phase);
        run.Stand(tower.Position);
        run.Advance(60);
        Assert.Equal(WaveEstatePhase.Active, tower.Phase);
        Assert.Contains("estate-wake:" + tower.Id, run.Wave.Completed);
        Assert.Equal(1, run.Wave.Counters["estate-wake-start:" + Tower]);
        Assert.Equal(1, run.Wave.Counters["estate-wake-complete:" + Tower]);
        Assert.True(WaveEstateSystem.Lit(c.WaveRuntime!, run.Wave.Estate!, tower.Position));
        run.Stand(new(tower.Position.X + 20000, tower.Position.Y));
        run.Advance(200);
        Assert.Equal(WaveEstatePhase.Active, tower.Phase);
        Assert.True(WaveEstateSystem.Lit(c.WaveRuntime!, run.Wave.Estate!, new(tower.Position.X + 2400, tower.Position.Y)));
        Assert.Equal(1, run.Wave.Counters["estate-wake-complete:" + Tower]);
    }

    [Fact]
    public void SpendingNeedsAnUninterruptedStayAndEnoughFoodAndPaysOnce()
    {
        var c = Catalog();
        var run = new Run(c, Options(c));
        var well = run.Building(Well);
        run.World.Food = 3;
        run.Stand(well.Position);
        run.Advance(10);
        run.Stand(new(well.Position.X + 5000, well.Position.Y));
        run.Advance(1);
        Assert.Equal(0, well.Dwell);
        Assert.Equal(3, run.World.Food);
        run.World.Food = 2;
        run.Stand(well.Position);
        run.Advance(40);
        Assert.False(well.Paid);
        Assert.Equal(WaveEstatePhase.Asleep, well.Phase);
        Assert.Equal(2, run.World.Food);
        run.World.Food = 5;
        run.Advance(1);
        Assert.True(well.Paid);
        Assert.Equal(WaveEstatePhase.Active, well.Phase);
        Assert.Equal(2, run.World.Food);
        run.Advance(30);
        Assert.Equal(2, run.World.Food);
        Assert.Equal(1, run.Wave.Counters["estate-spend:" + Well]);
    }

    [Fact]
    public void BothPaysTimberOnceThenChannelsWithoutRefundOrRepayWhenLeft()
    {
        var c = Catalog();
        var run = new Run(c, Options(c));
        var granary = run.Building(Granary);
        var timber = run.Wave.Timber;
        run.Stand(granary.Position);
        run.Advance(20);
        Assert.True(granary.Paid);
        Assert.Equal(timber - 2, run.Wave.Timber);
        Assert.Equal(WaveEstatePhase.Waking, granary.Phase);
        var progress = granary.Progress;
        Assert.InRange(progress, 1, 59);
        run.Stand(new(granary.Position.X + 5000, granary.Position.Y));
        run.Advance(30);
        run.Stand(granary.Position);
        run.Advance(60 - progress);
        Assert.Equal(WaveEstatePhase.Active, granary.Phase);
        Assert.Equal(timber - 2, run.Wave.Timber);
        Assert.Equal(1, run.Wave.Counters["estate-spend:" + Granary]);
    }

    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(0, 500, 0)]
    [InlineData(10, 500, 15)]
    [InlineData(3, 333, 4)]
    [InlineData(7, 0, 7)]
    public void DarkDamageMultipliesFirstThenRoundsUp(int damage, int permille, int expected) =>
        Assert.Equal(expected, WaveEstateSystem.Darken(damage, permille));

    [Fact]
    public void EnemiesHitHarderOnlyFromOutsideEveryLitZone()
    {
        long Damage(bool dark, out long darkHits)
        {
            var c = Catalog();
            var s = new InteractiveSession(c, Options(c));
            var world = s.Simulation.World;
            var at = dark ? new Position(world.Estate.X + 20000, world.Estate.Y + 20000) : world.Estate;
            var definition = c.WaveRuntime!.Enemies.Values.First(e => e.Kind == WaveEnemyKind.Pursuer).Id;
            long dealt = 0;
            var enemy = new EnemyState { Id = world.AllocateId(), Definition = definition, Position = new(at.X + 10, at.Y), Health = int.MaxValue };
            world.Enemies.Add(enemy);
            for (var tick = 0; tick < 600; tick++)
            {
                if (s.View.Status == RunStatus.AwaitingCard) { s.Apply(new(s.NextSequence, world.Tick, ReplayCommandKind.ChooseCard, CardId: s.View.CaptureCards().Cards[0])); }
                world.Lord = at; world.LordHealth = 100000; enemy.Position = new(at.X + 10, at.Y); enemy.Health = int.MaxValue;
                s.Apply(new(s.NextSequence, world.Tick, ReplayCommandKind.Advance));
                dealt += 100000 - world.LordHealth;
            }
            darkHits = world.WaveRuntime!.Counters.Where(p => p.Key.StartsWith("estate-dark-hit:", StringComparison.Ordinal)).Sum(p => p.Value);
            return dealt;
        }
        var lit = Damage(false, out var litDark);
        var dark = Damage(true, out var darkHits);
        Assert.Equal(0, litDark);
        Assert.True(lit > 0);
        Assert.True(darkHits > 0);
        Assert.True(dark > lit, $"dark {dark} should exceed lit {lit}");
    }

    [Fact]
    public void FrameCountsSleepingBuildingsAndCapsTheDarkFinishBonus()
    {
        var c = Catalog();
        var run = new Run(c, Options(c));
        var frame = Wave(run.Session)!.Estate!;
        Assert.Equal(3, frame.SleepingBuildings);
        Assert.Equal(120, frame.DarkFinishBonusPermille);
        var tower = run.Building(Tower);
        run.Stand(tower.Position);
        run.Advance(90);
        frame = Wave(run.Session)!.Estate!;
        Assert.Equal(2, frame.SleepingBuildings);
        Assert.Equal(100, frame.DarkFinishBonusPermille);
        var facts = MetaRunAdapter.FromWave(run.Session.GetSummary(), run.Session.View.CaptureFrame(), Wave(run.Session)!, false);
        Assert.Equal(new MetaDarkFinishFacts(2, 100), facts.DarkFinish);
    }

    [Fact]
    public void DarkFinishBonusIsPaidOnlyForClearedRunsWithinTheRunCapAndOutsideTheAdvertisementBasis()
    {
        var catalog = MetaEngineTests.Catalog();
        var state = MetaEngine.NewGame(catalog);
        var small = MetaEngineTests.Success with { Farms = 1, Buildings = 1, People = 1, Harvests = 1, Level = 1 };
        var plain = MetaEngine.SettleRun(catalog, MetaEngine.BeginRun(catalog, state, "chapter-1", 1).State, MetaEngine.BeginRun(catalog, state, "chapter-1", 1).Run, small);
        var plan = MetaEngine.BeginRun(catalog, state, "chapter-1", 1);
        var bonus = MetaEngine.SettleRun(catalog, plan.State, plan.Run, small with { DarkFinish = new(3, 250) });
        Assert.Null(plain.DarkFinish);
        Assert.NotNull(bonus.DarkFinish);
        var extra = bonus.DarkFinish!.Awarded["wood"];
        Assert.True(extra > 0);
        Assert.Equal(plain.Awarded["wood"] + extra, bonus.Awarded["wood"]);
        Assert.Equal(plain.Awarded["wood"], bonus.State.ResultRewardBasis!.Awarded["wood"]);
        foreach (var facts in new[] { small with { Survived = false }, small with { Abandoned = true }, small with { Tick = 99 }, small with { BossDefeated = false } })
        {
            var run = MetaEngine.BeginRun(catalog, state, "chapter-1", 1);
            Assert.Null(MetaEngine.SettleRun(catalog, run.State, run.Run, facts with { DarkFinish = new(3, 250) }).DarkFinish);
        }
        var capped = MetaEngine.BeginRun(catalog, state, "chapter-1", 1);
        var full = MetaEngine.SettleRun(catalog, capped.State, capped.Run, MetaEngineTests.Success with { DarkFinish = new(3, 250) });
        Assert.Equal(0, full.DarkFinish!.Awarded["wood"]);
        Assert.Equal(100, full.Awarded["wood"]);
        var invalid = MetaEngine.BeginRun(catalog, state, "chapter-1", 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => MetaEngine.SettleRun(catalog, invalid.State, invalid.Run, small with { DarkFinish = new(-1, 10) }));
    }

    [Fact]
    public void FormatSixRoundTripsVerifiesAndRejectsAnotherLoadoutCatalog()
    {
        // Recorded runs only use recorded commands (no test teleport), so the replay must reproduce the hash exactly.
        var c = Catalog();
        var fresh = new Run(c, Options(c));
        fresh.Advance(300);
        var replay = Roundtrip(fresh.Session, fresh.Commands);
        Assert.Equal(ReplayCodec.EstateVersion, replay.Header.FormatVersion);
        Assert.Equal("L1", replay.Header.Options.Estate!.LoadoutId);
        Assert.Equal(fresh.Session.ComputeStateHash(), ReplayRunner.Verify(c, DataHash, replay).StateHash);

        var otherLoadouts = Catalog(Light(towerTicks: 91));
        Assert.ThrowsAny<Exception>(() => ReplayRunner.Verify(otherLoadouts, DataHash, replay));
        var l3 = new Run(c, Options(c, "L3"));
        l3.Advance(300);
        Assert.NotEqual(fresh.Session.ComputeStateHash(), l3.Session.ComputeStateHash());
        Assert.Equal("L3", Roundtrip(l3.Session, l3.Commands).Header.Options.Estate!.LoadoutId);
    }

    [Fact]
    public void SameEstateInputsGiveTheSameStateHash()
    {
        var c = Catalog();
        string Play()
        {
            var run = new Run(c, Options(c));
            run.Advance(900);
            return run.Session.ComputeStateHash();
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void FormatSixKeepsOptionalHostRewardsAndFieldAndRejectsMismatchedOrInvalidEstateOptions()
    {
        var c = Catalog();
        var options = Options(c, field: false, rewards: new(true, null));
        var run = new Run(c, options);
        run.Advance(5);
        var replay = Roundtrip(run.Session, run.Commands);
        Assert.Null(replay.Header.Options.Field);
        Assert.NotNull(replay.Header.Options.HostRewards);
        Assert.Equal(run.Session.ComputeStateHash(), ReplayRunner.Verify(c, DataHash, replay).StateHash);

        Assert.Throws<ArgumentException>(() => new InteractiveSession(c, Options(c, "L9")));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(c, Options(c) with { Estate = new(WaveEstateLight.Version, "L1", new string('B', 64)) }));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(c, Options(c) with { Estate = new(2, "L1", PortableStateCodec.HashCatalog(c)) }));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(Wave(), Options(c)));
        Assert.ThrowsAny<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), ReplayCodec.Header(Options(c)) with { FormatVersion = ReplayCodec.HostRewardVersion }));
        var plain = Options(c) with { Estate = null };
        Assert.ThrowsAny<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), ReplayCodec.Header(plain) with { FormatVersion = ReplayCodec.EstateVersion }));
    }

    [Fact]
    public void InvalidEstateDefinitionsAreRejected()
    {
        var light = Light();
        WaveEstateLight.Validate(light);
        Assert.Throws<ArgumentException>(() => WaveEstateLight.Validate(light with { Version = 2 }));
        Assert.Throws<ArgumentException>(() => WaveEstateLight.Validate(light with { Loadouts = new Dictionary<string, WaveEstateSlot[]> { ["L1"] = new[] { new WaveEstateSlot("test:missing", 1, 2) } } }));
        var buildings = light.Buildings.ToDictionary(p => p.Key, p => p.Value);
        buildings[Well] = buildings[Well] with { Cost = 0 };
        Assert.Throws<ArgumentException>(() => WaveEstateLight.Validate(light with { Buildings = buildings }));
        buildings[Well] = light.Buildings[Well] with { Id = light.HomeId };
        Assert.Throws<ArgumentException>(() => WaveEstateLight.Validate(light with { Buildings = buildings }));
    }

    private static ReplayDocument Roundtrip(InteractiveSession session, IReadOnlyList<ReplayCommand> commands)
    {
        using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(session.Options));
        foreach (var command in commands) { ReplayCodec.WriteCommand(bytes, command); }
        var summary = session.GetSummary();
        ReplayCodec.WriteEnd(bytes, new(session.NextSequence, summary.Tick, summary.EndReason == "death" ? ReplayEndKind.Death : summary.EndReason == "duration" ? ReplayEndKind.Duration : ReplayEndKind.Quit, summary.StateHash));
        bytes.Position = 0; return ReplayCodec.Read(bytes);
    }

    // A format 7 record made by main 75ffc97 (first slice only, no effects) must keep verifying: an effect-less light and state
    // keep the first slice's bytes; effects use their own optional blocks (Astra review on #323, 2026-10-23).
    [Fact]
    public void FormatSevenRecordFromMainBeforeEffectsStillVerifies()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "benchmarks"))) { directory = directory.Parent; }
        var path = Path.Combine(directory!.FullName, "benchmarks", "estate-v7", "main-75ffc97-l1-30001.ssreplay");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("7E26EEEEF4A856AD69E62857D09903C3633DE27315D01538770C962AABEFF31F", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        var c = Catalog();
        Assert.Equal("D536D58C8F998C3C8AE3CAACF85AE0A89F33C019A6F00195631AF18FA2374508", PortableStateCodec.HashCatalog(c));
        using var input = new MemoryStream(bytes); var replay = ReplayCodec.Read(input);
        Assert.Equal(ReplayCodec.EstateVersion, replay.Header.FormatVersion);
        var verification = ReplayRunner.Verify(c, DataHash, replay);
        Assert.Equal((1800, ReplayEndKind.Quit, "57E2A2B0EA074EC4A4038B3EC3B9050F6ED09CC962D88CB54BB8DF986F484333"),
            (verification.Tick, (ReplayEndKind)verification.EndKind, verification.StateHash));
    }
}
