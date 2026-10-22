using System.Security.Cryptography;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

// ADR0064 supplement (2026-10-22): format 7 carries melee aim in estate header schema 2. Schema 1 (melee aim off) keeps its bytes.
public sealed class EstateReplayMeleeAimTests
{
    private static readonly string DataHash = new('A', 64);

    private static InteractiveOptions Fixed(MeleeAimOptions? melee) =>
        new(new(30001, "core:frontier_knight", "core:farm", "manual", ManualCards: true), AimMode.Movement, DataHash,
            Field: new FieldOptions(), MeleeAim: melee, Estate: new(WaveEstateLight.Version, "L1", new string('C', 64)));

    private static byte[] Record(InteractiveOptions options)
    {
        using var bytes = new MemoryStream();
        ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options));
        ReplayCodec.WriteCommand(bytes, new ReplayCommand(0, 0, ReplayCommandKind.Advance));
        ReplayCodec.WriteEnd(bytes, new(1, 1, ReplayEndKind.Quit, new string('D', 64)));
        return bytes.ToArray();
    }

    [Fact]
    public void FormatSevenWithoutMeleeAimKeepsItsBytes()
    {
        var bytes = Record(Fixed(null));
        Assert.Equal("6542931CF659A58D651DD08DE2158908D2462C4AFE4C15DFDA370B1FF6CCB1A0", Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Null(ReplayCodec.Read(new MemoryStream(bytes)).Header.Options.MeleeAim);
    }

    [Fact]
    public void FormatSevenRoundTripsWithMeleeAimOnAndOff()
    {
        var c = WaveEstateTests.Catalog();
        foreach (var melee in new MeleeAimOptions?[] { null, new() })
        {
            var session = new InteractiveSession(c, WaveEstateTests.Options(c) with { MeleeAim = melee });
            var commands = new List<ReplayCommand>();
            void Apply(ReplayCommandKind kind, string? card = null)
            {
                var command = new ReplayCommand(session.NextSequence, session.Simulation.World.Tick, kind, CardId: card);
                session.Apply(command); commands.Add(command);
            }
            for (var i = 0; i < 300;)
            {
                if (session.View.Status == RunStatus.AwaitingCard) { Apply(ReplayCommandKind.ChooseCard, session.View.CaptureCards().Cards[0]); continue; }
                Apply(ReplayCommandKind.Advance); i++;
            }
            using var bytes = new MemoryStream();
            ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(session.Options));
            foreach (var command in commands) { ReplayCodec.WriteCommand(bytes, command); }
            var summary = session.GetSummary();
            ReplayCodec.WriteEnd(bytes, new(session.NextSequence, summary.Tick, ReplayEndKind.Quit, summary.StateHash));
            bytes.Position = 0;
            var replay = ReplayCodec.Read(bytes);
            Assert.Equal(ReplayCodec.EstateVersion, replay.Header.FormatVersion);
            Assert.Equal(melee, replay.Header.Options.MeleeAim);
            Assert.Equal("L1", replay.Header.Options.Estate!.LoadoutId);
            Assert.Equal(session.ComputeStateHash(), ReplayRunner.Verify(c, DataHash, replay).StateHash);
        }
    }

    [Fact]
    public void ValidateHeaderAllowsMeleeAimInFormatSevenOnly()
    {
        var on = ReplayCodec.Header(Fixed(new()));
        Assert.Equal(ReplayCodec.EstateVersion, on.FormatVersion);
        ReplayCodec.WriteHeader(new MemoryStream(), on);
        var withoutEstate = Fixed(new()) with { Estate = null };
        Assert.Equal(ReplayCodec.MeleeAimVersion, ReplayCodec.Header(withoutEstate).FormatVersion);
        Assert.ThrowsAny<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), on with { FormatVersion = ReplayCodec.MeleeAimVersion }));
        Assert.ThrowsAny<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), ReplayCodec.Header(withoutEstate) with { FormatVersion = ReplayCodec.EstateVersion }));
        Assert.ThrowsAny<ArgumentException>(() => ReplayCodec.WriteHeader(new MemoryStream(), on with { Options = on.Options with { MeleeAim = new(2) } }));

        // A schema-2 header (melee aim on) differs from schema 1 only by the schema number and the appended melee aim version.
        var off = Record(Fixed(null)); var with = Record(Fixed(new()));
        Assert.Equal(off.Length + sizeof(int), with.Length);
        Assert.NotEqual(off, with);
        // An unknown estate header schema is refused.
        var corrupt = (byte[])with.Clone();
        var at = IndexOfSchema(with, 2); corrupt[at] = 3;
        Assert.ThrowsAny<Exception>(() => ReplayCodec.Read(new MemoryStream(corrupt)));
    }

    // The estate block starts with the schema int right before the estate version int (WaveEstateLight.Version) and the loadout text.
    private static int IndexOfSchema(byte[] bytes, int schema)
    {
        var pattern = BitConverter.GetBytes(schema).Concat(BitConverter.GetBytes(WaveEstateLight.Version)).ToArray();
        for (var i = bytes.Length - pattern.Length; i >= 0; i--)
        {
            if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern)) { return i; }
        }
        throw new InvalidOperationException("Estate schema not found.");
    }
}
