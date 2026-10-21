using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;

public sealed class MeleeAimTests
{
    private static (WaveRuntimeSystem System, WorldState World, EnemyState Enemy) Arena(bool enabled, bool configured = true)
    {
        var (catalog, _, world, enemy) = WaveRuntimeTests.Arena(WaveAttackKind.Arc);
        var gear = catalog.WaveRuntime!.Gear.Values.Single();
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with { Aim = configured ? "nearest-enemy" : null };
        return (new(catalog, world, new(1, true), new InteractiveState { MeleeAim = enabled ? new() : null }), world, enemy);
    }
    [Fact]
    public void OptInMeleeTurnsBehindLordWhileLegacyKeepsFacing()
    {
        foreach (var enabled in new[] { false, true })
        {
            var (system, world, enemy) = Arena(enabled); enemy.Position = new(400, 500);
            system.Tick(world.Lord);
            Assert.Equal(enabled ? 90 : 100, enemy.Health);
        }
    }
    [Fact]
    public void EqualDistanceUsesStableIdEvenWhenWorldListOrderDiffers()
    {
        var (system, world, enemy) = Arena(true);
        world.Enemies.Add(new() { Id = 5, Definition = enemy.Definition, Position = new(400, 500), Health = 100 });
        system.Tick(world.Lord);
        Assert.Equal(100, enemy.Health); Assert.Equal(90, world.Enemies[1].Health);
    }
    [Fact]
    public void TargetBeyondHitRangeChangesOnlyDirection()
    {
        var (system, world, enemy) = Arena(true); enemy.Position = new(200, 500);
        system.Tick(world.Lord);
        var attack = Assert.Single(world.WaveRuntime!.Events, e => e.Kind == "attack");
        Assert.True(attack.Target.X < attack.Position.X); Assert.Equal(100, enemy.Health);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MissingOrCoincidentTargetKeepsFacing(bool present, bool coincident)
    {
        var (system, world, enemy) = Arena(true);
        if (!present)
        {
            world.Enemies.Clear();
        }
        else if (coincident)
        {
            enemy.Position = world.Lord;
        }
        else
        {
            enemy.Health = 0;
        }

        system.Tick(world.Lord);
        var attack = Assert.Single(world.WaveRuntime!.Events, e => e.Kind == "attack");
        Assert.True(attack.Target.X > attack.Position.X); Assert.Equal(attack.Position.Y, attack.Target.Y);
    }
    [Fact]
    public void NewContractDoesNotRedirectUnconfiguredGear()
    {
        var (system, world, enemy) = Arena(true, false); enemy.Position = new(400, 500);
        system.Tick(world.Lord); Assert.Equal(100, enemy.Health);
    }
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, true)]
    public void V6RoundtripsNewAimWithOptionalPriorContracts(bool feel, bool field, bool rewards, bool reviewField)
    {
        var catalog = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
        var gear = catalog.WaveRuntime!.Gear.Values.First(g => g.Kind == WaveAttackKind.Arc);
        ((Dictionary<string, WaveGearDefinition>)catalog.WaveRuntime.Gear)[gear.Id] = gear with { Aim = "nearest-enemy" };
        var options = new InteractiveOptions(new(30001, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "mixed", ManualCards: true),
            AimMode.Movement, new string('A', 64), feel ? new(FasterLord: true) : null, field ? reviewField ? new(FieldOptions.ReviewVersion, 1200, 3000) : new() : null,
            rewards ? new(true, new(50, 30, 0)) : null, new());
        var session = new InteractiveSession(catalog, options);
        using var stream = new MemoryStream();
        var header = ReplayCodec.Header(options); Assert.Equal(6, header.FormatVersion);
        ReplayCodec.WriteHeader(stream, header);
        for (var tick = 0; tick < 600; tick++)
        {
            if (session.View.Status == RunStatus.AwaitingCard)
            {
                var choice = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.ChooseCard, CardId: session.View.CaptureCards().Cards[0]);
                session.Apply(choice); ReplayCodec.WriteCommand(stream, choice);
            }
            var command = new ReplayCommand(session.NextSequence, tick, ReplayCommandKind.Advance, new(-1000, 0));
            session.Apply(command); ReplayCodec.WriteCommand(stream, command);
        }
        ReplayCodec.WriteEnd(stream, new(session.NextSequence, 600, ReplayEndKind.Quit, session.ComputeStateHash()));
        stream.Position = 0; var replay = ReplayCodec.Read(stream);
        Assert.Equal(options, replay.Header.Options);
        Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(catalog, options.DataHash, replay).StateHash);
        Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), header with { FormatVersion = 5 }));
        Assert.Throws<ArgumentException>(() => new InteractiveSession(catalog, options with { MeleeAim = new(2) }));
    }
    [Fact]
    public void ReviewFieldRadiusIsExplicitBoundedAndRequiresV6()
    {
        var options = new FieldOptions(FieldOptions.ReviewVersion, 1200, 3000);
        var state = new FieldState(options, 30001);
        Assert.Equal(new Position(6200, 5000), state.Spawn(new(5000, 5000), 0));
        Assert.Equal(3000, state.RecycleRadius);
        Assert.Throws<ArgumentException>(() => new FieldState(options with { RecycleRadius = 1000 }, 1));
        Assert.Throws<ArgumentException>(() => new FieldState(new(1, 1200, 3000), 1));
        var session = InteractiveTests.Session();
        Assert.Throws<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), ReplayCodec.Header(session.Options with { Field = options })));
    }
    [Fact]
    public void MeleeAimRuntimeStateParticipatesInHashIndependentlyOfOptions()
    {
        var session = InteractiveTests.Session();
        var legacyHash = session.ComputeStateHash();
        session.State.MeleeAim = new();
        var enabledHash = session.ComputeStateHash();
        Assert.NotEqual(legacyHash, enabledHash);
        session.State.MeleeAim = new(2);
        Assert.NotEqual(enabledHash, session.ComputeStateHash());
        session.State.MeleeAim = null;
        Assert.Equal(legacyHash, session.ComputeStateHash());
    }
    [Fact]
    public void LegacyReplayFixturesRetainExactBytes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "benchmarks")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        foreach (var relative in new[] { "wave-c05/run.ssreplay", "wave-history/a03.ssreplay", "m1-254/dense-approach-candidate2.ssreplay" })
        {
            var expected = File.ReadAllBytes(Path.Combine(root.FullName, "benchmarks", relative));
            using var input = new MemoryStream(expected); var replay = ReplayCodec.Read(input);
            Assert.Null(replay.Header.Options.MeleeAim);
            using var output = new MemoryStream(); ReplayCodec.WriteHeader(output, replay.Header);
            var checkpoint = 0;
            foreach (var command in replay.Commands)
            {
                while (checkpoint < replay.Checkpoints.Count && replay.Checkpoints[checkpoint].AppliedCommands == command.Sequence)
                {
                    ReplayCodec.WriteCheckpoint(output, replay.Checkpoints[checkpoint++]);
                }

                ReplayCodec.WriteCommand(output, command);
            }
            while (checkpoint < replay.Checkpoints.Count)
            {
                ReplayCodec.WriteCheckpoint(output, replay.Checkpoints[checkpoint++]);
            }

            ReplayCodec.WriteEnd(output, replay.End);
            Assert.Equal(expected, output.ToArray());
        }
    }

    [Fact]
    public void ReviewProfileChangesOnlyExplicitGearAimAndGeneratesV6Fixture()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "data");
        var baseline = ContentLoader.Load(directory, profileName: "wave-1a");
        var review = ContentLoader.Load(directory, profileName: "m1-showcase-review");
        Assert.All(baseline.WaveRuntime!.Gear.Values, gear => Assert.Null(gear.Aim));
        Assert.Equal(2, review.WaveRuntime!.Gear.Values.Count(gear => gear.Aim is not null));
        var normalizedGear = review.WaveRuntime.Gear.ToDictionary(pair => pair.Key, pair => pair.Value with { Aim = null });
        var normalized = review with { WaveRuntime = review.WaveRuntime with { Gear = normalizedGear } };
        Assert.Equal(new CanonicalStateHasher().Compute(baseline), new CanonicalStateHasher().Compute(normalized));
        using var stream = new MemoryStream();
        var frame = InteractiveCli.RecordFieldFixture(review, new string('A', 64), 30001, stream, new());
        stream.Position = 0; var replay = ReplayCodec.Read(stream);
        Assert.Equal(6, replay.Header.FormatVersion); Assert.Equal(600, frame.Tick);
        Assert.Equal(replay.End.StateHash, ReplayRunner.Verify(review, new string('A', 64), replay).StateHash);
        Assert.Throws<ArgumentException>(() => InteractiveCli.RecordFieldFixture(baseline, new string('A', 64), 30001, new MemoryStream(), new()));
    }

    [Theory]
    [InlineData(1, "FB4568428B8C2D3EBD1E0C47AB31310A2F7320B6AB7898B961E6A24A8C6F23C2")]
    [InlineData(2, "E89B1265128F0D719A8651EA0DB9B7554B70EB3757C37E3DE9DF6831F7DC06B0")]
    [InlineData(3, "E920859EBA26CD4911FF0570569899ECBEA8C47D5D55230DE2D05D456A8DBDF5")]
    [InlineData(4, "5BA4F78D4E2CDD81223C739F5EBE15D941DEF8D80DECE0853A01EC94F72948BD")]
    [InlineData(5, "89FC0F49E768CBA1B1E96949D38761CF2FAA76EC7BA19487FCEF2B221B98729E")]
    public void LegacyHeadersKeep30a9eb1Bytes(int version, string expectedSha256)
    {
        var run = new RunOptions(30001, "test:hero", "test:estate", "mixed", ManualCards: true, TargetMaterial: version == 2 ? "test:wood" : null);
        var options = new InteractiveOptions(run, AimMode.Movement, new string('A', 64), version >= 3 ? new(FasterLord: true) : null,
            version >= 4 ? new() : null, version == 5 ? new(true, new(50, 30, 0)) : null);
        using var bytes = new MemoryStream(); var header = ReplayCodec.Header(options);
        Assert.Equal(version, header.FormatVersion); ReplayCodec.WriteHeader(bytes, header);
        Assert.Equal(expectedSha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())));
    }

}
