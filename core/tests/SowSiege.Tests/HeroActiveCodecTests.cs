using System.Security.Cryptography;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroActiveCodecTests
{
    internal static HeroActiveOptions Active(string hero = "test:hero", string slot = "test:weapon", string? enemy = null) =>
        new(new string('C', 64), new string('D', 64), hero, hero, slot, slot, 0,
            new[] { new HeroSkillBinding("test:dash", 1, new string('A', 64), HeroSkillKind.Active) },
            new[] { new HeroActiveDefinition("test:dash", 1, new string('A', 64), HeroActiveKind.Dash, new(1, 2000, 100, 300, 3, 10, 1)) },
            Array.Empty<HeroActiveWall>(), new[] { new HeroActiveEnemyBody(enemy ?? "test:enemy", 0, true) });

    internal static InteractiveOptions HeaderOptions()
    {
        var active = Active(); var snapshot = WorldCombatCodecTests.Options().Snapshot;
        var world = new WorldCombatOptions(snapshot with
        {
            Hero = snapshot.Hero with { Skills = active.Skills.Select(s => new WorldHeroSkill(s.Id, s.Version, s.ContentHash)).ToArray() }
        });
        return new(new(30001, active.HeroId, "test:estate", "manual", ManualCards: true), AimMode.Movement,
            active.ContentHash, WorldCombat: world, HeroActive: active);
    }

    [Fact]
    public void ActiveContractFreezesNestedListsAndRoundTripsExactly()
    {
        var input = Active() with { Walls = new[] { new HeroActiveWall("test:wall", 10, 10, 11, 20) } };
        var bytes = HeroActiveCodec.Encode(input); var frozen = HeroActiveCodec.Freeze(input);
        ((HeroActiveWall[])input.Walls)[0] = new("test:other", 20, 20, 21, 30);
        ((HeroActiveDefinition[])input.Definitions)[0] = input.Definitions[0] with { Dash = input.Definitions[0].Dash! with { SpeedPerTick = 999 } };
        Assert.Equal(bytes, HeroActiveCodec.Encode(frozen));
        Assert.Throws<NotSupportedException>(() => ((IList<HeroActiveWall>)frozen.Walls)[0] = input.Walls[0]);
        Assert.Equal(bytes, HeroActiveCodec.Encode(HeroActiveCodec.Decode(bytes)));
    }

    [Fact]
    public void ActiveBlockRejectsAllTruncationsAndTrailingBytes()
    {
        var bytes = HeroActiveCodec.Encode(Active());
        for (var length = 0; length < bytes.Length; length++)
        { Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes[..length])); }
        Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes.Concat(new byte[] { 0 }).ToArray()));
        bytes[40] ^= 1;
        Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void ResignedUnknownSchemaAndFlagsRemainInvalid(int value)
    {
        var original = HeroActiveCodec.Encode(Active());
        var schemaOffset = sizeof(int) + System.Text.Encoding.UTF8.GetByteCount("hero-active-v1");
        foreach (var offset in new[] { schemaOffset, schemaOffset + sizeof(int) })
        {
            var bytes = (byte[])original.Clone(); BitConverter.GetBytes(value).CopyTo(bytes, offset);
            SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
            Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes));
        }
    }

    [Fact]
    public void SkillDefinitionIdentityCannotBeSubstituted()
    {
        var active = Active();
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Skills = Array.Empty<HeroSkillBinding>() }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Skills = new[] { active.Skills[0] with { ContentHash = new string('B', 64) } } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Definitions = new[] { active.Definitions[0] with { Kind = (HeroActiveKind)99 } } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { OwnerId = "test:other" }));
    }

    [Theory]
    [InlineData("preparation", 0)]
    [InlineData("duration", 0)]
    [InlineData("cooldown", 0)]
    [InlineData("speed", 0)]
    [InlineData("distance", -1)]
    [InlineData("instances", 2)]
    [InlineData("speed", int.MaxValue)]
    public void InvalidDashDataCannotBeEncoded(string field, int value)
    {
        var active = Active(); var definition = active.Definitions[0]; var p = Assert.IsType<HeroDashParameters>(definition.Dash);
        p = field switch
        {
            "preparation" => p with { PreparationTicks = value },
            "duration" => p with { DurationTicks = value },
            "cooldown" => p with { CooldownTicks = value },
            "speed" => p with { SpeedPerTick = value },
            "distance" => p with { MaximumDistance = value },
            _ => p with { MaximumInstances = value }
        };
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Definitions = new[] { definition with { Dash = p } } }));
    }

    [Fact]
    public void DuplicateOrUnorderedGeometryAndBodiesAreRejected()
    {
        var active = Active(); var wall = new HeroActiveWall("test:wall", 1, 1, 2, 2);
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Walls = new[] { wall, wall } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { Walls = new[] { wall with { Id = "test:z" }, wall } }));
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(active with { EnemyBodies = new[] { active.EnemyBodies[0], active.EnemyBodies[0] } }));
    }

    [Fact]
    public void ActiveHeaderRoundTripsButCannotFallBackToNonWorldOrAutomaticBattle()
    {
        var options = HeaderOptions(); using var bytes = new MemoryStream();
        ReplayCodec.WriteHeader(bytes, ReplayCodec.Header(options)); bytes.Position = 0;
        var decoded = ReplayCodec.ReadHeader(new BinaryReader(bytes));
        Assert.Equal(HeroActiveCodec.Encode(options.HeroActive!), HeroActiveCodec.Encode(decoded.Options.HeroActive!));
        Assert.Throws<ArgumentNullException>(() => ReplayCodec.Header(options with { WorldCombat = null }));
        var snapshot = options.WorldCombat!.Snapshot;
        Assert.Throws<ArgumentException>(() => ReplayCodec.Header(options with { WorldCombat = new(snapshot with { Mode = WorldCombatMode.AutoPve }) }));
    }
}
