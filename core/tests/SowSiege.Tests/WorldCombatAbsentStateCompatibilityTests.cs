using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

// The oracle freezes the pre-world option/payload layout at f762eaf5242f777cacb07f7d82e8be32ee9a00c0.
public sealed class WorldCombatAbsentStateCompatibilityTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 10)]
    [InlineData(true, 0)]
    [InlineData(true, 10)]
    public void AbsentWorldKeepsCompleteHistoricalStateBytes(bool extensions, int ticks)
    {
        var session = HistoricalSession(extensions, ticks);
        var actual = CaptureHistoricalState(session);
        var fixture = Path.Combine(FixtureDirectory(), FixtureName(extensions, ticks));
        Assert.True(File.Exists(fixture), $"Missing frozen historical state fixture: {fixture}");
        Assert.Equal(File.ReadAllBytes(fixture), actual);
        Assert.Equal(session.ComputeStateHash(), Convert.ToHexString(SHA256.HashData(actual)));
    }

    internal static InteractiveSession HistoricalSession(bool extensions, int ticks)
    {
        var catalog = WaveEstateTests.Catalog();
        var options = WaveEstateTests.Options(catalog) with
        {
            Field = extensions ? new() : null,
            Estate = extensions ? WaveEstateTests.Options(catalog).Estate : null,
            MeleeAim = extensions ? new() : null,
            Vassals = extensions ? VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience) : null
        };
        var session = new InteractiveSession(catalog, options);
        VassalRecoveryTests.Advance(session, ticks);
        return session;
    }

    internal static string FixtureName(bool extensions, int ticks) => $"{(extensions ? "estate-melee-vassals" : "plain")}-tick-{ticks}.bin";

    internal static string FixtureDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
        {
            root = root.Parent;
        }
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "core", "tests", "SowSiege.Tests", "Fixtures", "world-absent-state");
    }

    // Reflection exposes bytes without adding a product API or a fixture-generation path to the test.
    // The root field order and version are frozen; nested encoders are checked against the saved bytes.
    internal static byte[] CaptureHistoricalState(InteractiveSession session)
    {
        using var stream = new MemoryStream();
        var codec = CreateCodec(stream);
        WriteStateValue(codec, "SowSiege-full-state"); WriteStateValue(codec, 1);
        WriteStateValue(codec, session.Options); WriteStateValue(codec, session.InitialCatalogHash);
        WriteStateValue(codec, session.Catalog); WriteStateValue(codec, session.NextSequence);
        WriteStateValue(codec, session.Simulation.World); WriteStateValue(codec, session.State);
        WriteStateValue(codec, session.Simulation.RandomState.Draws);
        WriteStateValue(codec, session.Simulation.RandomState.Portable);
        FlushCodec(codec);
        var bytes = stream.ToArray();
        Assert.Equal(session.ComputeStateHash(), Convert.ToHexString(SHA256.HashData(bytes)));
        return bytes;
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void AbsentWorldAddsNoBytesToHistoricalStateOptions(bool estate, bool melee, bool vassals)
    {
        var options = new InteractiveOptions(
            new(30001, "test:hero", "test:estate", "manual", ManualCards: true),
            AimMode.Movement, new string('A', 64),
            MeleeAim: melee ? new() : null,
            Estate: estate ? new(1, "test:loadout", new string('B', 64)) : null,
            Vassals: vassals ? VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience) : null);
        using var expected = new MemoryStream();
        using var writer = new BinaryWriter(expected, Encoding.UTF8, true);
        writer.Write(true); // InteractiveOptions presence
        writer.Write(true); // RunOptions presence
        writer.Write(30001);
        StateText(writer, "test:hero"); StateText(writer, "test:estate"); StateText(writer, "manual");
        StateText(writer, options.Run.PeopleRule); StateText(writer, options.Run.Scenario);
        writer.Write(true); StateText(writer, options.Run.Movement);
        writer.Write((int)AimMode.Movement); StateText(writer, new string('A', 64));
        if (melee)
        {
            StateText(writer, "melee-aim-options-v1"); writer.Write(1);
        }
        if (estate)
        {
            StateText(writer, "estate-run-options-v1"); writer.Write(1);
            StateText(writer, "test:loadout"); StateText(writer, new string('B', 64));
        }
        if (vassals)
        {
            StateText(writer, "vassal-run-options-v1");
            var payload = HistoricalVassalPayload(options.Vassals!);
            writer.Write(payload.Length); writer.Write(payload);
        }
        writer.Flush();
        Assert.Equal(expected.ToArray(), StateOptionBytes(options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalVassalPayloadKeepsEveryByteAndFieldOrder(bool populated)
    {
        var options = populated
            ? VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience)
            : VassalRunTests.Options();
        var expected = HistoricalVassalPayload(options);
        Assert.Equal(expected, VassalRunCodec.Encode(options));
        Assert.Equal(expected, VassalRunCodec.Encode(VassalRunCodec.Decode(expected)));
    }

    private static byte[] StateOptionBytes(InteractiveOptions options)
    {
        using var stream = new MemoryStream();
        var codec = CreateCodec(stream);
        WriteStateValue(codec, options);
        FlushCodec(codec);
        return stream.ToArray();
    }

    private static object CreateCodec(Stream stream)
    {
        var type = typeof(PortableStateCodec);
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(Stream)], null);
        Assert.NotNull(constructor);
        return constructor.Invoke([stream]);
    }

    private static void WriteStateValue<T>(object codec, T value)
    {
        var write = typeof(PortableStateCodec).GetMethod("Write", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(T)], null);
        Assert.NotNull(write);
        write.Invoke(codec, [value]);
    }

    private static void FlushCodec(object codec)
    {
        var field = typeof(PortableStateCodec).GetField("writer", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        Assert.IsType<BinaryWriter>(field.GetValue(codec)).Flush();
    }

    private static byte[] HistoricalVassalPayload(VassalRunOptions options)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), true);
        PayloadText(writer, "vassal-run-v1"); writer.Write(1); writer.Write(1u);
        var rules = options.Rules;
        PayloadText(writer, rules.Id); writer.Write(rules.Version); PayloadText(writer, rules.ContentHash);
        var caps = rules.Caps;
        writer.Write(caps.BaseSlots); writer.Write(caps.MaximumSlots); writer.Write(caps.BarracksLevelsPerSlot);
        writer.Write(caps.BaseLevelCap); writer.Write(caps.LevelCapPerForgeLevel); writer.Write(caps.MaximumLevel); writer.Write(caps.MaximumRank);
        writer.Write(rules.Vassals.Count);
        foreach (var definition in rules.Vassals)
        {
            PayloadText(writer, definition.Id); writer.Write((int)definition.Ability);
        }
        writer.Write(rules.Effects.Count);
        foreach (var rule in rules.Effects)
        {
            writer.Write((int)rule.Ability); writer.Write(rule.Base); writer.Write(rule.PerLevel); writer.Write(rule.PerRank); writer.Write(rule.Cap);
        }
        var snapshot = options.Snapshot;
        PayloadText(writer, snapshot.ContentHash); writer.Write(snapshot.ArithmeticVersion); writer.Write(snapshot.RemainderVersion);
        writer.Write(snapshot.EffectiveSlots); writer.Write(snapshot.EffectiveLevelCap);
        writer.Write(snapshot.ForgeLevel); writer.Write(snapshot.BarracksLevel); PayloadText(writer, snapshot.BasisHash);
        writer.Write(snapshot.Roster.Count);
        foreach (var entry in snapshot.Roster)
        {
            PayloadText(writer, entry.Id); writer.Write(entry.Level); writer.Write(entry.Rank); writer.Write(entry.Eligible);
        }
        writer.Write(snapshot.Effects.Count);
        foreach (var effect in snapshot.Effects)
        {
            writer.Write((int)effect.Ability); writer.Write(effect.Raw); writer.Write(effect.Bonus);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void StateText(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null) { PayloadText(writer, value); }
    }

    private static void PayloadText(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}
