using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaClaimTests
{
    [Fact]
    public void LegacyMigrationPreservesDeliveredBalancesWithoutRetroactiveClaims()
    {
        var catalog = MetaEngineTests.Catalog();
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 7);
        var old = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success).State;
        var restored = MetaSaveCodec.Decode(MetaSaveCodec.EncodeVersion(old, 3)).State!;
        Assert.Equal(old.Wallet, restored.Wallet);
        Assert.Equal(old.CompletedRuns, restored.CompletedRuns);
        Assert.Equal(old.CompletedChallenges, restored.CompletedChallenges);
        Assert.Empty(restored.PendingClaims);
        Assert.Equal(4, restored.SchemaVersion);
        var observed = MetaEngine.AdvanceIdle(catalog, restored, 0, 0, MetaRewardDelivery.PendingClaims).State;
        Assert.Empty(observed.PendingClaims);
    }

    [Fact]
    public void ChallengeClaimDeliversOnceAfterReloadAndDoesNotDelayRunRewards()
    {
        var catalog = MetaEngineTests.Catalog();
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 7);
        var result = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success, MetaRewardDelivery.PendingClaims);
        Assert.Equal(100, result.State.Wallet["wood"]);
        Assert.Equal(catalog.Economy.FragmentsPerClear, result.State.Vassals["vassal-0"].Fragments);
        Assert.Empty(result.State.CompletedChallenges);
        Assert.DoesNotContain("new-weapon", result.State.UnlockedContentIds);
        var claim = Assert.Single(result.State.PendingClaims);
        Assert.Equal(MetaClaimKind.Challenge, claim.Kind);
        Assert.Empty(MetaValidation.ValidateState(catalog, result.State));
        var restored = MetaSaveCodec.Decode(MetaSaveCodec.Encode(result.State)).State!;
        var paid = MetaEngine.Claim(catalog, restored, claim.Sequence);
        Assert.True(paid.Claimed);
        Assert.Equal(101, paid.State.Wallet["wood"]);
        Assert.Contains("new-weapon", paid.State.UnlockedContentIds);
        Assert.Contains("clear-first", paid.State.CompletedChallenges);
        Assert.Empty(paid.State.PendingClaims);
        Assert.Empty(MetaValidation.ValidateState(catalog, paid.State));
        Assert.False(MetaEngine.Claim(catalog, paid.State, claim.Sequence).Claimed);
        Assert.Equal(100, restored.Wallet["wood"]);
    }

    [Fact]
    public void IdleBoxAccumulatesOnlyToCapacityAndCanBeClaimedAgainWithNewIdentity()
    {
        var catalog = MetaEngineTests.Catalog() with { Challenges = [] };
        var state = MetaEngine.AdvanceIdle(catalog, MetaEngine.NewGame(catalog), 0, 100, MetaRewardDelivery.PendingClaims).State;
        for (int time = 10; time <= 200; time += 10)
            state = MetaEngine.AdvanceIdle(catalog, state, time, 100 + time, MetaRewardDelivery.PendingClaims).State;
        Assert.Equal(0, state.Wallet["wood"]);
        var pending = Assert.Single(state.PendingClaims);
        Assert.Equal(MetaClaimKind.Idle, pending.Kind);
        Assert.Equal(60, pending.IdleSeconds);
        Assert.Equal(6, pending.Rewards["wood"]);
        var paid = MetaEngine.Claim(catalog, state, pending.Sequence);
        Assert.True(paid.Claimed);
        Assert.Equal(6, paid.Awarded["wood"]);
        state = MetaEngine.AdvanceIdle(catalog, paid.State, 210, 310, MetaRewardDelivery.PendingClaims).State;
        Assert.True(Assert.Single(state.PendingClaims).Sequence > pending.Sequence);
        Assert.False(MetaEngine.Claim(catalog, state, pending.Sequence).Claimed);
    }

    [Fact]
    public void DefaultDeliveryRemainsAutomaticAndPendingChallengesCannotBeAutoPaid()
    {
        var catalog = MetaEngineTests.Catalog();
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 7);
        var automatic = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success);
        Assert.Empty(automatic.State.PendingClaims);
        Assert.Equal(101, automatic.State.Wallet["wood"]);
        var pending = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success, MetaRewardDelivery.PendingClaims);
        var priority = MetaEngine.SetManorPriority(catalog, pending.State, pending.State.ManorPriority);
        Assert.Single(priority.PendingClaims);
        Assert.Equal(100, priority.Wallet["wood"]);
    }
    [Fact]
    public void PendingClaimEncodingIsCanonicalAndCannotSilentlyDowngrade()
    {
        var catalog = MetaEngineTests.Catalog();
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 7);
        var state = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success, MetaRewardDelivery.PendingClaims).State;
        state = MetaEngine.AdvanceIdle(catalog, state, 0, 100, MetaRewardDelivery.PendingClaims).State;
        state = MetaEngine.AdvanceIdle(catalog, state, 10, 110, MetaRewardDelivery.PendingClaims).State;
        byte[] encoded = MetaSaveCodec.Encode(state);
        Assert.Equal(encoded, MetaSaveCodec.Encode(state with { PendingClaims = state.PendingClaims.Reverse().ToArray() }));
        Assert.Equal(encoded, MetaSaveCodec.Encode(MetaSaveCodec.Decode(encoded).State!));
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.EncodeVersion(state, 3));
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.Encode(state with { NextClaimSequence = 1 }));
        Assert.Throws<InvalidDataException>(() => MetaSaveCodec.Encode(state with { PendingClaims = [state.PendingClaims[0], state.PendingClaims[0]] }));
        Assert.Empty(MetaValidation.ValidateState(catalog, state));
    }

    [Fact]
    public void ClaimUsesWalletCapAndLeavesInputUntouched()
    {
        var catalog = MetaEngineTests.Catalog() with { Challenges = [] };
        var state = MetaEngine.NewGame(catalog);
        foreach (var building in catalog.ManorBuildings) state.ManorLevels[building.Id] = building.MaxLevel;
        state.Wallet["wood"] = 999;
        state = MetaEngine.AdvanceIdle(catalog, state, 0, 100, MetaRewardDelivery.PendingClaims).State;
        state = MetaEngine.AdvanceIdle(catalog, state, 30, 130, MetaRewardDelivery.PendingClaims).State;
        var before = MetaSaveCodec.Encode(state);
        var paid = MetaEngine.Claim(catalog, state, Assert.Single(state.PendingClaims).Sequence);
        Assert.Equal(1, paid.Awarded["wood"]);
        Assert.Equal(2, paid.Overflow["wood"]);
        Assert.Equal(1000, paid.State.Wallet["wood"]);
        Assert.Equal(before, MetaSaveCodec.Encode(state));
        Assert.Empty(MetaValidation.ValidateState(catalog, paid.State));
    }

    [Fact]
    public void RepeatedProgressionDoesNotDuplicatePendingChallenges()
    {
        var catalog = MetaEngineTests.Catalog();
        var run = MetaEngine.BeginRun(catalog, MetaEngine.NewGame(catalog), "chapter-1", 7);
        var state = MetaEngine.SettleRun(catalog, run.State, run.Run, MetaEngineTests.Success, MetaRewardDelivery.PendingClaims).State;
        long next = state.NextClaimSequence;
        for (int i = 0; i < 5; i++)
            state = MetaEngine.SetManorPriority(catalog, state, state.ManorPriority, MetaRewardDelivery.PendingClaims);
        Assert.Single(state.PendingClaims);
        Assert.Equal(next, state.NextClaimSequence);
        var broken = state with { PendingClaims = [state.PendingClaims[0] with { Rewards = new() { ["wood"] = 500 } }] };
        Assert.Contains("claim-challenge", MetaValidation.ValidateState(catalog, broken));
    }

}
