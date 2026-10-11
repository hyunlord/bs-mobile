using Game.App;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaBonusTests
{
    [Fact]
    public void HostFailedWriteLeavesIdleClaimAndReceiptUnchangedThenRetryPaysOnce()
    {
        string directory = Path.Combine(Path.GetTempPath(), "meta-bonus-" + Guid.NewGuid().ToString("N"));
        try
        {
            long seconds = 100;
            var catalog = MetaEngineTests.Catalog() with { Challenges = [] };
            var source = SowSiege.Sim.ContentLoader.Load(Path.Combine(AppContext.BaseDirectory, "data"), profileName: "wave-1a");
            MetaProgression Open() => new(catalog, source, new string('A', 64), directory, () => seconds, () => seconds + 1000,
                MetaProjectionPolicy.AccountOnlyWave, MetaRewardDelivery.PendingClaims);
            var host = Open();
            seconds += 30;
            host.AdvanceIdle();
            long sequence = Assert.Single(host.State.PendingClaims).Sequence;
            byte[] before = MetaSaveCodec.Encode(host.State);
            string temporary = Path.Combine(directory, "progress.bin.tmp");
            Directory.CreateDirectory(temporary);
            Assert.NotNull(Record.Exception(() => host.ClaimIdleWithBonus(sequence, "atomic-idle", Policy)));
            Assert.Equal(before, MetaSaveCodec.Encode(host.State));
            Directory.Delete(temporary);
            Assert.True(host.ClaimIdleWithBonus(sequence, "atomic-idle", Policy).Granted);
            Assert.False(Open().ClaimIdleWithBonus(sequence, "atomic-idle", Policy).Granted);
            Assert.Equal(6, Open().State.Wallet["wood"]);
            var restarted = Open();
            var run = restarted.BeginRun("chapter-1", 4);
            restarted.SettleRun(MetaEngineTests.Success);
            var reload = Open();
            before = MetaSaveCodec.Encode(reload.State);
            Directory.CreateDirectory(temporary);
            Assert.NotNull(Record.Exception(() => reload.GrantResultBonus(run.Plan.Run.Sequence, "atomic-result", Policy)));
            Assert.Equal(before, MetaSaveCodec.Encode(reload.State));
            Directory.Delete(temporary);
            Assert.True(reload.GrantResultBonus(run.Plan.Run.Sequence, "atomic-result", Policy).Granted);
            Assert.False(Open().GrantResultBonus(run.Plan.Run.Sequence, "atomic-result", Policy).Granted);
            Assert.Equal(2, Open().State.RewardReceipts.Length);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public void ReceiptEncodingIsCanonicalAndRejectsDuplicateSources()
    {
        var catalog = MetaEngineTests.Catalog();
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 1);
        var state = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success).State;
        state = state with
        {
            NextClaimSequence = 2,
            RewardReceipts = [new("z", MetaBonusKind.Result, 1), new("a", MetaBonusKind.Idle, 1)]
        };
        byte[] encoded = MetaSaveCodec.Encode(state);
        Assert.Equal(encoded, MetaSaveCodec.Encode(state with { RewardReceipts = state.RewardReceipts.Reverse().ToArray() }));
        Assert.Equal(encoded, MetaSaveCodec.Encode(MetaSaveCodec.Decode(encoded).State!));
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.Encode(state with { RewardReceipts = [state.RewardReceipts[0], state.RewardReceipts[0] with { ReceiptId = "different" }] }));
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.Encode(state with { RewardReceipts = [new("future", MetaBonusKind.Result, state.NextRunSequence)] }));
        Assert.Contains("result-reward-basis", MetaValidation.ValidateState(catalog, state with { ResultRewardBasis = new(1, new() { ["invented"] = 10 }) }));
    }

    [Fact]
    public void UndefinedOrNegativeBonusConfigurationCannotIssueReceipt()
    {
        var catalog = MetaEngineTests.Catalog();
        var state = MetaEngine.NewGame(catalog);
        Assert.Throws<ArgumentException>(() => MetaEngine.GrantResultBonus(catalog, state, 1, "missing", new(1000, new())));
        Assert.Throws<ArgumentException>(() => MetaEngine.GrantResultBonus(catalog, state, 1, "negative", new(-1, new() { ["wood"] = 1 })));
        Assert.Empty(state.RewardReceipts);
    }

    private static MetaBonusPolicy Policy => new(1000, new() { ["wood"] = 100 });
    [Fact]
    public void IdleBaseAndBonusAreAtomicAndReceiptSurvivesReload()
    {
        var catalog = MetaEngineTests.Catalog() with { Challenges = [] };
        var state = MetaEngine.AdvanceIdle(catalog, MetaEngine.NewGame(catalog), 0, 100, MetaRewardDelivery.PendingClaims).State;
        state = MetaEngine.AdvanceIdle(catalog, state, 30, 130, MetaRewardDelivery.PendingClaims).State;
        long sequence = Assert.Single(state.PendingClaims).Sequence;
        var paid = MetaEngine.ClaimIdleWithBonus(catalog, state, sequence, "idle-receipt", Policy);
        Assert.True(paid.Granted);
        Assert.Equal(6, paid.State.Wallet["wood"]);
        Assert.Empty(paid.State.PendingClaims);
        Assert.Equal(0, state.Wallet["wood"]);
        var reload = MetaSaveCodec.Decode(MetaSaveCodec.Encode(paid.State)).State!;
        Assert.False(MetaEngine.ClaimIdleWithBonus(catalog, reload, sequence, "other-receipt", Policy).Granted);
        Assert.False(MetaEngine.Claim(catalog, reload, sequence).Claimed);
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.EncodeVersion(reload, 4));
    }
    [Fact]
    public void ResultUsesActualAwardedMaterialsAndNeverFragments()
    {
        var catalog = MetaEngineTests.Catalog() with { Challenges = [] };
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 3);
        var settlement = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success, MetaRewardDelivery.PendingClaims);
        var paid = MetaEngine.GrantResultBonus(catalog, settlement.State, run.Run.Sequence, "result-receipt", new(1000, new() { ["wood"] = 7 }));
        Assert.Equal(7, paid.BonusAwarded["wood"]);
        Assert.Equal(settlement.State.Vassals, paid.State.Vassals);
        Assert.False(MetaEngine.GrantResultBonus(catalog, paid.State, run.Run.Sequence, "second-receipt", Policy).Granted);
        Assert.False(MetaEngine.GrantResultBonus(catalog, paid.State, run.Run.Sequence, "result-receipt", Policy).Granted);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void OldSavesNeverInventRewardReceipts(int version)
    {
        var state = MetaEngine.NewGame(MetaEngineTests.Catalog());
        state.Wallet["wood"] = 17;
        var restored = MetaSaveCodec.Decode(MetaSaveCodec.EncodeVersion(state, version)).State!;
        Assert.Equal(5, restored.SchemaVersion);
        Assert.Equal(17, restored.Wallet["wood"]);
        Assert.Empty(restored.RewardReceipts);
        Assert.Null(restored.ResultRewardBasis);
    }
    [Fact]
    public void BonusCannotBypassWalletCapOrBeAddedAfterNormalIdleClaim()
    {
        var catalog = MetaEngineTests.Catalog() with { Challenges = [] };
        var state = MetaEngine.NewGame(catalog);
        foreach (var building in catalog.ManorBuildings)
        {
            state.ManorLevels[building.Id] = building.MaxLevel;
        }

        state.Wallet["wood"] = 996;
        state = MetaEngine.AdvanceIdle(catalog, state, 0, 100, MetaRewardDelivery.PendingClaims).State;
        state = MetaEngine.AdvanceIdle(catalog, state, 30, 130, MetaRewardDelivery.PendingClaims).State;
        long sequence = Assert.Single(state.PendingClaims).Sequence;
        var paid = MetaEngine.ClaimIdleWithBonus(catalog, state, sequence, "cap", Policy);
        Assert.Equal(1000, paid.State.Wallet["wood"]);
        Assert.Equal(1, paid.BonusAwarded["wood"]);
        var normal = MetaEngine.Claim(catalog, state, sequence);
        Assert.False(MetaEngine.ClaimIdleWithBonus(catalog, normal.State, sequence, "late", Policy).Granted);
    }
}
