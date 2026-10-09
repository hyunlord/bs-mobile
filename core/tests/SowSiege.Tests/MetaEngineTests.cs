using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class MetaEngineTests
{
    internal static MetaCatalog Catalog()
    {
        Dictionary<string, int> Money(int value) => new() { ["wood"] = value };
        return new(1, [new("wood", "Wood", 1000, "wood")],
            Enumerable.Range(1, 3).Select(i => new MetaChapter($"chapter-{i}", i, "Chapter", "Test", 1000, 1000, 1, 10, 1, 1000, 1000, 1000, ["enemy"], "boss", [], Money(100))).ToArray(),
            Enum.GetValues<ManorEffect>().Select((e, i) => new MetaManorBuilding($"building-{i}", "Building", "Test", "art", e, 3, Money(200), Money(100))).ToArray(),
            Enum.GetValues<MetaAbility>().Select((a, i) => new MetaVassal($"vassal-{i}", "Vassal", "Test", "art", a, true, 10, 5, 20, 20, 2, 30, [5, 10], Money(2), Money(1))).ToArray(),
            [new("clear-first", "Clear", MetaMetric.Clears, 1, 0, ["new-weapon"], [], Money(1))], ["old-weapon"],
            new(500, 250, 0, 10, 10, 60, 30, 120, Money(1), 2, 5, 5, 1, 1, 6, 3, 1, [new("wood", 1, 1, 1, 1, 1, 10)]));
    }
    internal static MetaRunFacts Success => new(100, 100, true, true, false, 10, 5, 5, 5, 50, 100);

    [Fact]
    public void SettlementIsExactlyOnceAndDoesNotMutateInput()
    {
        var catalog = Catalog(); var initial = MetaEngine.NewGame(catalog); var before = MetaSaveCodec.Encode(initial);
        var plan = MetaEngine.BeginRun(catalog, initial, "chapter-1", 7);
        var result = MetaEngine.SettleRun(catalog, plan.State, plan.Run, Success);
        Assert.True(result.Cleared); Assert.Equal(1, result.State.HighestClearedChapter);
        Assert.Equal(100, result.Awarded["wood"]); Assert.Contains("new-weapon", result.State.UnlockedContentIds);
        Assert.Throws<InvalidOperationException>(() => MetaEngine.SettleRun(catalog, result.State, plan.Run, Success));
        Assert.Throws<InvalidOperationException>(() => MetaEngine.SettleRun(catalog, plan.State, plan.Run with { Seed = 8 }, Success));
        Assert.Equal(before, MetaSaveCodec.Encode(initial)); Assert.NotNull(plan.State.PendingRun);
    }
    [Fact]
    public void ClearRequiresDurationBossAndSurvivalAndUnlocksOnlyNextChapter()
    {
        var catalog = Catalog(); var state = MetaEngine.NewGame(catalog);
        Assert.Throws<InvalidOperationException>(() => MetaEngine.BeginRun(catalog, state, "chapter-3", 1));
        foreach (var facts in new[] { Success with { Tick = 99 }, Success with { BossDefeated = false }, Success with { Survived = false }, Success with { Abandoned = true } })
        {
            var run = MetaEngine.BeginRun(catalog, state, "chapter-1", 1); var result = MetaEngine.SettleRun(catalog, run.State, run.Run, facts);
            Assert.False(result.Cleared); Assert.Equal(0, result.State.HighestClearedChapter);
        }
    }
    [Fact]
    public void RewardPoliciesAndWalletCapsAreEnforced()
    {
        var catalog = Catalog() with { Challenges = [] }; var state = MetaEngine.NewGame(catalog);
        foreach (var building in catalog.ManorBuildings)
        {
            state.ManorLevels[building.Id] = building.MaxLevel;
        }

        foreach (var pair in new[] { (Success, 100), (Success with { Survived = false }, 25), (Success with { Abandoned = true }, 0), (Success with { Tick = 1 }, 0) })
        {
            var plan = MetaEngine.BeginRun(catalog, state, "chapter-1", 1); Assert.Equal(pair.Item2, MetaEngine.SettleRun(catalog, plan.State, plan.Run, pair.Item1).Awarded["wood"]);
        }
        state = state with { HighestClearedChapter = 1 }; var repeat = MetaEngine.BeginRun(catalog, state, "chapter-1", 1);
        Assert.Equal(50, MetaEngine.SettleRun(catalog, repeat.State, repeat.Run, Success).Awarded["wood"]);
        state.Wallet["wood"] = 999; var capped = MetaEngine.BeginRun(catalog, state, "chapter-2", 1); var settlement = MetaEngine.SettleRun(catalog, capped.State, capped.Run, Success);
        Assert.Equal(1, settlement.Awarded["wood"]); Assert.Equal(99, settlement.Overflow["wood"]);
    }
    [Fact]
    public void ClockManipulationGrantsNothingAndDoesNotPoisonRecovery()
    {
        var catalog = Catalog(); var state = MetaEngine.NewGame(catalog);
        state = MetaEngine.AdvanceIdle(catalog, state, 100, 1000).State;
        var jump = MetaEngine.AdvanceIdle(catalog, state, 110, 9999999); Assert.Equal(0, jump.CreditedSeconds);
        var reset = MetaEngine.AdvanceIdle(catalog, jump.State, 120, 1020); Assert.Equal(0, reset.CreditedSeconds);
        var recovered = MetaEngine.AdvanceIdle(catalog, reset.State, 130, 1030); Assert.Equal(10, recovered.CreditedSeconds); Assert.Equal(1, recovered.Awarded["wood"]);
        var cap = MetaEngine.AdvanceIdle(catalog, recovered.State, 10000, 10900); Assert.Equal(60, cap.CreditedSeconds);
        Assert.Equal(0, MetaEngine.AdvanceIdle(catalog, cap.State, 1, 10910).CreditedSeconds);
    }
    [Fact]
    public void ManorPriorityGrowthAndAllSixVassalChannelsWork()
    {
        var catalog = Catalog(); var state = MetaEngine.NewGame(catalog); state.Wallet["wood"] = 200;
        var grown = MetaEngine.SetManorPriority(catalog, state, "building-2"); Assert.Equal(1, grown.ManorLevels["building-2"]); Assert.Equal(0, state.ManorLevels["building-2"]);
        state = state with { ActiveVassalIds = catalog.Vassals.Select(x => x.Id).ToArray() };
        Assert.Equal(new MetaModifiers(1010, 1010, 1010, 1010, 1010, 1010), MetaEngine.Modifiers(catalog, state));
        state.Vassals["vassal-0"] = new(1, 0, 5, true);
        var upgraded = MetaEngine.UpgradeVassal(catalog, state, "vassal-0"); upgraded = MetaEngine.RankUpVassal(catalog, upgraded, "vassal-0");
        Assert.Equal(1035, MetaEngine.Modifiers(catalog, upgraded).AttackPermille); Assert.Equal(0, upgraded.Vassals["vassal-0"].Fragments);
    }
    [Fact]
    public void VassalSlotsAndLevelCapCannotBeBypassed()
    {
        var catalog = Catalog(); var state = MetaEngine.NewGame(catalog); state.Wallet["wood"] = 20;
        Assert.Throws<InvalidOperationException>(() => MetaEngine.ToggleVassal(catalog, state, "vassal-1"));
        for (int i = 1; i < 5; i++)
        {
            state = MetaEngine.UpgradeVassal(catalog, state, "vassal-0");
        }

        Assert.Throws<InvalidOperationException>(() => MetaEngine.UpgradeVassal(catalog, state, "vassal-0"));
    }
    [Fact]
    public void ResearchGatesChallengeClaimsAndMetricAccumulationIsBounded()
    {
        var catalog = Catalog();
        catalog = catalog with { Challenges = [catalog.Challenges[0] with { ResearchLevel = 1 }] };
        var initial = MetaEngine.NewGame(catalog);
        var run = MetaEngine.BeginRun(catalog, initial, "chapter-1", 1);
        var result = MetaEngine.SettleRun(catalog, run.State, run.Run, Success with { Kills = long.MaxValue });
        Assert.Empty(result.ChallengesCompleted);
        Assert.Equal(MetaValidation.MaximumMetricValue, result.State.Metrics[MetaMetric.Kills.ToString()]);
        result.State.Wallet["wood"] = 200;
        var researched = MetaEngine.SetManorPriority(catalog, result.State, "building-1");
        Assert.Contains("clear-first", researched.CompletedChallenges);
        Assert.Empty(MetaValidation.ValidateState(catalog, researched));
    }
    [Fact]
    public void CatalogValidationRejectsCapsUnknownKeysAndInventedUnlocks()
    {
        var catalog = Catalog(); var state = MetaEngine.NewGame(catalog);
        Assert.Empty(MetaValidation.ValidateState(catalog, state));
        state.Wallet["wood"] = 1001; state.Wallet["unknown"] = 1;
        state.Vassals["vassal-0"] = new(20, 99, 999, true);
        state = state with { UnlockedContentIds = ["old-weapon", "invented"], ActiveVassalIds = ["vassal-0", "vassal-1"] };
        var errors = MetaValidation.ValidateState(catalog, state);
        Assert.Contains("wallet:wood", errors); Assert.Contains("wallet-keys", errors);
        Assert.Contains("vassal:vassal-0", errors); Assert.Contains("content-unlocks", errors); Assert.Contains("active-vassals", errors);
    }
    [Fact]
    public void InstantAbandonCannotFarmChallengeRewardsOrProgress()
    {
        var catalog = Catalog() with { Challenges = [new("first-run", "First", MetaMetric.Runs, 1, 0, ["new-weapon"], [], new() { ["wood"] = 10 })] };
        var state = MetaEngine.NewGame(catalog);
        for (int i = 0; i < 8; i++)
        {
            var run = MetaEngine.BeginRun(catalog, state, "chapter-1", i);
            state = MetaEngine.SettleRun(catalog, run.State, run.Run, Success with { Tick = 0, Abandoned = true, Survived = false }).State;
        }
        Assert.Equal(8, state.CompletedRuns);
        Assert.Empty(state.Metrics); Assert.Empty(state.CompletedChallenges);
        Assert.Equal(0, state.Wallet["wood"]); Assert.DoesNotContain("new-weapon", state.UnlockedContentIds);
        var eligible = MetaEngine.BeginRun(catalog, state, "chapter-1", 9);
        state = MetaEngine.SettleRun(catalog, eligible.State, eligible.Run, Success with { Tick = 10, Abandoned = true, Survived = false }).State;
        Assert.Equal(1, state.Metrics[MetaMetric.Runs.ToString()]);
        Assert.Contains("first-run", state.CompletedChallenges);
        Assert.Equal(0, state.HighestClearedChapter);
    }
    [Fact]
    public void LargeCapGrowthCoefficientsClampBeforeIntegerConversion()
    {
        var catalog = Catalog(); catalog = catalog with { Economy = catalog.Economy with { IdleCapSecondsPerLevel = int.MaxValue, LevelCapPerForgeLevel = int.MaxValue } };
        var state = MetaEngine.NewGame(catalog); state.ManorLevels["building-0"] = 3; state.ManorLevels["building-2"] = 3;
        state.Wallet["wood"] = 1000; state.Vassals["vassal-0"] = new(19, 0, 0, true);
        var upgraded = MetaEngine.UpgradeVassal(catalog, state, "vassal-0"); Assert.Equal(20, upgraded.Vassals["vassal-0"].Level);
        Assert.Empty(MetaValidation.ValidateState(catalog, upgraded));
        var clock = MetaEngine.AdvanceIdle(catalog, upgraded, 1, 1000).State;
        var idle = MetaEngine.AdvanceIdle(catalog, clock, 10001, 11000);
        Assert.Equal(120, idle.CreditedSeconds); Assert.All(idle.Awarded.Values, x => Assert.True(x >= 0));
    }
    [Theory]
    [InlineData("priority")]
    [InlineData("upgrade")]
    [InlineData("settlement")]
    [InlineData("idle")]
    public void AutomaticGrowthAndResearchRewardsReachStableStateBeforeReturning(string operation)
    {
        var catalog = Catalog();
        catalog = catalog with
        {
            ManorBuildings = catalog.ManorBuildings.Select(b => b with
            {
                BaseCost = new() { ["wood"] = b.Effect == ManorEffect.Research ? 200 : 1000 },
                CostPerLevel = new() { ["wood"] = 0 }
            }).ToArray(),
            Challenges = Enumerable.Range(1, 3).Select(level => new MetaChallenge(
                "research-" + level, "Research", MetaMetric.Runs, 1, level,
                ["unlock-" + level], [], new() { ["wood"] = 200 })).ToArray()
        };
        var state = MetaEngine.NewGame(catalog) with { ManorPriority = "building-1" };
        state.Metrics[MetaMetric.Runs.ToString()] = 1;
        state.Wallet["wood"] = operation == "upgrade" ? 202 : operation == "settlement" ? 100 : 200;
        var before = MetaSaveCodec.Encode(state);
        MetaState result;
        if (operation == "priority")
        {
            result = MetaEngine.SetManorPriority(catalog, state, "building-1");
        }
        else if (operation == "upgrade")
        {
            result = MetaEngine.UpgradeVassal(catalog, state, "vassal-0");
        }
        else if (operation == "idle")
        {
            var idle = MetaEngine.AdvanceIdle(catalog, state, 100, 1000);
            result = idle.State;
            Assert.Equal(3, idle.BuildingsGrown.Length);
        }
        else
        {
            var plan = MetaEngine.BeginRun(catalog, state, "chapter-1", 7);
            var settlement = MetaEngine.SettleRun(catalog, plan.State, plan.Run, Success);
            result = settlement.State;
            Assert.Equal(3, settlement.BuildingsGrown.Length);
            Assert.Equal(3, settlement.ChallengesCompleted.Length);
        }
        Assert.Equal(3, result.ManorLevels["building-1"]);
        Assert.Equal(3, result.CompletedChallenges.Length);
        Assert.Equal(200, result.Wallet["wood"]);
        Assert.Equal(before, MetaSaveCodec.Encode(state));
        Assert.Equal(MetaSaveCodec.Encode(result), MetaSaveCodec.Encode(MetaEngine.SetManorPriority(catalog, result, "building-1")));
    }
}
