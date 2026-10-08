using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SowSiege.Core;
using SowSiege.Sim;
using Xunit;

namespace SowSiege.Tests;

public class HostSerializationTests
{
    private readonly record struct LegacyPosition(int X, int Y);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -20)]
    [InlineData(int.MinValue, int.MaxValue)]
    [InlineData(int.MaxValue, int.MinValue)]
    [InlineData(1200, 4500)]
    public void PositionPreservesCompilerRecordHash(int x, int y)
    {
        Assert.Equal(new LegacyPosition(x, y).GetHashCode(), new Position(x, y).GetHashCode());
    }

    private sealed class OptionalState
    {
        [OmitWhenNull] public string? Optional { get; set; }
        public string? Required { get; set; }
        [OmitWhenNull] public string? Field;
    }

    [Fact]
    public void OnlyAnnotatedNullsAreOmittedFromHashAndCli()
    {
        var state = new OptionalState();
        Assert.Equal("{\"Required\":null}", JsonSerializer.Serialize(state, HostJson.CreateOptions(includeFields: true)));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{\"Required\":null}"))), new CanonicalStateHasher().Compute(state));
        state.Optional = "kept"; state.Field = "field";
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, HostJson.CreateOptions(includeFields: true)));
        Assert.Equal("kept", json.RootElement.GetProperty("Optional").GetString());
        Assert.Equal("field", json.RootElement.GetProperty("Field").GetString());
    }

    [Fact]
    public void ObjectsSortOrdinallyWhileArraysKeepTheirOrder()
    {
        var hasher = new CanonicalStateHasher();
        Assert.Equal(hasher.Compute(new { z = 1, a = new { b = 2, a = 3 } }), hasher.Compute(new { a = new { a = 3, b = 2 }, z = 1 }));
        Assert.NotEqual(hasher.Compute(new[] { 1, 2 }), hasher.Compute(new[] { 2, 1 }));
    }

    [Fact]
    public void CoreIdentityDescribesTheLoadedAssemblyBytes()
    {
        var identity = CoreAssemblyMetadata.Read();
        Assert.Equal(".NETCoreApp,Version=v8.0", identity.TargetFramework);
        Assert.Equal(typeof(Simulation).Assembly.ManifestModule.ModuleVersionId.ToString("D"), identity.Mvid);
        Assert.Equal(typeof(Simulation).Assembly.Location, identity.Location);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(identity.Location))), identity.Sha256);
    }

    [Fact]
    public void PositionKeepsValueEqualityMovementAndSerializedCoordinates()
    {
        var point = new Position(10, 20);
        var same = new Position(10, 20);
        var other = new Position(20, 30);
        point.Deconstruct(out var x, out var y);
        Assert.Equal((10, 20), (x, y));
        Assert.Equal("Position { X = 10, Y = 20 }", point.ToString());
        Assert.True(point == same);
        Assert.False(point != same);
        Assert.True(point != other);
        Assert.True(point.Equals((object)same));
        Assert.False(point.Equals(null));
        Assert.False(point.Equals("10,20"));
        Assert.Equal(point.GetHashCode(), same.GetHashCode());
        Assert.Single(new HashSet<Position> { point, same });
        Assert.Equal(new Position(15, 25), point.MoveToward(other, 5));
        Assert.Equal(other, point.MoveToward(other, 10));
        Assert.Equal(200, point.DistanceSquared(other));
        Assert.Equal("{\"X\":10,\"Y\":20}", JsonSerializer.Serialize(point, HostJson.CreateOptions()));
    }
}
