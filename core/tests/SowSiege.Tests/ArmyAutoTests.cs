using System.Security.Cryptography;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class ArmyAutoTests
{
    private static ArmyAutoFixtureDocument Document() => ArmyAutoFixtureCli.Load(Path.Combine(AppContext.BaseDirectory, "data", "opt-in", "army-auto", "hand-fixtures-v1.json"));
    private static ArmyAutoInput Input() => Document().Fixtures.First(f => f.Name == "victory").Input;
    [Theory]
    [InlineData("mutual")]
    [InlineData("victory")]
    [InlineData("cap")]
    [InlineData("undefended")]
    [InlineData("empty")]
    [InlineData("empty-attack")]
    [InlineData("overkill")]
    public void HandCalculatedFixture(string name)
    {
        var fixture = Document().Fixtures.Single(f => f.Name == name); var result = ArmyAuto.Resolve(fixture.Input); var e = fixture.Expected;
        Assert.Equal(e.Terminal, result.Combat.Terminal.ToString()); Assert.Equal(e.Rounds, result.Combat.TickOrRound);
        Assert.Equal(e.AttackerK, result.Combat.Troops.Sum(t => t.K)); Assert.Equal(e.DefenderK, result.DefenderTroops.Sum(t => t.K));
        Assert.Equal(e.FirstAttackerHealth, result.AttackerSlots.FirstOrDefault()?.Health ?? 0);
        Assert.Equal(fixture.Input.ContractHash, result.Combat.ContractHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(ArmyAuto.EncodeResult(result))), result.Combat.StateHash);
        Assert.Empty(result.Combat.Objectives);
    }
    [Fact]
    public void PublicEncoderRejectsFabricatedDirectObjectiveProgress()
    {
        var result = ArmyAuto.Resolve(Input());
        var forged = result with
        {
            Combat = result.Combat with
            { Objectives = new[] { new WorldObjectiveResult("test:hold", 30, true) } }
        };
        Assert.Throws<ArgumentException>(() => ArmyAuto.EncodeResult(forged));
        Assert.Throws<ArgumentException>(() => ArmyAuto.EncodeResult(result with { Combat = result.Combat with { Objectives = null! } }));
    }
    [Fact]
    public void FinalAllowedRoundVictoryPrecedesCap()
    {
        var input = Input(); var result = ArmyAuto.Resolve(input with { Rules = input.Rules with { MaxRounds = 1 } });
        Assert.Equal(WorldTerminalReason.AutoVictory, result.Combat.Terminal);
    }
    [Theory]
    [InlineData(WorldCombatMode.AutoPve, true)]
    [InlineData(WorldCombatMode.CityPvp, false)]
    public void ObjectiveUsesModePolicy(WorldCombatMode mode, bool satisfied)
    {
        var input = Input(); input = input with
        {
            Mode = mode,
            Rules = input.Rules with
            { Policies = input.Rules.Policies.Select(p => p with { VictorySatisfiesObjective = p.Mode == WorldCombatMode.AutoPve }).ToArray() }
        };
        Assert.Equal(satisfied ? "auto-objective-satisfied" : "auto-objective-unsatisfied", ArmyAuto.Resolve(input).ObjectiveStatus);
    }
    [Fact]
    public void UndefendedUsesItsOwnPolicyAndRetainsZeroRoster()
    {
        var input = Document().Fixtures.Single(f => f.Name == "undefended").Input;
        input = input with { Rules = input.Rules with { Policies = input.Rules.Policies.Select(p => p with { UndefendedSatisfiesObjective = false }).ToArray() } };
        var result = ArmyAuto.Resolve(input); Assert.Equal("auto-objective-unsatisfied", result.ObjectiveStatus);
        Assert.Equal(new WorldTroopResult("test:troop", 0, 0), Assert.Single(result.DefenderTroops));
    }
    [Fact]
    public void FloorMatchupAndDefenseAreConsumed()
    {
        var input = Input(); var a = input.Attacker.Troops[0] with { Attack = 5 }; var d = input.Defender.Troops[0] with { Defense = 0, Health = 20, Attack = 0 };
        input = input with
        {
            Attacker = input.Attacker with { Troops = new[] { a } },
            Defender = input.Defender with { Troops = new[] { d } },
            BattlefieldDefense = 1,
            ObjectiveDefense = 1,
            Rules = input.Rules with
            {
                MaxRounds = 1,
                Matchups = input.Rules.Matchups.Select(m => m with { Multiplier = 1500 }).ToArray()
            }
        };
        Assert.Equal(15, Assert.Single(ArmyAuto.Resolve(input).DefenderSlots).Health);
    }
    [Fact]
    public void RawFlatBonusesSumBeforeOneCapAndNeverApplyTwice()
    {
        var input = Input(); var hash = input.ContentHash;
        input = input with
        {
            Attacker = input.Attacker with
            {
                Effects = new[] {
            new ArmyAutoEffect(WorldEffectSourceKind.Vassal, "test:a", hash, 15, 0, 0),
            new ArmyAutoEffect(WorldEffectSourceKind.Research, "test:b", hash, 15, 0, 0) }
            },
            Defender = input.Defender with { Troops = new[] { input.Defender.Troops[0] with { Health = 100 } } },
            Rules = input.Rules with { MaxRounds = 1 }
        };
        Assert.Equal(70, Assert.Single(ArmyAuto.Resolve(input).DefenderSlots).Health);
    }
    [Fact]
    public void SortingIsStableAndDoesNotMutateCallerArrays()
    {
        var input = Input(); var ordinals = new[] { 9, 2 }; var a = input.Attacker.Troops[0] with { Ordinals = ordinals };
        input = input with { Attacker = input.Attacker with { Troops = new[] { a } } };
        var result = ArmyAuto.Resolve(input);
        var reversed = input with
        {
            Attacker = input.Attacker with { Troops = new[] { a with { Ordinals = new[] { 2, 9 } } } },
            Rules = input.Rules with { Policies = input.Rules.Policies.Reverse().ToArray() }
        };
        Assert.Equal(ArmyAuto.EncodeResult(result), ArmyAuto.EncodeResult(ArmyAuto.Resolve(reversed)));
        Assert.Equal(new[] { 9, 2 }, ordinals); ordinals[0] = 99;
        Assert.DoesNotContain(result.AttackerSlots, s => s.Ordinal == 99);
        Assert.Equal(2, result.AttackerSlots[0].Ordinal);
    }
    [Fact]
    public void TargetTieUsesTroopIdThenOrdinalAndPermutationDoesNotChangeHashes()
    {
        var input = Input(); var first = input.Defender.Troops[0] with { Id = "test:a", Ordinals = new[] { 9, 2 } };
        var second = first with { Id = "test:b", Ordinals = new[] { 0 } };
        var types = new[] { "test:a", "test:b", "test:troop" };
        input = input with
        {
            Defender = input.Defender with { Troops = new[] { second, first } },
            Rules = input.Rules with { MaxRounds = 1, Matchups = types.SelectMany(a => types.Select(d => new ArmyAutoMatchup(a, d, 1000))).ToArray() }
        };
        var result = ArmyAuto.Resolve(input);
        Assert.Equal(new ArmyAutoSlotResult("test:a", 2, 0), result.DefenderSlots[0]);
        var reversed = input with
        {
            Defender = input.Defender with { Troops = input.Defender.Troops.Reverse().ToArray() },
            Rules = input.Rules with { Matchups = input.Rules.Matchups.Reverse().ToArray() }
        };
        Assert.Equal(ArmyAuto.EncodeResult(result), ArmyAuto.EncodeResult(ArmyAuto.Resolve(reversed)));
    }
    [Fact]
    public void DataPriorityPrecedesTroopId()
    {
        var input = Input(); var a = input.Defender.Troops[0] with { Id = "test:a", TargetPriority = 1 };
        var b = a with { Id = "test:b", TargetPriority = 0 };
        input = input with
        {
            Defender = input.Defender with { Troops = new[] { a, b } },
            Rules = input.Rules with
            {
                MaxRounds = 1,
                Matchups = new[] { new ArmyAutoMatchup("test:troop", "test:a", 1000), new ArmyAutoMatchup("test:troop", "test:b", 1000),
            new ArmyAutoMatchup("test:a", "test:troop", 1000), new ArmyAutoMatchup("test:b", "test:troop", 1000) }
            }
        };
        Assert.Equal("test:b", ArmyAuto.Resolve(input).DefenderSlots.Single(s => s.Health == 0).TroopId);
    }
    [Theory]
    [InlineData("contract")]
    [InlineData("content")]
    [InlineData("mode")]
    [InlineData("rules")]
    [InlineData("negative")]
    [InlineData("duplicate-troop")]
    [InlineData("duplicate-slot")]
    [InlineData("negative-slot")]
    [InlineData("matchup")]
    [InlineData("duplicate-matchup")]
    [InlineData("policy")]
    [InlineData("duplicate-policy")]
    [InlineData("hero")]
    [InlineData("duplicate-effect")]
    [InlineData("overflow")]
    [InlineData("work")]
    [InlineData("null")]
    public void RejectsMalformedSnapshots(string kind)
    {
        var i = Input(); var troop = i.Attacker.Troops[0];
        i = kind switch
        {
            "contract" => i with { ContractHash = "a" },
            "content" => i with { ContentHash = "a" },
            "mode" => i with { Mode = WorldCombatMode.DirectPve },
            "rules" => i with { Rules = i.Rules with { Version = "army-auto-v2" } },
            "negative" => i with { ObjectiveDefense = -1 },
            "duplicate-troop" => i with { Attacker = i.Attacker with { Troops = new[] { troop, troop } } },
            "duplicate-slot" => i with { Attacker = i.Attacker with { Troops = new[] { troop with { Ordinals = new[] { 0, 0 } } } } },
            "negative-slot" => i with { Attacker = i.Attacker with { Troops = new[] { troop with { Ordinals = new[] { -1 } } } } },
            "matchup" => i with { Rules = i.Rules with { Matchups = Array.Empty<ArmyAutoMatchup>() } },
            "duplicate-matchup" => i with { Rules = i.Rules with { Matchups = new[] { i.Rules.Matchups[0], i.Rules.Matchups[0] } } },
            "policy" => i with { Rules = i.Rules with { Policies = Array.Empty<ArmyAutoPolicy>() } },
            "duplicate-policy" => i with { Rules = i.Rules with { Policies = new[] { i.Rules.Policies[0], i.Rules.Policies[0] } } },
            "hero" => i with { Attacker = i.Attacker with { Effects = new[] { new ArmyAutoEffect(WorldEffectSourceKind.Hero, "test:hero", i.ContentHash, 1, 0, 0) } } },
            "duplicate-effect" => i with { Attacker = i.Attacker with { Effects = Enumerable.Repeat(new ArmyAutoEffect(WorldEffectSourceKind.Vassal, "test:v", i.ContentHash, 1, 0, 0), 2).ToArray() } },
            "overflow" => i with { Rules = i.Rules with { MaxStat = int.MaxValue } },
            "work" => i with { Rules = i.Rules with { MaxRounds = int.MaxValue } },
            "null" => i with { Attacker = null! },
            _ => throw new InvalidOperationException()
        };
        var error = Record.Exception(() => ArmyAuto.Resolve(i)); Assert.True(error is ArgumentException or OverflowException, error?.ToString());
    }
    [Fact]
    public void ProvenanceAndZeroHeroAvailabilityAreBoundIntoState()
    {
        var input = Input(); var baseline = ArmyAuto.Resolve(input);
        foreach (var changed in new[] { input with { ContractHash = new string('B', 64) }, input with { ContentHash = new string('E', 64) },
            input with { Defender = input.Defender with { HeroAvailable = true } } })
        { var result = ArmyAuto.Resolve(changed); Assert.NotEqual(baseline.InputHash, result.InputHash); Assert.NotEqual(baseline.Combat.StateHash, result.Combat.StateHash); }
    }
}
