using System.Security.Cryptography;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroTauntCodecTests
{
    internal static HeroTauntParameters Parameters(string enemy = "test:enemy") =>
        new(1, 1000, 100, 10, 20, 2, 2, 0, 400, 800,
            new[] { new HeroTauntEnemyPolicy(enemy, true) });

    internal static HeroActiveOptions Options(int count = 1)
    {
        var basis = HeroActiveCodecTests.Active();
        var definitions = Enumerable.Range(0, count).Select(i => new HeroActiveDefinition($"test:taunt-{i:D2}", 1,
            new string('A', 64), HeroActiveKind.TauntShield, null, Parameters())).ToArray();
        return basis with { Definitions = definitions, Skills = definitions.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
    }

    internal static HeroActiveOptions Change(HeroActiveOptions active, int index, Func<HeroTauntParameters, HeroTauntParameters> change)
    {
        var definitions = active.Definitions.ToArray();
        definitions[index] = definitions[index] with { Taunt = change(definitions[index].Taunt!) };
        return active with { Definitions = definitions };
    }

    [Fact]
    public void TauntPayloadRoundTripsAndFreezesEnemyPolicy()
    {
        var options = Options(); var bytes = HeroActiveCodec.Encode(options); var frozen = HeroActiveCodec.Freeze(options);
        ((HeroTauntEnemyPolicy[])options.Definitions[0].Taunt!.EnemyPolicies)[0] = new("test:other", false);
        Assert.Equal(bytes, HeroActiveCodec.Encode(frozen));
        Assert.Equal(bytes, HeroActiveCodec.Encode(HeroActiveCodec.Decode(bytes)));
        Assert.Throws<NotSupportedException>(() => ((IList<HeroTauntEnemyPolicy>)frozen.Definitions[0].Taunt!.EnemyPolicies)[0] = new("test:other", false));
    }

    [Fact]
    public void KindRequiresExactlyItsMatchingPayload()
    {
        var taunt = Options(); var dash = HeroActiveCodecTests.Active().Definitions[0].Dash;
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(taunt with { Definitions = new[] { taunt.Definitions[0] with { Taunt = null } } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(taunt with { Definitions = new[] { taunt.Definitions[0] with { Dash = dash } } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(taunt with { Definitions = new[] { taunt.Definitions[0] with { Kind = (HeroActiveKind)99 } } }));
        var active = HeroActiveCodecTests.Active();
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Definitions = new[] { active.Definitions[0] with { Taunt = Parameters() } } }));
    }

    [Theory]
    [InlineData("preparation", 0)]
    [InlineData("radius", 0)]
    [InlineData("stop", -1)]
    [InlineData("duration", 0)]
    [InlineData("cooldown", 0)]
    [InlineData("instances", 0)]
    [InlineData("targets", 0)]
    [InlineData("angle", -1001)]
    [InlineData("angle", 1001)]
    [InlineData("raw", -1)]
    [InlineData("cap", 1001)]
    public void InvalidTauntDataFailsClosed(string field, int value)
    {
        var active = Change(Options(), 0, p => field switch
        {
            "preparation" => p with { PreparationTicks = value },
            "radius" => p with { Radius = value },
            "stop" => p with { StopRadius = value },
            "duration" => p with { DurationTicks = value },
            "cooldown" => p with { CooldownTicks = value },
            "instances" => p with { MaximumInstances = value },
            "targets" => p with { MaximumTargets = value },
            "angle" => p with { ShieldHalfAngleCosPermille = value },
            "raw" => p with { DamageReductionPermille = value },
            _ => p with { DamageReductionCapPermille = value }
        });
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active));
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(0)]
    [InlineData(1000)]
    public void InclusiveConeRangeAndZeroRawAreRepresentable(int cosine)
    {
        var options = Change(Options(), 0, p => p with { ShieldHalfAngleCosPermille = cosine, DamageReductionPermille = 0 });
        Assert.Equal(HeroActiveCodec.Encode(options), HeroActiveCodec.Encode(HeroActiveCodec.Decode(HeroActiveCodec.Encode(options))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OwnerKindDefinitionsMustAgreeOnSharedCapAndInstanceBudget(bool cap)
    {
        var options = Change(Options(2), 1, p => cap ? p with { DamageReductionCapPermille = 700 } : p with { MaximumInstances = 3 });
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(options));
    }

    [Fact]
    public void SharedBudgetIsNotMultipliedByDefinitionCount()
    {
        var options = Options(10);
        for (var index = 0; index < options.Definitions.Count; index++)
        { options = Change(options, index, p => p with { MaximumInstances = 4096, MaximumTargets = 16 }); }
        Assert.Equal(HeroActiveCodec.Encode(options), HeroActiveCodec.Encode(HeroActiveCodec.Decode(HeroActiveCodec.Encode(options))));
        var excess = options;
        for (var index = 0; index < excess.Definitions.Count; index++)
        { excess = Change(excess, index, p => p with { MaximumTargets = 17 }); }
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(excess));
    }

    [Fact]
    public void HugeBudgetProductIsRejectedBeforeAllocation()
    {
        var options = Change(Options(), 0, p => p with { MaximumInstances = int.MaxValue, MaximumTargets = int.MaxValue });
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(options));
    }

    [Fact]
    public void DashAlsoCountsAgainstGlobalInstanceStorageBound()
    {
        var options = Change(Options(), 0, p => p with { MaximumInstances = 4096, MaximumTargets = 1 });
        var definitions = HeroActiveCodecTests.Active().Definitions.Concat(options.Definitions).ToArray();
        options = options with { Definitions = definitions, Skills = definitions.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(options));
    }

    [Fact]
    public void LongIdentifiersCannotExceedRuntimeByteBudgetEvenWithValidArrayCounts()
    {
        var options = Change(Options(), 0, p => p with { MaximumInstances = 4096, MaximumTargets = 1 });
        var id = new string('x', WorldCombatCodec.MaximumIdentifierCharacters);
        var definition = options.Definitions[0] with { Id = id };
        options = options with
        {
            HeroId = id,
            OwnerId = id,
            Definitions = new[] { definition },
            Skills = new[] { new HeroSkillBinding(id, 1, definition.ContentHash, HeroSkillKind.Active) }
        };
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(options));
    }

    [Fact]
    public void SecondDashDefinitionIsRejectedEvenWithDistinctIdentity()
    {
        var active = HeroActiveCodecTests.Active(); var first = active.Definitions[0];
        var definitions = new[] { first, first with { Id = "test:dash2" } };
        active = active with { Definitions = definitions, Skills = definitions.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active));
    }

    [Fact]
    public void DuplicateAndUnorderedPoliciesAreRejected()
    {
        var duplicate = Change(Options(), 0, p => p with { EnemyPolicies = new[] { p.EnemyPolicies[0], p.EnemyPolicies[0] } });
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(duplicate));
        var unordered = Change(Options(), 0, p => p with { EnemyPolicies = new[] { new HeroTauntEnemyPolicy("test:z", true), p.EnemyPolicies[0] } });
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(unordered));
    }

    [Fact]
    public void TruncatedAndResignedTrailingTauntBlocksAreRejected()
    {
        var bytes = HeroActiveCodec.Encode(Options(2));
        for (var length = 0; length < bytes.Length; length++)
        { Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes[..length])); }
        var changed = new byte[bytes.Length + 1]; bytes.AsSpan(0, bytes.Length - 32).CopyTo(changed);
        changed[bytes.Length - 32] = 1;
        SHA256.HashData(changed.AsSpan(0, changed.Length - 32)).CopyTo(changed.AsSpan(changed.Length - 32));
        Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(changed));
    }
}
