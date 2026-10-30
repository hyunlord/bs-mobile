using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalRecoveryTests
{
    internal static List<ReplayCommand> Advance(InteractiveSession session, int ticks)
    {
        var commands = new List<ReplayCommand>();
        var until = session.Simulation.World.Tick + ticks;
        while (session.Simulation.World.Tick < until)
        {
            var choose = session.View.Status == RunStatus.AwaitingCard;
            var command = new ReplayCommand(session.NextSequence, session.Simulation.World.Tick,
                choose ? ReplayCommandKind.ChooseCard : ReplayCommandKind.Advance,
                CardId: choose ? session.View.CaptureCards().Cards[0] : null);
            session.Apply(command); commands.Add(command);
        }
        return commands;
    }

    [Fact]
    public void PrefixRestoreReturnsAContinuableSessionWithTheSameSnapshotAndState()
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with { Vassals = VassalRunTests.Options(MetaAbility.Growth, MetaAbility.Allies, MetaAbility.Experience) };
        var session = new InteractiveSession(catalog, options);
        var commands = Advance(session, 90);
        var saved = VassalRecoveryCodec.Capture(session, commands);
        var restored = VassalRecoveryCodec.Restore(catalog, options.DataHash, saved);
        Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(session.State.Vassals!.Encode(), restored.State.Vassals!.Encode());
        Assert.Equal(VassalRunCodec.Encode(session.Options.Vassals!), VassalRunCodec.Encode(restored.Options.Vassals!));
        var continuation = Advance(session, 30);
        foreach (var command in continuation)
        {
            restored.Apply(command);
        }
        Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void CommandReachableRemaindersAndGroupMaximumSurviveRecoveryAndFurtherHarvests()
    {
        var (catalog, _, _, _) = WaveRuntimeTests.Arena(WaveAttackKind.MusterWave);
        var wave = catalog.WaveRuntime!;
        var weaponId = catalog.Tuning.World.Progression.StartingWeapon;
        var seedId = catalog.Heroes[catalog.Tuning.DefaultHero].StartingTool;
        var template = wave.Gear.Values.Single();
        var gear = new Dictionary<string, WaveGearDefinition>
        {
            [weaponId] = template with { Id = weaponId, Kind = WaveAttackKind.MusterWave, Range = 100000, Damage = 0, Capacity = 100 },
            [seedId] = template with { Id = seedId, Kind = WaveAttackKind.SeedFan, Range = 0, Damage = 0, WorkTicks = 5, CooldownTicks = 10, RewardExperience = 3 }
        };
        catalog = catalog with
        {
            WaveRuntime = wave with
            {
                Gear = gear,
                Enemies = wave.Enemies.ToDictionary(p => p.Key, p => p.Value with { FirstSpawnTick = 1 })
            }
        };
        var catalogHash = PortableStateCodec.HashCatalog(catalog);
        var options = new InteractiveOptions(new(1, catalog.Tuning.DefaultHero, catalog.Tuning.DefaultEstate, "manual", ManualCards: true),
            AimMode.Movement, new string('A', 64), Vassals: VassalRunTests.Options(MetaAbility.Growth, MetaAbility.Allies, MetaAbility.Experience));
        var session = new InteractiveSession(catalog, options);
        var commands = new List<ReplayCommand>();
        var state = session.State.Vassals!;
        for (var tick = 0; tick < 40; tick++)
        {
            commands.AddRange(Advance(session, 1));
            if (state.GrainRemainders.Values.Any(value => value > 0) && state.ExperienceRemainders["growth"] > 0 && state.GroupMaxHealth.Count > 0) { break; }
        }
        Assert.Contains(state.GrainRemainders, p => p.Value > 0);
        Assert.True(state.ExperienceRemainders["growth"] > 0);
        Assert.True(session.Simulation.World.HarvestExperience > 0);
        Assert.NotEmpty(state.GroupMaxHealth);
        foreach (var group in session.Simulation.World.WaveRuntime!.Groups)
        {
            Assert.Equal(113, state.GroupMaxHealth[group.Id]);
            Assert.InRange(group.Health, 1, state.GroupMaxHealth[group.Id]);
        }

        var restored = VassalRecoveryCodec.Restore(catalog, options.DataHash, VassalRecoveryCodec.Capture(session, commands));
        Assert.Equal(state.Encode(), restored.State.Vassals!.Encode());
        Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
        var harvestBefore = session.Simulation.World.HarvestExperience;
        var continuation = Advance(session, 30);
        foreach (var command in continuation) { restored.Apply(command); }
        Assert.True(session.Simulation.World.HarvestExperience > harvestBefore);
        Assert.Equal(state.Encode(), restored.State.Vassals.Encode());
        Assert.Equal(session.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(catalogHash, PortableStateCodec.HashCatalog(catalog));
        Assert.Equal(catalogHash, session.InitialCatalogHash);
        Assert.Equal(catalogHash, restored.InitialCatalogHash);
    }

    [Fact]
    public void RecoveryRefusesMissingPrefixWrongCatalogAndExtensionTampering()
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with { Vassals = VassalRunTests.Options(MetaAbility.Experience) };
        var session = new InteractiveSession(catalog, options);
        var commands = Advance(session, 10);
        Assert.Throws<ArgumentException>(() => VassalRecoveryCodec.Capture(session, commands.Skip(1).ToArray()));
        var saved = VassalRecoveryCodec.Capture(session, commands);
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Restore(catalog, new string('B', 64), saved));
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Restore(catalog with { Tuning = catalog.Tuning with { DurationTicks = catalog.Tuning.DurationTicks + 1 } }, options.DataHash, saved));
        var corrupt = (byte[])saved.Clone(); corrupt[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Restore(catalog, options.DataHash, corrupt));
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Restore(catalog, options.DataHash, saved.Concat(new byte[] { 0 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Restore(catalog, options.DataHash, saved[..^1]));
    }

    [Fact]
    public void CaptureRejectsAnUnrecordedMutationInsteadOfBlessingTheNewState()
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with { Vassals = VassalRunTests.Options(MetaAbility.Experience) };
        var session = new InteractiveSession(catalog, options);
        var commands = Advance(session, 10);
        session.State.Vassals!.AwardExperience("enemy", 1);
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Capture(session, commands));
    }
}
