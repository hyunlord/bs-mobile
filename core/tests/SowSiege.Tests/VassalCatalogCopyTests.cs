using System.Collections;
using System.Reflection;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalCatalogCopyTests
{
    [Theory]
    [InlineData("wave-1a")]
    [InlineData("first-playable")]
    [InlineData("s4b-02")]
    public void ExplicitCopyKeepsCatalogHashAndEveryCollectionIndependent(string profile)
    {
        var source = ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profile != "wave-1a", profile);
        var frozen = VassalCatalogCopy.Freeze(source);
        Assert.Equal(PortableStateCodec.HashCatalog(source), PortableStateCodec.HashCatalog(frozen));
        AssertIndependentCollections(source, frozen, "catalog");
    }

    [Fact]
    public void WaveOptionalProgramsLevelsUnlocksAndEstateSlotsAreIndependent()
    {
        var source = WaveEstateTests.Catalog();
        var wave = source.WaveRuntime!;
        var programs = (wave.Programs ?? WaveLegacyCompiler.Compile(wave)).ToDictionary(pair => pair.Key,
            pair => new WavePrimitiveProgram(pair.Value.Params.ToDictionary(unit => unit.Key,
                unit => (IReadOnlyDictionary<string, string>)unit.Value.ToDictionary(parameter => parameter.Key, parameter => parameter.Value))));
        var gear = wave.Gear.ToDictionary(pair => pair.Key, pair => pair.Value);
        var gearId = gear.Keys.First(id => programs[id].Has("unit:attack-shape"));
        var originalShape = programs[gearId].Value("unit:attack-shape", "shape");
        gear[gearId] = gear[gearId] with { Levels = new[] { new WaveGearLevel(17, 800, 30, 0, 1, 30, 0) } };
        var evolutions = wave.Evolutions.ToDictionary(pair => pair.Key, pair => pair.Value);
        var evolutionId = evolutions.Keys.First();
        evolutions[evolutionId] = evolutions[evolutionId] with
        {
            Unlock = new(new[] { new WaveUnlockInput(gearId, 2) }, new[] { new WaveUnlockEvent("test:event", 2) })
        };
        source = source with
        {
            WaveRuntime = wave with
            {
                Gear = gear,
                Evolutions = evolutions,
                Programs = programs,
                CardOffer = new(Cards: new Dictionary<string, WaveCardOfferCard> { [gearId] = new(2, 3) })
            }
        };
        var frozen = VassalCatalogCopy.Freeze(source);
        var hash = PortableStateCodec.HashCatalog(frozen);
        Assert.Equal(PortableStateCodec.HashCatalog(source), hash);
        AssertIndependentCollections(source, frozen, "catalog");
        gear[gearId].Levels![0] = gear[gearId].Levels![0] with { Damage = 999 };
        evolutions[evolutionId].Unlock!.Inputs[0] = new(gearId, 7);
        ((Dictionary<string, string>)programs[gearId].Params["unit:attack-shape"])["shape"] = originalShape == "orbit" ? "melee-fan" : "orbit";
        var slot = source.WaveRuntime!.EstateLight!.Loadouts["L1"][0];
        source.WaveRuntime.EstateLight.Loadouts["L1"][0] = slot with { MaxRadius = slot.MaxRadius + 100 };
        Assert.Equal(hash, PortableStateCodec.HashCatalog(frozen));
        Assert.NotEqual(hash, PortableStateCodec.HashCatalog(source));
        Assert.Equal(originalShape, frozen.WaveRuntime!.Programs![gearId].Value("unit:attack-shape", "shape"));
        Assert.Equal(slot, frozen.WaveRuntime.EstateLight!.Loadouts["L1"][0]);
    }

    [Fact]
    public void MutatingCallerCatalogDoesNotChangeFrozenExecution()
    {
        var source = WaveEstateTests.Wave();
        var frozen = VassalCatalogCopy.Freeze(source);
        var control = VassalCatalogCopy.Freeze(source);
        var options = new InteractiveOptions(new(30001, source.Tuning.DefaultHero, source.Tuning.DefaultEstate,
            "manual", ManualCards: true), AimMode.Movement, new string('A', 64));
        var session = new InteractiveSession(frozen, options);
        var expected = new InteractiveSession(control, options);
        source.Tuning.World.Seasons[0] = source.Tuning.World.Seasons[0] with { SpawnMultiplier = 1 };
        source.Tuning.World.Farms.StageTicks[0] = 1;
        source.Tuning.Policies.Clear();
        ((IDictionary<string, EnemyDefinition>)source.Enemies).Clear();
        ((IDictionary<string, WaveGearDefinition>)source.WaveRuntime!.Gear).Clear();
        for (var tick = 0; tick < 30; tick++)
        {
            var command = new ReplayCommand(tick, tick, ReplayCommandKind.Advance, new(1000, 0));
            session.Apply(command); expected.Apply(command);
            Assert.Equal(expected.ComputeStateHash(), session.ComputeStateHash());
        }
    }

    [Fact]
    public void OptInSessionOwnsCatalogAndDoesNotExposeMutableVassalSnapshot()
    {
        var source = WaveEstateTests.Wave();
        var options = new InteractiveOptions(new(30001, source.Tuning.DefaultHero, source.Tuning.DefaultEstate,
            "manual", ManualCards: true), AimMode.Movement, new string('A', 64),
            Vassals: VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience));
        var expected = new InteractiveSession(VassalCatalogCopy.Freeze(source), options);
        var session = new InteractiveSession(source, options);
        var initialHash = session.ComputeStateHash();
        var returned = Assert.IsType<VassalRunOptions>(session.VassalSnapshot);
        var snapshotBytes = VassalRunCodec.Encode(returned);
        Assert.Equal(expected.ComputeStateHash(), initialHash);

        source.Tuning.World.Seasons[0] = source.Tuning.World.Seasons[0] with { SpawnMultiplier = 1 };
        source.Tuning.World.Farms.StageTicks[0] = 1;
        source.Tuning.Policies.Clear();
        ((IDictionary<string, EnemyDefinition>)source.Enemies).Clear();
        ((IDictionary<string, WaveGearDefinition>)source.WaveRuntime!.Gear).Clear();
        Assert.Throws<NotSupportedException>(() =>
            ((IList<VassalRosterEntry>)returned.Snapshot.Roster)[0] = returned.Snapshot.Roster[0] with { Level = 1 });
        Assert.Throws<NotSupportedException>(() =>
            ((IList<VassalEffect>)returned.Snapshot.Effects)[0] = returned.Snapshot.Effects[0] with { Bonus = 0 });
        Assert.Throws<NotSupportedException>(() =>
            ((IList<VassalDefinition>)returned.Rules.Vassals)[0] = returned.Rules.Vassals[0] with { Id = "changed" });
        Assert.Throws<NotSupportedException>(() =>
            ((IList<VassalEffectRule>)returned.Rules.Effects)[0] = returned.Rules.Effects[0] with { Base = 999 });

        Assert.Equal(initialHash, session.ComputeStateHash());
        Assert.Equal(snapshotBytes, VassalRunCodec.Encode(session.VassalSnapshot!));
        var commands = VassalRecoveryTests.Advance(expected, 120);
        var control = new InteractiveSession(expected.Catalog, options);
        foreach (var command in commands)
        {
            session.Apply(command);
            control.Apply(command);
            Assert.Equal(control.ComputeStateHash(), session.ComputeStateHash());
        }
        Assert.Equal(expected.ComputeStateHash(), session.ComputeStateHash());
        Assert.Equal(snapshotBytes, VassalRunCodec.Encode(session.VassalSnapshot!));
    }

    // Reflection is confined to this regression test so newly added nested collections cannot silently alias.
    private static void AssertIndependentCollections(object? source, object? copy, string path)
    {
        if (source is null) { Assert.Null(copy); return; }
        Assert.NotNull(copy);
        var type = source.GetType();
        if (source is string || type.IsPrimitive || type.IsEnum || source is decimal) { Assert.Equal(source, copy); return; }
        if (source is IEnumerable collection)
        {
            var items = collection.Cast<object?>().ToArray();
            var copiedItems = Assert.IsAssignableFrom<IEnumerable>(copy).Cast<object?>().ToArray();
            Assert.Equal(items.Length, copiedItems.Length);
            // Sharing a zero-length array cannot expose a mutable element. Dictionaries must still be independent.
            if (source is not Array || items.Length != 0) { Assert.False(ReferenceEquals(source, copy), path); }
            for (var index = 0; index < items.Length; index++) { AssertIndependentCollections(items[index], copiedItems[index], path + "[" + index + "]"); }
            return;
        }
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            AssertIndependentCollections(property.GetValue(source), property.GetValue(copy), path + "." + property.Name);
        }
    }
}
