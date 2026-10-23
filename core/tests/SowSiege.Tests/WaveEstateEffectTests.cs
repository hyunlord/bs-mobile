using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

// ADR0064 second Core slice and ADR0069: building effects, the opt-in data profile and bot runs with the dark estate.
public sealed class WaveEstateEffectTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private const string Profile = "wave-2a-dark";

    [Fact]
    public void OptInFilesStayOutsideTheDataTreeHash()
    {
        var copy = Path.Combine(Path.GetTempPath(), "bs-optin-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyTree(Data, copy);
            Assert.True(Directory.Exists(Path.Combine(copy, ContentLoader.OptInDirectory)));
            var withOptIn = ContentLoader.Hash(copy, false);
            Directory.Delete(Path.Combine(copy, ContentLoader.OptInDirectory), true);
            Assert.Equal(withOptIn, ContentLoader.Hash(copy, false));
            Assert.Equal(ContentLoader.Hash(Data, false), withOptIn);
        }
        finally { if (Directory.Exists(copy)) { Directory.Delete(copy, true); } }
    }

    [Fact]
    public void OptInProfileIsWave1aPlusTheEstateLight()
    {
        var wave = ContentLoader.Load(Data, false, "wave-1a");
        var dark = ContentLoader.Load(Data, false, Profile);
        Assert.Null(wave.WaveRuntime!.EstateLight);
        var light = dark.WaveRuntime!.EstateLight!;
        Assert.Equal(new[] { "L1", "L3" }, light.Loadouts.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(PortableStateCodec.HashCatalog(wave), PortableStateCodec.HashCatalog(dark with { WaveRuntime = dark.WaveRuntime with { EstateLight = null } }));
        Assert.Equal("L1", ContentLoader.LoadProfile(Data, Profile).EstateLoadout);
        Assert.All(light.Buildings.Values, b => Assert.NotNull(b.Effect));
    }

    // A wave-1a bot run must not change with the estate code. Golden from ac78982 was 5FAA2C4D…; main changed the encoded state
    // (ADR0058 132eb0d, #257 92d55b6, #287 9f2e31f; every other result field equal), so it is re-pinned to main e719918 (ADR0069).
    [Fact]
    public void Wave1aBotHashIsUnchangedByTheEstateCode()
    {
        var c = ContentLoader.Load(Data, false, "wave-1a");
        c = c with { Tuning = c.Tuning with { DurationTicks = 900 } };
        // Same options as `SowSiege.Sim --profile wave-1a --seed 7 --duration-ticks 900` (the CLI passes the default people rule).
        var simulation = SimulationFactory.Create(c, new RunOptions(7, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", c.Tuning.World.DefaultPeopleRule));
        while (!simulation.IsComplete) { simulation.Tick(); }
        Assert.Equal("E7963B7C0D511AA263EE56A5C1F2197D69E06BBEFE87B6135B9EEE827BBDC570", simulation.Result().Hash);
    }

    private static (Simulation Simulation, ContentCatalog Catalog) Bot(string? loadout, string? movement, int ticks, int seed = 7)
    {
        var c = ContentLoader.Load(Data, false, Profile);
        c = c with { Tuning = c.Tuning with { DurationTicks = ticks } };
        var simulation = SimulationFactory.Create(c, new RunOptions(seed, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", Movement: movement, EstateLoadout: loadout));
        while (!simulation.IsComplete) { simulation.Tick(); }
        return (simulation, c);
    }

    [Fact]
    public void BotsWakeTheBuildingsOnlyWithTheEstateWakeMovementAndRepeatExactly()
    {
        var (walker, _) = Bot("L1", Simulation.EstateWakeMovement, 3000);
        Assert.All(walker.World.WaveRuntime!.Estate!, b => Assert.Equal(WaveEstatePhase.Active, b.Phase));
        Assert.True(walker.World.WaveRuntime.Counters.GetValueOrDefault("estate-shot:core:estate_watchtower") > 0);
        Assert.Equal(walker.Result().Hash, Bot("L1", Simulation.EstateWakeMovement, 3000).Simulation.Result().Hash);
        var (wanderer, _) = Bot("L1", null, 3000);
        Assert.Contains(wanderer.World.WaveRuntime!.Estate!, b => b.Phase != WaveEstatePhase.Active);
        var (off, _) = Bot(null, null, 900);
        Assert.Null(off.World.WaveRuntime!.Estate);
    }

    [Fact]
    public void BotEstateOptionsAreRejectedWhereTheyCannotApply()
    {
        var wave = ContentLoader.Load(Data, false, "wave-1a");
        var dark = ContentLoader.Load(Data, false, Profile);
        RunOptions Run(ContentCatalog c, string? loadout, string? movement) => new(7, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", Movement: movement, EstateLoadout: loadout);
        Assert.Throws<ArgumentException>(() => SimulationFactory.Create(wave, Run(wave, "L1", null)));
        Assert.Throws<ArgumentException>(() => SimulationFactory.Create(dark, Run(dark, "L9", null)));
        Assert.Throws<ArgumentException>(() => SimulationFactory.Create(dark, Run(dark, null, Simulation.EstateWakeMovement)));
        var options = WaveEstateTests.Options(dark) with { Run = Run(dark, "L1", null) with { Policy = "manual", ManualCards = true } };
        Assert.Throws<ArgumentException>(() => new InteractiveSession(dark, options));
    }

    // Recreates the estate on a live session world to drive one effect at a time.
    private static (InteractiveSession Session, WaveEstateSystem System, WaveEstateBuilding Building, WaveEstateBuildingDefinition Definition) Effect(string id)
    {
        var c = ContentLoader.Load(Data, false, Profile);
        var session = new InteractiveSession(c, WaveEstateTests.Options(c));
        var world = session.Simulation.World;
        var system = WaveEstateSystem.Create(c, world, null, "L1", 1)!;
        var building = world.WaveRuntime!.Estate!.Single(b => b.DefinitionId == id);
        building.Phase = WaveEstatePhase.Active;
        world.Enemies.Clear(); world.WaveRuntime.Rewards.Clear(); world.WaveRuntime.Events.Clear();
        return (session, system, building, c.WaveRuntime!.EstateLight!.Buildings[id]);
    }

    [Fact]
    public void WatchtowerShootsTheNearestEnemyInsideItsLightOnItsOwnClock()
    {
        var (session, system, tower, definition) = Effect("core:estate_watchtower");
        var world = session.Simulation.World;
        var effect = definition.Effect!;
        var near = new EnemyState { Id = 9001, Definition = "core:raider", Position = new(tower.Position.X + 500, tower.Position.Y), Health = 1000 };
        var far = new EnemyState { Id = 9002, Definition = "core:raider", Position = new(tower.Position.X + definition.LitRadius + 50, tower.Position.Y), Health = 1000 };
        world.Enemies.Add(far); world.Enemies.Add(near);
        system.Tick();
        Assert.Equal(1000 - effect.Amount, near.Health);
        Assert.Equal(1000, far.Health);
        system.Tick();
        Assert.Equal(1000 - effect.Amount, near.Health);
        world.Tick += effect.PeriodTicks;
        system.Tick();
        Assert.Equal(1000 - 2 * effect.Amount, near.Health);
        Assert.Equal(2 * effect.Amount, world.WaveRuntime!.Counters["estate-damage:core:estate_watchtower"]);
    }

    [Fact]
    public void WellHealsTheLordInsideItsLightUpToFullAndPausesAfterAHit()
    {
        var (session, system, well, definition) = Effect("core:estate_well_shrine");
        var world = session.Simulation.World;
        var effect = definition.Effect!;
        var full = world.LordHealth;
        world.Lord = new(well.Position.X + definition.LitRadius + 50, well.Position.Y);
        world.LordHealth = full - 100;
        system.Tick();
        Assert.Equal(full - 100, world.LordHealth);
        world.Lord = well.Position;
        system.Tick();
        Assert.Equal(full - 100 + effect.Amount, world.LordHealth);
        world.Tick += effect.PeriodTicks;
        world.WaveRuntime!.Emit(world.Tick, "lord-hit", "core:raider", 0, world.Lord, world.Lord, 5);
        system.Tick();
        Assert.Equal(full - 100 + effect.Amount, world.LordHealth);
        world.WaveRuntime.Events.Clear();
        world.Tick += effect.PauseTicks;
        world.LordHealth = full - 1;
        system.Tick();
        Assert.Equal(full, world.LordHealth);
    }

    [Fact]
    public void GranaryPullsRewardsTowardTheLordInsideItsLightAndAddsFoodToHarvestsThere()
    {
        var (session, system, granary, definition) = Effect("core:estate_granary");
        var world = session.Simulation.World;
        var effect = definition.Effect!;
        var reward = new WaveReward { Id = 77, Experience = 3, Source = "test", CompletionKey = "k", Position = new(granary.Position.X + 1500, granary.Position.Y) };
        world.WaveRuntime!.Rewards.Add(reward);
        world.Lord = new(granary.Position.X + definition.LitRadius + 50, granary.Position.Y);
        system.Tick();
        Assert.Equal(granary.Position.X + 1500, reward.Position.X);
        world.Lord = granary.Position;
        system.Tick();
        Assert.Equal(granary.Position.X + 1500 - effect.Amount, reward.Position.X);
        var food = world.Food;
        world.WaveRuntime.Emit(world.Tick, "harvest-complete", "core:seed_bag", 5, new(granary.Position.X + 100, granary.Position.Y), new(granary.Position.X + 100, granary.Position.Y));
        world.WaveRuntime.Emit(world.Tick, "harvest-complete", "core:seed_bag", 6, new(granary.Position.X + definition.LitRadius + 50, granary.Position.Y), new(0, 0));
        system.Tick();
        Assert.Equal(food + effect.Bonus, world.Food);
    }

    [Fact]
    public void EffectsAreValidatedPerKind()
    {
        var light = WaveEstateTests.Light();
        var buildings = light.Buildings.ToDictionary(p => p.Key, p => p.Value);
        foreach (var effect in new WaveEstateEffectDefinition[] { new(WaveEstateEffectKind.Attack, 0, 10), new(WaveEstateEffectKind.Attack, 5, 0), new(WaveEstateEffectKind.Heal, 5, 10, 0, 1), new(WaveEstateEffectKind.Magnet, 5, 10) })
        {
            buildings["test:estate_tower"] = light.Buildings["test:estate_tower"] with { Effect = effect };
            Assert.Throws<ArgumentException>(() => WaveEstateLight.Validate(light with { Buildings = buildings }));
        }
        buildings["test:estate_tower"] = light.Buildings["test:estate_tower"] with { Effect = new(WaveEstateEffectKind.Magnet, 5, 0, 0, 2) };
        WaveEstateLight.Validate(light with { Buildings = buildings });
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) { Directory.CreateDirectory(directory.Replace(from, to)); }
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) { File.Copy(file, file.Replace(from, to)); }
    }

    // The format 7 estate fixture (also replayed on both Core targets by tools/verify-estate-target-parity.mjs): normal input wakes
    // all three buildings and every effect fires (shots, heals and granary pulls). Its end is pinned so effect rules cannot drift.
    [Fact]
    public void EstateFixtureIsFormatSevenFiresEveryEffectAndKeepsItsEndHash()
    {
        var c = ContentLoader.Load(Data, false, Profile); var dataHash = ContentLoader.Hash(Data, false);
        using var stream = new MemoryStream();
        var observation = InteractiveCli.RecordEstateFixture(c, dataHash, InteractiveCli.EstateFixtureSeed, stream);
        Assert.True(observation.AllAwakeTick > 0);
        Assert.True(observation.Shots > 0 && observation.Heals > 0 && observation.MagnetPulls > 0, $"{observation}");
        Assert.Equal(0, observation.PullsOutsideGranaries);
        stream.Position = 0;
        var replay = ReplayCodec.Read(stream);
        Assert.Equal(ReplayCodec.EstateVersion, replay.Header.FormatVersion);
        Assert.Null(replay.Header.Options.Feel);
        var verification = ReplayRunner.Verify(c, dataHash, replay);
        Assert.Equal((2688, ReplayEndKind.Quit, "FD73A19A584ECB7F883D6B02E46775332A35869F44F464D488F41DF5DEA8D746"),
            (verification.Tick, (ReplayEndKind)verification.EndKind, verification.StateHash));
    }

    // A weapon and the watchtower can both reach a one-health enemy on the same tick; the kill and its experience are paid once.
    [Fact]
    public void WatchtowerAndWeaponOnTheSameEnemyPayTheKillOnce()
    {
        var c = ContentLoader.Load(Data, false, Profile);
        var session = new InteractiveSession(c, WaveEstateTests.Options(c));
        var world = session.Simulation.World;
        var tower = world.WaveRuntime!.Estate!.Single(b => b.DefinitionId == "core:estate_watchtower");
        tower.Phase = WaveEstatePhase.Active; tower.ReadyTick = 0; tower.Position = world.Lord;
        world.Enemies.Clear();
        var enemy = new EnemyState { Id = 9100, Definition = "core:raider", Position = new(world.Lord.X + 150, world.Lord.Y), Health = 1 };
        world.Enemies.Add(enemy);
        var experience = world.KillExperience;
        session.Apply(new ReplayCommand(session.NextSequence, session.View.CaptureFrame().Tick, ReplayCommandKind.Advance));
        var events = ((IWaveRunView)session.View).CaptureWaveRuntime()!.Events;
        Assert.Single(events, e => e.Kind == "enemy-killed" && e.SubjectId == enemy.Id);
        Assert.True(events.Count(e => e.Kind == "estate-shot" && e.SubjectId == enemy.Id) <= 1);
        Assert.Equal(experience + c.Enemies["core:raider"].Experience, world.KillExperience);
        Assert.DoesNotContain(world.Enemies, e => e.Id == enemy.Id);
    }

    // The well heals up to the run's own maximum (the readable-health feel caps it at 100, below the map value) and never raises a
    // fallen hero.
    [Fact]
    public void WellHealsOnlyUpToTheFeelMaximumAndNeverRevives()
    {
        var c = ContentLoader.Load(Data, false, Profile);
        var session = new InteractiveSession(c, WaveEstateTests.Options(c) with { Feel = new FeelExperimentOptions(ReadableHealth: true) });
        var world = session.Simulation.World;
        var system = WaveEstateSystem.Create(c, world, session.State, "L1", 1)!;
        var well = world.WaveRuntime!.Estate!.Single(b => b.DefinitionId == "core:estate_well_shrine");
        var effect = c.WaveRuntime!.EstateLight!.Buildings[well.DefinitionId].Effect!;
        well.Phase = WaveEstatePhase.Active; world.Lord = well.Position;
        world.Enemies.Clear(); world.WaveRuntime.Events.Clear();
        var maximum = session.View.CaptureFrame().Lord.MaxHealth;
        Assert.Equal(100, maximum);
        Assert.True(c.Tuning.World.Map.LordHealth > maximum);
        world.LordHealth = maximum - 5;
        system.Tick();
        Assert.Equal(maximum, world.LordHealth);
        world.Tick += effect.PeriodTicks;
        system.Tick();
        Assert.Equal(maximum, world.LordHealth);
        world.LordHealth = 0;
        world.Tick += effect.PeriodTicks;
        system.Tick();
        Assert.Equal(0, world.LordHealth);
        Assert.Equal(5, world.WaveRuntime.Counters["estate-healing:core:estate_well_shrine"]);
    }

    // A harvest inside both the home light and the granary light adds the granary bonus once; the home has no effect of its own.
    [Fact]
    public void HarvestInsideTheHomeAndGranaryLightsGetsTheBonusOnce()
    {
        var (session, system, granary, definition) = Effect("core:estate_granary");
        var world = session.Simulation.World; var runtime = ContentLoader.Load(Data, false, Profile).WaveRuntime!;
        var home = world.WaveRuntime!.Estate!.Single(b => b.DefinitionId == runtime.EstateLight!.HomeId);
        granary.Position = new(home.Position.X + 4000, home.Position.Y);
        var at = new Position(home.Position.X + 2000, home.Position.Y);
        Assert.True(WaveEstateSystem.Lit(runtime, new() { home }, at));
        var food = world.Food;
        world.WaveRuntime.Emit(world.Tick, "harvest-complete", "core:seed_bag", 5, at, at);
        system.Tick();
        Assert.Equal(food + definition.Effect!.Bonus, world.Food);
        Assert.Single(world.WaveRuntime.Events, e => e.Kind == "estate-harvest-bonus");
    }
}
