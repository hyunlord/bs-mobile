using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaSaveCodecTests
{
    [Fact]
    public void EncodingIsDeterministicAndRoundtripsPendingRun()
    {
        var catalog = MetaEngineTests.Catalog(); var state = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 42).State;
        byte[] bytes = MetaSaveCodec.Encode(state); var decoded = MetaSaveCodec.Decode(bytes);
        Assert.True(decoded.Valid); Assert.Equal(state.PendingRun, decoded.State!.PendingRun); Assert.Equal(bytes, MetaSaveCodec.Encode(decoded.State));
        var reordered = state with { Vassals = state.Vassals.Reverse().ToDictionary(x => x.Key, x => x.Value), ManorLevels = state.ManorLevels.Reverse().ToDictionary(x => x.Key, x => x.Value) };
        Assert.Equal(bytes, MetaSaveCodec.Encode(reordered));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OlderSchemasMigrateStepwisePreservingProgress(int version)
    {
        var state = MetaEngine.NewGame(MetaEngineTests.Catalog()) with { LastMonotonicSeconds = 100, LastWallSeconds = 1000, CompletedRuns = 7, NextRunSequence = 8 };
        state.Metrics[MetaMetric.Runs.ToString()] = 7;
        var result = MetaSaveCodec.Decode(MetaSaveCodec.EncodeVersion(state, version));
        Assert.True(result.Valid); Assert.Equal(3, result.State!.SchemaVersion); Assert.Equal(7, result.State.CompletedRuns);
        Assert.Equal(version == 1 ? -1 : 100, result.State.LastMonotonicSeconds);
        Assert.True(MetaSaveCodec.Decode(MetaSaveCodec.Encode(result.State)).Valid);
    }
    [Fact]
    public void CorruptionTruncationTrailingBytesAndUnknownVersionAreDistinct()
    {
        var bytes = MetaSaveCodec.Encode(MetaEngine.NewGame(MetaEngineTests.Catalog()));
        for (int i = 0; i < bytes.Length; i++)
        {
            Assert.False(MetaSaveCodec.Decode(bytes[..i]).Valid);
        }

        var corrupt = (byte[])bytes.Clone(); corrupt[30] ^= 1; Assert.Equal("corrupt", MetaSaveCodec.Decode(corrupt).ErrorCode);
        Assert.Equal("corrupt", MetaSaveCodec.Decode([.. bytes, 0]).ErrorCode);
        var unknown = (byte[])bytes.Clone(); unknown[4] = 99; Assert.Equal("unsupported-version", MetaSaveCodec.Decode(unknown).ErrorCode);
    }
    [Fact]
    public void InvalidQuantitiesAndReferencesCannotBeSaved()
    {
        var state = MetaEngine.NewGame(MetaEngineTests.Catalog()); state.Wallet["wood"] = -1;
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.Encode(state));
        state.Wallet["wood"] = 0;
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.Encode(state with { ActiveVassalIds = ["unknown"] }));
    }
    [Fact]
    public void ValidChecksumCannotBypassBoundedStringReads()
    {
        var bytes = MetaSaveCodec.Encode(MetaEngine.NewGame(MetaEngineTests.Catalog()));
        // First payload string follows highest chapter at byte 16.
        BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 16);
        System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
        Assert.Equal("corrupt", MetaSaveCodec.Decode(bytes).ErrorCode);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void MigrationCountsResolvedRunsSeparatelyFromEligibleChallengeRuns(int version)
    {
        var catalog = MetaEngineTests.Catalog();
        var state = MetaEngine.NewGame(catalog) with { NextRunSequence = 10, PendingRun = new(9, "chapter-1", 77), CompletedRuns = 8 };
        state.Metrics[MetaMetric.Runs.ToString()] = 2;
        var migrated = MetaSaveCodec.Decode(MetaSaveCodec.EncodeVersion(state, version));
        Assert.True(migrated.Valid); Assert.Equal(8, migrated.State!.CompletedRuns);
        Assert.Equal(2, migrated.State.Metrics[MetaMetric.Runs.ToString()]);
        Assert.Equal(state.PendingRun, migrated.State.PendingRun);
        Assert.Empty(MetaValidation.ValidateState(catalog, migrated.State));
    }
}
