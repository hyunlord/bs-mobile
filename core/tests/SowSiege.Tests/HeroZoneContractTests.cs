using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroZoneContractTests
{
    [Theory]
    [InlineData("tell", -1)]
    [InlineData("duration", 0)]
    [InlineData("cooldown", 0)]
    [InlineData("target-range", 0)]
    [InlineData("radius", 0)]
    [InlineData("instances", 0)]
    [InlineData("targets", 0)]
    [InlineData("raw", -1)]
    [InlineData("raw", 1001)]
    [InlineData("cap", 1001)]
    [InlineData("minimum", 0)]
    [InlineData("minimum", 11)]
    [InlineData("tell", int.MaxValue)]
    [InlineData("duration", int.MaxValue)]
    [InlineData("cooldown", int.MaxValue)]
    public void UnsafeZoneParametersAreRejectedBeforeSessionExecution(string field, int value)
    {
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0, p => field switch
        {
            "tell" => p with { TellTicks = value },
            "duration" => p with { DurationTicks = value },
            "cooldown" => p with { CooldownTicks = value },
            "target-range" => p with { TargetRange = value },
            "radius" => p with { Radius = value },
            "instances" => p with { MaximumInstances = value },
            "targets" => p with { MaximumTargets = value },
            "raw" => p with { SlowPermille = value },
            "cap" => p with { SlowCapPermille = value },
            _ => p with { MinimumPositiveSpeed = value }
        })));
    }

    [Theory]
    [InlineData("cap")]
    [InlineData("minimum")]
    [InlineData("instances")]
    public void OwnerDefinitionsMustAgreeOnSlowRulesAndSharedInstanceBudget(string field)
    {
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(2, configure: a => HeroZoneStateTests.Change(a, 1, p => field switch
        {
            "cap" => p with { SlowCapPermille = 700 },
            "minimum" => p with { MinimumPositiveSpeed = 2 },
            _ => p with { MaximumInstances = 3 }
        })));
    }

    [Fact]
    public void DefinitionCountDoesNotMultiplySharedSourceStorage()
    {
        var f = HeroZoneStateTests.Fixture(10, configure: a =>
        {
            for (var i = 0; i < a.Definitions.Count; i++)
            { a = HeroZoneStateTests.Change(a, i, p => p with { MaximumInstances = 1024, MaximumTargets = 8 }); }
            return a;
        });
        Assert.True(f.State.RuntimeByteBound <= WorldCombatCodec.MaximumBytes);
    }

    [Fact]
    public void AggregateSourceAndEncodedRuntimeBoundsRejectBeforeAllocation()
    {
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0,
            p => p with { MaximumInstances = 4096, MaximumTargets = 17 })));
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0,
            p => p with { MaximumInstances = int.MaxValue, MaximumTargets = int.MaxValue })));
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a =>
        {
            var id = new string('x', WorldCombatCodec.MaximumIdentifierCharacters);
            var definition = a.Definitions[0] with { Id = id, Zone = a.Definitions[0].Zone! with { MaximumInstances = 4096, MaximumTargets = 16 } };
            return a with { Definitions = new[] { definition }, Skills = new[] { new HeroSkillBinding(id, 1, definition.ContentHash, HeroSkillKind.Active) } };
        }));
    }

    [Fact]
    public void UnknownMissingAndDuplicatePoliciesFailClosed()
    {
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0,
            p => p with { EnemyPolicies = new[] { new HeroZoneEnemyPolicy("test:unknown", true) } })));
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0,
            p => p with { EnemyPolicies = Array.Empty<HeroZoneEnemyPolicy>() })));
        Assert.Throws<ArgumentException>(() => HeroZoneStateTests.Fixture(configure: a => HeroZoneStateTests.Change(a, 0,
            p => p with { EnemyPolicies = new[] { p.EnemyPolicies[0], p.EnemyPolicies[0] } })));
    }

    [Fact]
    public void ZonePayloadRoundTripsAndRejectsWrongUnionPayload()
    {
        var f = HeroZoneStateTests.Fixture(); var active = f.Options.HeroActive!;
        var bytes = HeroActiveCodec.Encode(active); Assert.Equal(bytes, HeroActiveCodec.Encode(HeroActiveCodec.Decode(bytes)));
        var dash = HeroActiveCodecTests.Active().Definitions[0].Dash;
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Definitions = new[] { active.Definitions[0] with { Dash = dash } } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Definitions = new[] { active.Definitions[0] with { Zone = null } } }));
        for (var length = 0; length < bytes.Length; length++)
        { Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes[..length])); }
    }
}
