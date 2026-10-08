using SowSiege.Core;
using SowSiege.Sim;
using Xunit;
namespace SowSiege.Tests;

public sealed class ReplayTests
{
    private static byte[] Record()
    {
        var c = InteractiveTests.Catalog(); c = c with { Tuning = c.Tuning with { DurationTicks = 90 } };
        using var stream = new MemoryStream(); InteractiveCli.RecordFixture(c, new string('a', 64), 30000, stream); return stream.ToArray();
    }
    [Fact]
    public void ReplayRoundTripIncludesPausedCommandsAndRejectsContentMismatch()
    {
        var bytes = Record(); using var stream = new MemoryStream(bytes); var document = ReplayCodec.Read(stream);
        var c = InteractiveTests.Catalog(); c = c with { Tuning = c.Tuning with { DurationTicks = 90 } };
        Assert.Equal(90, ReplayRunner.Verify(c, new string('a', 64), document).Tick);
        Assert.Contains(document.Commands, x => x.Kind == ReplayCommandKind.LockCard);
        Assert.Contains(document.Commands, x => x.Kind == ReplayCommandKind.BanCard);
        Assert.Throws<InvalidDataException>(() => ReplayRunner.Verify(c, new string('b', 64), document));
        Assert.Throws<InvalidDataException>(() => ReplayRunner.Verify(c, new string('a', 64), document with { End = document.End with { Kind = ReplayEndKind.Quit } }));
        Assert.Throws<InvalidDataException>(() => ReplayRunner.Verify(c, new string('a', 64), document with { End = document.End with { StateHash = new string('f', 64) } }));
    }
    [Fact]
    public void DecoderRejectsTruncationTrailingBytesAndEveryCommandPayloadMisuse()
    {
        var bytes = Record();
        foreach (var length in new[] { 0, 1, bytes.Length / 2, bytes.Length - 1 }) { Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(new MemoryStream(bytes[..length]))); }
        Assert.Throws<InvalidDataException>(() => ReplayCodec.Read(new MemoryStream(bytes.Concat(new byte[] { 0 }).ToArray())));
        var s = InteractiveTests.Session(); var hash = s.ComputeStateHash();
        foreach (var command in new[] { new ReplayCommand(0, 0, ReplayCommandKind.Advance, default, "bad"), new(0, 0, ReplayCommandKind.GrantLevel, new(1, 0)), new(0, 0, ReplayCommandKind.SetInvulnerable, Value: 2), new(0, 0, ReplayCommandKind.SetAimMode, Value: 99), new(0, 0, ReplayCommandKind.SetSpawnPermille, Value: -1), new(0, 0, (ReplayCommandKind)99) })
        {
            Assert.Throws<ArgumentException>(() => s.Apply(command)); Assert.Equal(hash, s.ComputeStateHash());
        }
    }
    [Fact]
    public void SnapshotReadOnlyCollectionsCannotMutateCoreAndRngWordsAffectHash()
    {
        var s = InteractiveTests.Session(); s.Apply(new(0, 0, ReplayCommandKind.Advance));
        var frame = s.View.CaptureFrame(); var hash = s.ComputeStateHash();
        Assert.Throws<NotSupportedException>(() => ((IList<EnemyView>)frame.Enemies).Clear());
        Assert.Equal(hash, s.ComputeStateHash());
        s.Simulation.RandomState.Portable!.Words[1] ^= 1; Assert.NotEqual(hash, s.ComputeStateHash());
    }
    [Fact]
    public void ExplicitStateWriterCoversEveryPersistedMember()
    {
        // Structural coverage prevents newly added hidden state from silently escaping the codec.
        var root = FindRoot(); var source = File.ReadAllText(Path.Combine(root, "core/src/SowSiege.Core/PortableStateCodec.cs"));
        var types = typeof(PortableStateCodec).GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(method => method.Name == "Write" && method.GetParameters().Length == 1)
            .Select(method => method.GetParameters()[0].ParameterType)
            .Where(type => type.Namespace == typeof(WorldState).Namespace && !type.IsValueType).Distinct();
        foreach (var type in types)
        {
            var start = source.IndexOf($"private void Write({type.Name}? value)", StringComparison.Ordinal); Assert.True(start >= 0, type.Name);
            var end = source.IndexOf("\n        }", start, StringComparison.Ordinal); var body = source[start..end];
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Where(field => !field.Name.StartsWith("<", StringComparison.Ordinal))) { Assert.Contains("value." + field.Name, body); }
            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) { Assert.Contains("value." + prop.Name, body); }
        }
    }
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
