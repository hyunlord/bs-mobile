using System.Security.Cryptography;
using SowSiege.Core;
using Xunit;

namespace SowSiege.Tests;

public sealed class HeroZoneUnionBoundaryTests
{
    [Theory]
    [InlineData(HeroActiveKind.Dash)]
    [InlineData(HeroActiveKind.TauntShield)]
    [InlineData(HeroActiveKind.ZoneSlow)]
    public void EveryKindRejectsBothForeignPayloads(HeroActiveKind kind)
    {
        var basis = HeroActiveCodecTests.Active();
        var dash = basis.Definitions[0].Dash!;
        var taunt = HeroTauntCodecTests.Parameters();
        var zone = HeroZoneStateTests.Parameters("test:enemy");
        var definition = basis.Definitions[0] with { Kind = kind, Dash = dash, Taunt = taunt, Zone = zone };
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(basis with { Definitions = new[] { definition } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResignedUnknownKindAndNoncanonicalZoneBooleanFailClosed(bool corruptBoolean)
    {
        var active = HeroZoneStateTests.Fixture().Options.HeroActive!;
        var bytes = HeroActiveCodec.Encode(active);
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream);
        SkipText(reader); reader.ReadInt32(); reader.ReadUInt32();
        for (var i = 0; i < 6; i++) { SkipText(reader); }
        reader.ReadInt32(); var skills = reader.ReadInt32();
        for (var i = 0; i < skills; i++) { SkipText(reader); reader.ReadInt32(); SkipText(reader); reader.ReadInt32(); }
        Assert.Equal(1, reader.ReadInt32()); SkipText(reader); reader.ReadInt32(); SkipText(reader);
        var kindOffset = checked((int)stream.Position); reader.ReadInt32();
        if (corruptBoolean)
        {
            for (var i = 0; i < 10; i++) { reader.ReadInt32(); }
            Assert.Equal(1, reader.ReadInt32()); SkipText(reader); bytes[checked((int)stream.Position)] = 2;
        }
        else { BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, kindOffset); }
        SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes.AsSpan(bytes.Length - 32));
        Assert.Throws<InvalidDataException>(() => HeroActiveCodec.Decode(bytes));
    }

    [Fact]
    public void MixedKindsRejectCombinedRuntimeEvenWhenEachPoolFits()
    {
        var basis = HeroActiveCodecTests.Active(); var owner = new string('o', WorldCombatCodec.MaximumIdentifierCharacters);
        basis = basis with { OwnerId = owner, HeroId = owner };
        var dash = basis.Definitions[0] with { Id = new string('a', WorldCombatCodec.MaximumIdentifierCharacters) };
        var taunt = new HeroActiveDefinition(new string('b', WorldCombatCodec.MaximumIdentifierCharacters), 1, new string('A', 64),
            HeroActiveKind.TauntShield, null, HeroTauntCodecTests.Parameters() with { MaximumInstances = 1900, MaximumTargets = 1 });
        var zone = new HeroActiveDefinition(new string('c', WorldCombatCodec.MaximumIdentifierCharacters), 1, new string('A', 64),
            HeroActiveKind.ZoneSlow, null, Zone: HeroZoneStateTests.Parameters("test:enemy") with { MaximumInstances = 1900, MaximumTargets = 1 });
        HeroActiveOptions Bind(params HeroActiveDefinition[] definitions) => basis with
        { Definitions = definitions, Skills = definitions.Select(d => new HeroSkillBinding(d.Id, d.Version, d.ContentHash, HeroSkillKind.Active)).ToArray() };
        Assert.NotEmpty(HeroActiveCodec.Encode(Bind(taunt)));
        Assert.NotEmpty(HeroActiveCodec.Encode(Bind(zone)));
        Assert.True(1 + 1900 + 1900 <= HeroActiveCodec.MaximumActiveInstances);
        Assert.Throws<ArgumentException>(() => HeroActiveCodec.Encode(Bind(dash, taunt, zone)));
    }

    private static void SkipText(BinaryReader reader)
    { var count = reader.ReadInt32(); Assert.Equal(count, reader.ReadBytes(count).Length); }
}
