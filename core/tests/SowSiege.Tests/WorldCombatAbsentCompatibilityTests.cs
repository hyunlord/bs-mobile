using System.Text;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

// ADR0083: the independent envelope oracle describes the pre-world wire at f762eaf5242f777cacb07f7d82e8be32ee9a00c0.
// These are codec-only vectors, not executed battles; their checkpoint/end hashes are deliberately synthetic.
public sealed class WorldCombatAbsentCompatibilityTests
{
    public static IEnumerable<object[]> HistoricalCases()
    {
        for (var format = 1; format <= 6; format++)
        {
            yield return new object[] { format, false, false, false };
        }
        yield return new object[] { 7, true, false, false };
        yield return new object[] { 7, true, true, false };
        foreach (var estate in new[] { false, true })
        {
            foreach (var melee in new[] { false, true })
            {
                foreach (var populated in new[] { false, true })
                {
                    yield return new object[] { 8, estate, melee, populated };
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(HistoricalCases))]
    public void AbsentWorldKeepsHistoricalHeaderAndCompleteReplayBytes(int format, bool estate, bool melee, bool populated)
    {
        var options = Options(format, estate, melee, populated);
        var header = ReplayCodec.Header(options);
        Assert.Equal(format, header.FormatVersion);
        Assert.Equal(1, header.RulesVersion);
        Assert.Equal(1, header.StateCodecVersion);
        Assert.Equal("dotnet-seeded-subtractive-v1", header.RngAlgorithm);

        using var expected = new MemoryStream();
        using var oracle = new BinaryWriter(expected, new UTF8Encoding(false, true), true);
        WriteHistoricalHeader(oracle, options, format);
        oracle.Flush();
        using var actual = new MemoryStream();
        ReplayCodec.WriteHeader(actual, header);
        Assert.Equal(expected.ToArray(), actual.ToArray());

        var checkpoint = new ReplayCheckpoint(0, 0, new string('B', 64));
        ReplayCodec.WriteCheckpoint(actual, checkpoint);
        oracle.Write(2); oracle.Write(0L); oracle.Write(0); Text(oracle, new string('B', 64));

        var commands = new ReplayCommand[]
        {
            new(0, 0, ReplayCommandKind.Advance, new(-1000, 1000)),
            new(1, 1, ReplayCommandKind.ChooseCard, CardId: "test:card_성장"),
            new(2, 1, ReplayCommandKind.SetAimMode, Value: 1)
        };
        foreach (var command in commands) { ReplayCodec.WriteCommand(actual, command); }
        WriteHistoricalCommand(oracle, 0, 0, 0, -1000, 1000, null, 0);
        WriteHistoricalCommand(oracle, 1, 1, 1, 0, 0, "test:card_성장", 0);
        WriteHistoricalCommand(oracle, 2, 1, 5, 0, 0, null, 1);

        var finalCheckpoint = new ReplayCheckpoint(3, 1, new string('C', 64));
        ReplayCodec.WriteCheckpoint(actual, finalCheckpoint);
        oracle.Write(2); oracle.Write(3L); oracle.Write(1); Text(oracle, new string('C', 64));
        var end = new ReplayEnd(3, 1, ReplayEndKind.Quit, new string('D', 64));
        ReplayCodec.WriteEnd(actual, end);
        oracle.Write(3); oracle.Write(3L); oracle.Write(1); oracle.Write(2); Text(oracle, new string('D', 64));
        oracle.Flush();
        Assert.Equal(expected.ToArray(), actual.ToArray());

        actual.Position = 0;
        var decoded = ReplayCodec.Read(actual);
        Assert.Equal(format, decoded.Header.FormatVersion);
        Assert.Equal(commands, decoded.Commands);
        Assert.Equal(new[] { checkpoint, finalCheckpoint }, decoded.Checkpoints);
        Assert.Equal(end, decoded.End);
        using var roundtrip = new MemoryStream();
        ReplayCodec.WriteHeader(roundtrip, decoded.Header);
        ReplayCodec.WriteCheckpoint(roundtrip, decoded.Checkpoints[0]);
        foreach (var command in decoded.Commands) { ReplayCodec.WriteCommand(roundtrip, command); }
        ReplayCodec.WriteCheckpoint(roundtrip, decoded.Checkpoints[1]);
        ReplayCodec.WriteEnd(roundtrip, decoded.End);
        Assert.Equal(expected.ToArray(), roundtrip.ToArray());
    }

    private static InteractiveOptions Options(int format, bool estate, bool melee, bool populated)
    {
        var options = new InteractiveOptions(
            new(30001, "core:frontier_knight", "core:farm", "manual", PeopleRule: "B", ManualCards: true,
                TargetMaterial: format == 1 ? null : "test:material"),
            AimMode.Movement, new string('A', 64));
        return options with
        {
            Feel = format >= 3 ? new(true, false, true) : null,
            Field = format >= 4 ? new() : null,
            HostRewards = format >= 5 ? new(true, new(17, 23, HostRevivePreservation.PreserveWorldAndCards)) : null,
            MeleeAim = format == 6 || melee ? new() : null,
            Estate = estate ? new(1, "test:loadout", new string('E', 64)) : null,
            Vassals = format == 8
                ? populated ? VassalRunTests.Options(MetaAbility.Attack, MetaAbility.Health, MetaAbility.Experience) : VassalRunTests.Options()
                : null
        };
    }

    private static void WriteHistoricalHeader(BinaryWriter writer, InteractiveOptions options, int format)
    {
        Text(writer, "SowSiege-replay"); writer.Write(format); writer.Write(1); writer.Write(1);
        Text(writer, "dotnet-seeded-subtractive-v1");
        var run = options.Run;
        writer.Write(run.Seed); Text(writer, run.HeroId); Text(writer, run.EstateId); Text(writer, run.Policy);
        Text(writer, run.PeopleRule); Text(writer, run.Scenario); writer.Write(run.ManualCards); Text(writer, run.Movement);
        writer.Write((int)options.InitialAimMode); Text(writer, options.DataHash);
        if (format == 1) { return; }
        if (format == 2) { Text(writer, run.TargetMaterial); return; }
        if (format == 8)
        {
            writer.Write(1); Text(writer, run.TargetMaterial);
            writer.Write(options.Feel is not null);
            if (options.Feel is not null) { writer.Write(5); }
            WriteOptionalField(writer, options);
            writer.Write(options.HostRewards is not null);
            if (options.HostRewards is not null) { WriteHost(writer, options.HostRewards); }
            writer.Write(options.Estate is not null);
            if (options.Estate is not null) { WriteEstate(writer, options.Estate); }
            writer.Write(options.MeleeAim is not null);
            if (options.MeleeAim is not null) { writer.Write(1); }
            // Envelope coverage only: the existing payload encoder is reused, so a separate frozen vassal payload pin is still required.
            var payload = VassalRunCodec.Encode(options.Vassals!);
            writer.Write(payload.Length); writer.Write(payload);
            return;
        }

        Text(writer, run.TargetMaterial); writer.Write(1); writer.Write(5);
        if (format == 3) { return; }
        if (format == 4) { writer.Write(1); writer.Write(1); return; }
        WriteOptionalField(writer, options);
        if (format == 5) { WriteHost(writer, options.HostRewards!); return; }
        writer.Write(options.HostRewards is not null);
        if (options.HostRewards is not null) { WriteHost(writer, options.HostRewards); }
        if (format == 6) { writer.Write(1); return; }
        writer.Write(options.MeleeAim is null ? 1 : 2);
        WriteEstate(writer, options.Estate!);
        if (options.MeleeAim is not null) { writer.Write(1); }
    }

    private static void WriteOptionalField(BinaryWriter writer, InteractiveOptions options)
    {
        writer.Write(options.Field is not null);
        if (options.Field is not null) { writer.Write(1); }
    }

    private static void WriteHost(BinaryWriter writer, HostRewardRunOptions host)
    {
        writer.Write(1); writer.Write(host.AllowRerollGrant); writer.Write(host.Revive is not null);
        if (host.Revive is { } revive)
        {
            writer.Write(revive.RestoreHealth); writer.Write(revive.InvulnerabilityTicks); writer.Write(0);
        }
    }

    private static void WriteEstate(BinaryWriter writer, EstateRunOptions estate)
    {
        writer.Write(1); Text(writer, estate.LoadoutId); Text(writer, estate.LoadoutCatalogHash);
    }

    private static void WriteHistoricalCommand(BinaryWriter writer, long sequence, int tick, int kind,
        short x, short y, string? card, int value)
    {
        writer.Write(1); writer.Write(sequence); writer.Write(tick); writer.Write(kind);
        writer.Write(x); writer.Write(y); Text(writer, card); writer.Write(value);
    }

    private static void Text(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is null) { return; }
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }
}
