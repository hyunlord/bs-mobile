using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public sealed class VassalContentTests
{
    private static string Data => Path.Combine(AppContext.BaseDirectory, "data");
    private static string Source => File.ReadAllText(Path.Combine(Data, "opt-in", "vassals", "vassal-run-v1.json"));

    [Fact]
    public void SourceHashCoversExactFileBytesIncludingWhitespace()
    {
        using var first = new Input(Source); using var second = new Input(Source + "\n");
        var a = first.Load(); var b = second.Load();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(first.Path))), a.ContentHash);
        Assert.NotEqual(a.ContentHash, b.ContentHash);
        Assert.Equal(a.Caps, b.Caps); Assert.Equal(a.Effects, b.Effects); Assert.Equal(a.Vassals, b.Vassals);
        var meta = MetaContentLoader.Read(Data); var state = MetaEngine.NewGame(meta);
        var snapshot = VassalRun.Create(meta, state, a);
        Assert.Equal(a.ContentHash, snapshot.Snapshot.ContentHash);
        Assert.Throws<ArgumentException>(() => VassalRun.Validate(snapshot with { Rules = b }));
    }

    [Fact]
    public void ChangedFileCoefficientsCapsAndSlotLimitsDriveSnapshot()
    {
        var source = JsonNode.Parse(Source)!.AsObject();
        var attack = source["effects"]!.AsArray().Single(node => node!["ability"]!.GetValue<string>() == "Attack")!;
        attack["base"] = 37; attack["cap"] = 13;
        source["caps"]!["baseSlots"] = 2; source["caps"]!["maximumSlots"] = 2;
        source["caps"]!["baseLevelCap"] = 4; source["caps"]!["maximumLevel"] = 19;
        using var input = new Input(source.ToJsonString()); var rules = input.Load();
        var original = MetaContentLoader.Read(Data);
        var meta = original with { Economy = original.Economy with { BaseVassalSlots = 2, MaximumVassalSlots = 2, BaseVassalLevelCap = 4 } };
        var state = MetaEngine.NewGame(meta); string id = meta.Vassals.Single(v => v.Ability == MetaAbility.Attack).Id;
        state.Vassals[id] = state.Vassals[id] with { Unlocked = true };
        state = state with { ActiveVassalIds = [id] };
        var options = VassalRun.Create(meta, state, rules);
        Assert.Equal(2, options.Snapshot.EffectiveSlots); Assert.Equal(4, options.Snapshot.EffectiveLevelCap);
        Assert.Equal(37, options.Snapshot.Effects[(int)MetaAbility.Attack].Raw);
        Assert.Equal(13, options.Snapshot.Effects[(int)MetaAbility.Attack].Bonus);
        Assert.NotEqual(VassalContentLoader.Load(Data).ContentHash, options.Snapshot.ContentHash);
        Assert.Equal(VassalRunCodec.Encode(options), VassalRunCodec.Encode(VassalRunCodec.Decode(VassalRunCodec.Encode(options))));
    }

    [Fact]
    public void EverySourceFieldIsRequiredIncludingLegitimateZeroValues()
    {
        var root = JsonNode.Parse(Source)!.AsObject();
        var paths = root.Select(item => new[] { item.Key })
            .Concat(root["caps"]!.AsObject().Select(item => new[] { "caps", item.Key }))
            .Concat(root["effects"]![0]!.AsObject().Select(item => new[] { "effects", "0", item.Key }))
            .Concat(root["vassals"]![0]!.AsObject().Select(item => new[] { "vassals", "0", item.Key }));
        foreach (var path in paths)
        {
            var altered = JsonNode.Parse(Source)!; var parent = altered;
            foreach (string part in path.SkipLast(1)) { parent = parent is JsonArray ? parent[int.Parse(part)]! : parent[part]!; }
            Assert.True(parent.AsObject().Remove(path[^1]));
            using var input = new Input(altered.ToJsonString());
            Assert.Throws<InvalidDataException>(() => input.Load());
        }
    }

    [Theory]
    [InlineData("baseSlots", 0)]
    [InlineData("maximumSlots", 0)]
    [InlineData("barracksLevelsPerSlot", 0)]
    [InlineData("baseLevelCap", 0)]
    [InlineData("levelCapPerForgeLevel", -1)]
    [InlineData("maximumLevel", 0)]
    [InlineData("maximumRank", -1)]
    public void MalformedCapValuesAreRejected(string field, int value)
    {
        var source = JsonNode.Parse(Source)!; source["caps"]![field] = value;
        using var input = new Input(source.ToJsonString());
        Assert.Throws<ArgumentException>(() => input.Load());
    }

    [Fact]
    public void SourceCannotSpoofHashAndUnknownOrDuplicateFieldsAreRejected()
    {
        var source = JsonNode.Parse(Source)!; source["contentHash"] = new string('A', 64);
        using var spoof = new Input(source.ToJsonString());
        Assert.Throws<System.Text.Json.JsonException>(() => spoof.Load());
        using var duplicate = new Input(Source.Replace("\"version\": 1,", "\"version\": 1, \"version\": 1,"));
        Assert.Throws<InvalidDataException>(() => duplicate.Load());
        source = JsonNode.Parse(Source)!; source["caps"]!["unknown"] = 1;
        using var unknown = new Input(source.ToJsonString());
        Assert.Throws<System.Text.Json.JsonException>(() => unknown.Load());
    }

    [Fact]
    public void StaleBasisRejectsCoefficientAndCapChangesEvenWithMatchingSourceHash()
    {
        var options = VassalRunTests.Options();
        Assert.Throws<ArgumentException>(() => VassalRun.Validate(options with
        { Rules = options.Rules with { Caps = options.Rules.Caps with { MaximumRank = options.Rules.Caps.MaximumRank + 1 } } }));
        var effects = options.Rules.Effects.ToArray(); effects[0] = effects[0] with { Cap = effects[0].Cap + 1 };
        Assert.Throws<ArgumentException>(() => VassalRun.Validate(options with { Rules = options.Rules with { Effects = effects } }));
        Assert.Throws<ArgumentException>(() => VassalRun.Validate(options with
        { Snapshot = options.Snapshot with { ContentHash = new string('A', 64) }, Rules = options.Rules with { ContentHash = new string('A', 64) } }));
    }

    private sealed class Input : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vassal-content-test-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(root, "opt-in", "vassals", "vassal-run-v1.json");
        public Input(string source) { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!); File.WriteAllText(Path, source); }
        public VassalRunRules Load() => VassalContentLoader.Load(root);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
