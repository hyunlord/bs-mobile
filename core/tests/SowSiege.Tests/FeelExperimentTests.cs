using System.Text;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class FeelExperimentTests
{
    private static ContentCatalog Catalog() => ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
    private static InteractiveOptions Options(ContentCatalog c, FeelExperimentOptions? feel = null, string? target = null) => new(new(30000, c.Tuning.DefaultHero, c.Tuning.DefaultEstate, "mixed", ManualCards: true, TargetMaterial: target), AimMode.Movement, new string('a', 64), feel);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ContactRequiresCompleteBodyWidthsButLegacyAndOtherExperimentsDoNot(bool absent)
    {
        var original = Catalog(); var wave = original.WaveRuntime!;
        var widths = new Dictionary<string, int>(wave.EnemySeparation!.BodyWidths);
        var ordinary = wave.Enemies.Values.First(e => e.Kind == WaveEnemyKind.Pursuer);
        widths.Remove(ordinary.Id);
        var incomplete = original with { WaveRuntime = wave with { EnemySeparation = absent ? null : new(1, widths) } };
        Assert.Throws<ArgumentException>(() => new InteractiveSession(incomplete, Options(incomplete, new(ImmediateContact: true))));
        Assert.Null(new InteractiveSession(incomplete, Options(incomplete)).State.Feel);
        Assert.Null(new InteractiveSession(incomplete, Options(incomplete, new())).State.Feel);
        Assert.NotNull(new InteractiveSession(incomplete, Options(incomplete, new(FasterLord: true, ReadableHealth: true))).State.Feel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("meta:grain")]
    public void OffRetainsLegacyHeaderBytesAndState(string? target)
    {
        var catalog = Catalog(); var options = Options(catalog, target: target);
        using var expected = new MemoryStream();
        using (var writer = new BinaryWriter(expected, Encoding.UTF8, true))
        {
            void Text(string? value)
            {
                writer.Write(value is not null); if (value is null)
                {
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
            }
            Text("SowSiege-replay"); writer.Write(target is null ? 1 : 2); writer.Write(1); writer.Write(1); Text(PortableRandom.Algorithm);
            writer.Write(30000); Text(options.Run.HeroId); Text(options.Run.EstateId); Text("mixed"); Text(null); Text("normal"); writer.Write(true); Text(null);
            writer.Write((int)AimMode.Movement); Text(options.DataHash); if (target is not null)
            {
                Text(target);
            }
        }
        var normal = new InteractiveSession(catalog, options);
        var explicitOff = new InteractiveSession(catalog, options with { Feel = new() });
        using var actual = new MemoryStream(); ReplayCodec.WriteHeader(actual, ReplayCodec.Header(explicitOff.Options));
        Assert.Equal(expected.ToArray(), actual.ToArray()); Assert.Null(explicitOff.Options.Feel); Assert.Null(explicitOff.State.Feel);
        for (var tick = 0; tick < 30; tick++)
        {
            var command = new ReplayCommand(tick, tick, ReplayCommandKind.Advance, new(1000, 0));
            normal.Apply(command); explicitOff.Apply(command); Assert.Equal(normal.ComputeStateHash(), explicitOff.ComputeStateHash());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public void RulesAreRecordedAndRestoredWithoutPreferences(int flags)
    {
        var catalog = Catalog(); var feel = new FeelExperimentOptions((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0);
        var options = Options(catalog, feel); var session = new InteractiveSession(catalog, options);
        using var bytes = new MemoryStream(); var header = ReplayCodec.Header(options); Assert.Equal(3, header.FormatVersion);
        ReplayCodec.WriteHeader(bytes, header);
        for (var tick = 0; tick < 30; tick++)
        {
            var command = new ReplayCommand(tick, tick, ReplayCommandKind.Advance, new(1000, 0));
            session.Apply(command); ReplayCodec.WriteCommand(bytes, command);
        }
        ReplayCodec.WriteEnd(bytes, new(30, 30, ReplayEndKind.Quit, session.ComputeStateHash()));
        bytes.Position = 0; var replay = ReplayCodec.Read(bytes);
        Assert.Equal(feel, replay.Header.Options.Feel);
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, replay).StateHash);
        Assert.Equal(feel.ReadableHealth ? 100 : 1800, session.View.CaptureFrame().Lord.MaxHealth);
        var oldHash = session.ComputeStateHash(); session.State.Feel!.RemainderX++; Assert.NotEqual(oldHash, session.ComputeStateHash());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(8, 1)]
    [InlineData(1, 2)]
    public void ReplayRejectsUnknownOrEmptyExperiment(int flags, int version)
    {
        var c = Catalog(); using var bytes = new MemoryStream(); ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(Options(c, new(true))));
        bytes.Position = bytes.Length - 8;
        using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true)) { writer.Write(version); writer.Write(flags); }
        bytes.Position = 0; Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(bytes));
    }

    [Fact]
    public void FractionalSpeedCarriesSignedInputAndClearsOnStopReversalAndBoundary()
    {
        var map = Catalog().Tuning.World.Map;
        var state = new FeelExperimentState(new(true)); var point = new Position(6000, 6000);
        for (var i = 0; i < 10; i++)
        {
            point = state.Move(point, new(1000, 0), 24, map);
        }

        Assert.Equal(6312, point.X);
        for (var i = 0; i < 10; i++)
        {
            point = state.Move(point, new(-1000, 0), 24, map);
        }

        Assert.Equal(6000, point.X);
        point = state.Move(point, new(1000, 0), 24, map); Assert.NotEqual(0, state.RemainderX);
        point = state.Move(point, default, 24, map); Assert.Equal(0, state.RemainderX);
        point = state.Move(new(map.Width - 1, 0), new(1000, -1000), 24, map);
        Assert.Equal(new Position(map.Width, 0), point); Assert.Equal(0, state.RemainderX); Assert.Equal(0, state.RemainderY);
        point = new(6000, 6000);
        for (var i = 0; i < 10; i++)
        {
            point = state.Move(point, new(1000, 0), 30, map);
        }

        Assert.Equal(6390, point.X);
    }

    private static (WaveEnemySystem System, WorldState World, InteractiveState Interactive, EnemyState Enemy) Arena(WaveEnemyKind kind, bool immediate = true, bool health = false, bool elite = false)
    {
        var c = Catalog(); var original = c.WaveRuntime!.Enemies.Values.Single(e => e.Kind == kind); var id = original.Id;
        var rule = original with { FirstSpawnTick = 100000, TellTicks = 3 };
        c = c with { WaveRuntime = c.WaveRuntime with { Enemies = new Dictionary<string, WaveEnemyDefinition> { [id] = rule }, BossSpawnTick = 100000 } };
        if (elite) { c = c with { FirstPlayable = FirstPlayableTests.Catalog("arc").FirstPlayable! with { Enemies = new Dictionary<string, FirstPlayableEnemyDefinition> { [id] = new("elite", 0, 0, 1) } } }; }
        var world = new WorldState { Lord = new(6000, 6000), LordHealth = health ? 100 : 1800, WaveRuntime = new(), NextId = 10 };
        var interactive = new InteractiveState { Feel = new(new(false, immediate, health)) };
        var enemy = new EnemyState { Id = 1, Definition = id, Position = new(6000, 6000), Health = 100 };
        world.Enemies.Add(enemy);
        return (new(c, world, new TrackedRandom(1, true), interactive), world, interactive, enemy);
    }

    [Fact]
    public void OrdinaryContactIsImmediateWithGlobalFifteenTickImmunityAndLordKnockback()
    {
        var (system, world, interactive, enemy) = Arena(WaveEnemyKind.Pursuer);
        world.Enemies.Add(new() { Id = 2, Definition = enemy.Definition, Position = enemy.Position, Health = 100 });
        system.Tick(); Assert.Equal(1788, world.LordHealth); Assert.Equal(6072, world.Lord.X); Assert.Equal(15, interactive.Feel!.InvulnerableUntil);
        for (var tick = 1; tick < 15; tick++)
        { world.Tick = tick; enemy.Position = world.Lord; system.Tick(); Assert.Equal(1788, world.LordHealth); }
        world.Tick = 15; enemy.Position = world.Lord; system.Tick(); Assert.Equal(1776, world.LordHealth);
    }

    [Theory]
    [InlineData(WaveEnemyKind.Charger)]
    [InlineData(WaveEnemyKind.FloodBoss)]
    public void SpecialEnemyKeepsTellEvenWhenTouching(WaveEnemyKind kind)
    {
        var (system, world, _, enemy) = Arena(kind); system.Tick();
        Assert.Equal(1800, world.LordHealth); Assert.StartsWith("tell-", world.WaveRuntime!.EnemyActions[enemy.Id].Phase);
        Assert.Equal(new Position(6000, 6000), world.Lord);
    }

    [Fact]
    public void WaspRangedRadiusIsNotAContactRadiusAndProjectilesRespectImmunity()
    {
        var (system, world, interactive, enemy) = Arena(WaveEnemyKind.Ranged);
        enemy.Position = new(7000, 6000); system.Tick(); Assert.Equal(1800, world.LordHealth);
        interactive.Feel!.InvulnerableUntil = 15;
        world.WaveRuntime!.Projectiles.Add(new() { Id = 20, Source = enemy.Definition, Position = world.Lord, Direction = world.Lord, Damage = 8, Speed = 60, ExpireTick = 30, Hostile = true });
        world.Tick = 1; system.Tick(); Assert.Equal(1800, world.LordHealth);
    }

    [Fact]
    public void EliteMarkedOrdinaryArchetypeKeepsItsTell()
    {
        var (system, world, _, enemy) = Arena(WaveEnemyKind.Pursuer, elite: true); system.Tick();
        Assert.Equal(1800, world.LordHealth); Assert.Equal("tell-strike", world.WaveRuntime!.EnemyActions[enemy.Id].Phase);
    }

    [Fact]
    public void SessionMovementAndReadOnlyPredictionSpeedAgreeWithoutHashSideEffects()
    {
        var c = Catalog(); var session = new InteractiveSession(c, Options(c, new(FasterLord: true)));
        var initial = session.ComputeStateHash(); Assert.Equal(31.2, session.LocomotionSpeedPerTick, 10); Assert.Equal(initial, session.ComputeStateHash());
        var x = session.View.CaptureFrame().Lord.Position.X;
        for (var tick = 0; tick < 10; tick++)
        {
            session.Apply(new(tick, tick, ReplayCommandKind.Advance, new(1000, 0)));
        }

        Assert.Equal(x + 312, session.View.CaptureFrame().Lord.Position.X);
    }

    [Fact]
    public void ReadableHealthUsesActualDamageAndDeathWithoutChangingBaseCatalog()
    {
        var c = Catalog(); var session = new InteractiveSession(c, Options(c, new(ReadableHealth: true)));
        Assert.Equal(100, session.View.CaptureFrame().Lord.Health); Assert.Equal(1800, c.Tuning.World.Map.LordHealth);
        Assert.Equal(5, session.State.Feel!.Damage(1)); Assert.Equal(15, session.State.Feel.Damage(99));
        var (system, world, state, _) = Arena(WaveEnemyKind.Pursuer, health: true); world.LordHealth = 3; system.Tick();
        Assert.Equal(0, world.LordHealth); Assert.Single(state.Events); Assert.Equal(3, state.Events[0].Amount);
    }
}
