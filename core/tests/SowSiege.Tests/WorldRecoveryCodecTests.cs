using System.Text;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class WorldRecoveryCodecTests
{
    internal static WorldRecoveryDocument Document(bool vassals = false)
    {
        var world = WorldCombatCodecTests.Options();
        var vassalOptions = vassals ? VassalRunTests.Options() : null;
        if (vassalOptions is not null) { world = WorldCombatCodecTests.WithVassals(world, vassalOptions); }
        var snapshot = world.Snapshot;
        var options = new InteractiveOptions(new(snapshot.Provenance.Seed, snapshot.Hero.Id, "test:estate", "manual", ManualCards: true),
            AimMode.Movement, snapshot.Provenance.ContentHash,
            Vassals: vassalOptions, WorldCombat: world);
        var commands = new ReplayCommand[]
        {
            new(0, 0, ReplayCommandKind.SetAimMode, Value: (int)AimMode.NearestEnemy),
            new(1, 0, ReplayCommandKind.Advance),
            new(2, 1, ReplayCommandKind.Advance)
        };
        return new(ReplayCodec.Header(options), new string('A', 64), commands, commands.Length, 2,
            new string('B', 64), new byte[] { 1, 2, 3 }, vassals ? new byte[] { 4, 5, 6 } : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandidateRoundTripPreservesCompletePrefixAndOpaqueRuntimeBlocks(bool vassals)
    {
        var document = Document(vassals);
        var bytes = WorldRecoveryCodec.Encode(document);
        var decoded = WorldRecoveryCodec.Decode(bytes);
        Assert.Equal(bytes, WorldRecoveryCodec.Encode(decoded));
        Assert.Equal(document.Commands, decoded.Commands);
        Assert.Equal(document.NextSequence, decoded.NextSequence);
        Assert.Equal(document.Tick, decoded.Tick);
        Assert.Equal(document.CatalogHash, decoded.CatalogHash);
        Assert.Equal(document.StateHash, decoded.StateHash);
        Assert.Equal(document.WorldRuntimeBytes, decoded.WorldRuntimeBytes);
        Assert.Equal(document.VassalRuntimeBytes, decoded.VassalRuntimeBytes);
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        Assert.Equal("SowSiege-world-recovery-v1", Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32())));
        Assert.Equal(1, reader.ReadInt32());
    }

    [Fact]
    public void EncodeRejectsMissingDuplicateReorderedAndWrongTickPrefixes()
    {
        var document = Document();
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Commands = document.Commands.Skip(1).ToArray() }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Commands = new[] { document.Commands[0], document.Commands[0], document.Commands[2] } }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Commands = document.Commands.Reverse().ToArray() }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Commands = new[] { document.Commands[0] with { Tick = 1 }, document.Commands[1], document.Commands[2] } }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { NextSequence = long.MaxValue }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Tick = int.MaxValue }));
    }

    [Fact]
    public void EncodeRejectsInconsistentRuntimePresenceAndNoncanonicalHashes()
    {
        var document = Document();
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { WorldRuntimeBytes = Array.Empty<byte>() }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { VassalRuntimeBytes = new byte[] { 1 } }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(Document(true) with { VassalRuntimeBytes = null }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { CatalogHash = new string('a', 64) }));
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { StateHash = new string('G', 64) }));
    }

    [Fact]
    public void RecoveryRequiresWorldHeaderAndCannotFallBackToLegacyVassalRecovery()
    {
        var document = Document(true);
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Header = ReplayCodec.Header(document.Header.Options with { WorldCombat = null }) }));
        var bytes = WorldRecoveryCodec.Encode(document);
        Assert.Throws<InvalidDataException>(() => VassalRecoveryCodec.Restore(WaveEstateTests.Catalog(), document.Header.Options.DataHash, bytes));
    }

    [Fact]
    public void DecoderRejectsTruncationAtEveryBoundaryAndTrailingData()
    {
        var bytes = WorldRecoveryCodec.Encode(Document(true));
        var positions = Offsets(bytes);
        foreach (var end in new[] { 0, 1, positions.Schema, positions.HeaderLength, positions.HeaderStart,
            positions.CommandCount, positions.Commands, positions.Sequence, positions.WorldLength, positions.VassalFlag, bytes.Length - 1 })
        {
            Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(bytes[..end]));
        }
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void DecoderRejectsNegativeAndOverflowSizedCountsBeforeAllocation(int value)
    {
        var bytes = WorldRecoveryCodec.Encode(Document());
        var positions = Offsets(bytes);
        foreach (var offset in new[] { 0, positions.HeaderLength, positions.CommandCount, positions.WorldLength })
        {
            var corrupt = (byte[])bytes.Clone(); PutInt32(corrupt, offset, value);
            Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(corrupt));
        }
    }

    [Fact]
    public void DecoderRejectsUnknownSchemaFlagsInvalidUtf8AndHeaderTail()
    {
        var bytes = WorldRecoveryCodec.Encode(Document());
        var positions = Offsets(bytes);
        var schema = (byte[])bytes.Clone(); PutInt32(schema, positions.Schema, 2);
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(schema));
        var flag = (byte[])bytes.Clone(); flag[positions.VassalFlag] = 2;
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(flag));
        var utf8 = (byte[])bytes.Clone(); utf8[sizeof(int)] = 0xFF;
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(utf8));
        var headerLength = BitConverter.ToInt32(bytes, positions.HeaderLength);
        var headerEnd = positions.HeaderStart + headerLength;
        var tail = bytes[..headerEnd].Concat(new byte[] { 0 }).Concat(bytes[headerEnd..]).ToArray();
        PutInt32(tail, positions.HeaderLength, headerLength + 1);
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(tail));
    }

    [Fact]
    public void DecoderRejectsSequenceTickAndFrontierManipulation()
    {
        var bytes = WorldRecoveryCodec.Encode(Document());
        var positions = Offsets(bytes);
        var sequence = (byte[])bytes.Clone(); PutInt64(sequence, positions.Commands + sizeof(int), long.MaxValue);
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(sequence));
        var tick = (byte[])bytes.Clone(); PutInt32(tick, positions.Commands + sizeof(int) + sizeof(long), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(tick));
        var frontier = (byte[])bytes.Clone(); PutInt64(frontier, positions.Sequence, long.MaxValue);
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(frontier));
    }

    [Fact]
    public void EmptyPrefixHasAnExactZeroFrontier()
    {
        var document = Document() with { Commands = Array.Empty<ReplayCommand>(), NextSequence = 0, Tick = 0 };
        var decoded = WorldRecoveryCodec.Decode(WorldRecoveryCodec.Encode(document));
        Assert.Empty(decoded.Commands);
        Assert.Equal(0, decoded.NextSequence);
        Assert.Equal(0, decoded.Tick);
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document with { Tick = 1 }));
    }

    [Theory]
    [InlineData(ReplayCommandKind.SetInvulnerable, 1)]
    [InlineData(ReplayCommandKind.SetSpawnPermille, 0)]
    [InlineData(ReplayCommandKind.GrantLevel, 0)]
    [InlineData(ReplayCommandKind.HostGrantRerollV1, 1)]
    [InlineData(ReplayCommandKind.HostReviveV1, 0)]
    public void RecoveryRefusesReviewAndUnavailableHostCommands(ReplayCommandKind kind, int value)
    {
        var document = Document() with { Commands = new[] { new ReplayCommand(0, 0, kind, Value: value) }, NextSequence = 1, Tick = 0 };
        Assert.Throws<ArgumentException>(() => WorldRecoveryCodec.Encode(document));
        var bytes = WorldRecoveryCodec.Encode(Document());
        var command = Offsets(bytes).Commands;
        PutInt32(bytes, command + sizeof(int) + sizeof(long) + sizeof(int), (int)kind);
        PutInt32(bytes, command + 25, value);
        Assert.Throws<InvalidDataException>(() => WorldRecoveryCodec.Decode(bytes));
    }

    private static (int Schema, int HeaderLength, int HeaderStart, int CommandCount, int Commands, int Sequence, int WorldLength, int VassalFlag) Offsets(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var reader = new BinaryReader(input, Encoding.UTF8);
        SkipBlock(reader); var schema = checked((int)input.Position); reader.ReadInt32();
        var headerLength = checked((int)input.Position); var length = reader.ReadInt32();
        var headerStart = checked((int)input.Position); input.Position += length;
        SkipBlock(reader); var countOffset = checked((int)input.Position); var count = reader.ReadInt32();
        var commands = checked((int)input.Position);
        for (var i = 0; i < count; i++) { ReplayCodec.ReadRecoveryCommand(reader); }
        var sequence = checked((int)input.Position); reader.ReadInt64(); reader.ReadInt32(); SkipBlock(reader);
        var worldLength = checked((int)input.Position); SkipBlock(reader);
        return (schema, headerLength, headerStart, countOffset, commands, sequence, worldLength, checked((int)input.Position));
    }

    private static void SkipBlock(BinaryReader reader)
    {
        var length = reader.ReadInt32(); reader.BaseStream.Position += length;
    }
    private static void PutInt32(byte[] bytes, int offset, int value)
    {
        using var stream = new MemoryStream(bytes); stream.Position = offset;
        using var writer = new BinaryWriter(stream); writer.Write(value);
    }
    private static void PutInt64(byte[] bytes, int offset, long value)
    {
        using var stream = new MemoryStream(bytes); stream.Position = offset;
        using var writer = new BinaryWriter(stream); writer.Write(value);
    }
}
