using System.Text;
using System.Text.Json.Nodes;
using SowSiege.Server.Application;
using Xunit;

namespace SowSiege.Server.Tests;

public sealed class WorldMapTests
{
    private static JsonObject Small()
    {
        var file = JsonNode.Parse(System.IO.File.ReadAllBytes(Path.Combine(ContentEstateTests.Root(), "data", WorldMapContent.DataPath)))!.AsObject();
        file["width"] = 4;
        file["height"] = 4;
        file["chunkSize"] = 2;
        file["registeredAccountTarget"] = 1;
        file["terrainPatches"] = new JsonArray();
        file["startRegion"] = JsonNode.Parse("{\"x\":0,\"y\":0,\"width\":1,\"height\":1}");
        file["startAnchor"] = JsonNode.Parse("{\"x\":0,\"y\":0}");
        file["initialTargets"] = new JsonArray();
        return file;
    }

    private static WorldMapContent Parse(JsonObject file) => WorldMapContent.Parse(Encoding.UTF8.GetBytes(file.ToJsonString()));

    private static void ValidateReferences(WorldMapContent map) => map.ValidateTargets(
        System.IO.File.ReadAllBytes(Path.Combine(ContentEstateTests.Root(), "data/meta/progression.json")),
        System.IO.File.ReadAllBytes(Path.Combine(ContentEstateTests.Root(), "data/opt-in/slg/content-draft-v1.json")));

    [Theory]
    [InlineData("resource", "meta:unknown_material")]
    [InlineData("resource", "meta:charter")]
    [InlineData("monster", "slg:unknown_encounter")]
    [InlineData("monster", "slg:encounter_ruin_marker")]
    [InlineData("stronghold", "slg:encounter_field_mites")]
    public void UnknownResourcesAndMismatchedEncounterCategoriesFailClosed(string kind, string contentId)
    {
        var file = Small();
        var target = new JsonObject { ["targetId"] = "test:target", ["kind"] = kind, ["contentId"] = contentId, ["x"] = 1, ["y"] = 0, ["generation"] = 1 };
        file["initialTargets"] = new JsonArray(target);
        var map = Parse(file);
        Assert.Throws<InvalidDataException>(() => ValidateReferences(map));
    }

    [Fact]
    public void ShippedResourcesAndEncounterCategoriesResolveWithoutChangingPlacement()
    {
        var map = WorldMapContent.Load(ContentEstateTests.Root());
        string hash = map.Hash;
        var targets = map.InitialTargets.ToArray();
        ValidateReferences(map);
        Assert.Equal(hash, map.Hash);
        Assert.Equal(targets, map.InitialTargets);
    }

    [Fact]
    public void ShippedLayoutHasConnectedCapacityAndBoundedChunks()
    {
        var map = WorldMapContent.Load(ContentEstateTests.Root());
        Assert.Equal((512, 512, 32), (map.Width, map.Height, map.ChunkSize));
        Assert.True(map.CitySlots.Count >= map.RegisteredAccountTarget);
        Assert.Equal(map.CitySlots.Count, map.CitySlots.Distinct().Count());
        Assert.Equal(map.CitySlots.OrderBy(p => p.X).ThenBy(p => p.Y), map.CitySlots);
        Assert.All(map.CitySlots, p => Assert.True(map.IsPassable(p.X, p.Y)));
        Assert.All(map.InitialTargets, t => Assert.DoesNotContain(new WorldCoordinate(t.X, t.Y), map.CitySlots));
        Assert.Equal(1024, map.Chunk(0, 0).Length);
        Assert.Equal(new WorldTile(511, 511, "plain", true), map.Chunk(15, 15)[^1]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4, 0)]
    [InlineData(0, 4)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void OutOfWorldCoordinatesNeverWrap(int x, int y)
    {
        var map = Parse(Small());
        Assert.False(map.Contains(x, y));
        Assert.False(map.IsPassable(x, y));
        Assert.Null(map.ShortestPath(new(0, 0), new(x, y)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.TerrainAt(x, y));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void InvalidChunkDoesNotReturnOtherChunks(int x, int y) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Parse(Small()).Chunk(x, y));

    [Fact]
    public void EqualShortestPathsUseXThenYNeighbourOrder()
    {
        var map = Parse(Small());
        WorldCoordinate[] expected = [new(1, 1), new(1, 2), new(2, 2)];
        Assert.Equal(expected, map.ShortestPath(new(1, 1), new(2, 2)));
        Assert.Equal(expected, map.ShortestPath(new(1, 1), new(2, 2)));
        Assert.Equal(new[] { new WorldCoordinate(1, 1) }, map.ShortestPath(new(1, 1), new(1, 1)));
    }

    [Fact]
    public void WaterAndMountainBlockWhileForestAndFixedBridgePass()
    {
        var file = Small();
        file["terrainPatches"] = JsonNode.Parse("""
            [{"terrain":"water","x":1,"y":0,"width":1,"height":4},
             {"terrain":"bridge","x":1,"y":2,"width":1,"height":1},
             {"terrain":"forest","x":2,"y":2,"width":1,"height":1},
             {"terrain":"mountain","x":3,"y":0,"width":1,"height":1}]
            """);
        var map = Parse(file);
        Assert.False(map.IsPassable(1, 0));
        Assert.False(map.IsPassable(3, 0));
        Assert.True(map.IsPassable(1, 2));
        Assert.True(map.IsPassable(2, 2));
        var path = map.ShortestPath(new(0, 0), new(2, 0));
        Assert.NotNull(path);
        Assert.Equal(7, path.Length);
        Assert.Contains(new WorldCoordinate(1, 2), path);
        Assert.Null(map.ShortestPath(new(0, 0), new(3, 0)));
    }

    [Fact]
    public void SeparatedDestinationsHaveNoPathAndCannotBeStartingCities()
    {
        var file = Small();
        file["terrainPatches"] = JsonNode.Parse("[{\"terrain\":\"water\",\"x\":1,\"y\":0,\"width\":1,\"height\":4}]");
        Assert.Null(Parse(file).ShortestPath(new(0, 0), new(2, 0)));
        file["startRegion"] = JsonNode.Parse("{\"x\":0,\"y\":0,\"width\":4,\"height\":4}");
        Assert.Throws<InvalidDataException>(() => Parse(file));
    }

    [Fact]
    public void BlockedStartingCellsAreNeverAllocated()
    {
        var file = Small();
        file["startRegion"] = JsonNode.Parse("{\"x\":0,\"y\":0,\"width\":2,\"height\":1}");
        file["terrainPatches"] = JsonNode.Parse("[{\"terrain\":\"mountain\",\"x\":1,\"y\":0,\"width\":1,\"height\":1}]");
        Assert.Equal(new WorldCoordinate(0, 0), Assert.Single(Parse(file).CitySlots));
        file["registeredAccountTarget"] = 2;
        Assert.Throws<InvalidDataException>(() => Parse(file));
    }

    [Fact]
    public void TargetMarkersReserveCitySlotsButDoNotBlockPaths()
    {
        var file = Small();
        file["startRegion"] = JsonNode.Parse("{\"x\":0,\"y\":0,\"width\":2,\"height\":1}");
        file["initialTargets"] = JsonNode.Parse("[{\"targetId\":\"test:target\",\"kind\":\"resource\",\"contentId\":\"meta:grain\",\"x\":1,\"y\":0,\"generation\":1}]");
        var map = Parse(file);
        Assert.Equal(new WorldCoordinate(0, 0), Assert.Single(map.CitySlots));
        Assert.NotNull(map.ShortestPath(new(0, 0), new(2, 0)));
        Assert.Equal(1, Assert.Single(map.InitialTargets).Generation);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("occupied")]
    [InlineData("generation")]
    [InlineData("blocked")]
    [InlineData("isolated")]
    public void InvalidTargetsRejectTheWholeMap(string invalid)
    {
        var file = Small();
        var target = JsonNode.Parse("{\"targetId\":\"test:target\",\"kind\":\"resource\",\"contentId\":\"meta:grain\",\"x\":2,\"y\":0,\"generation\":1}")!.AsObject();
        var targets = new JsonArray(target);
        file["initialTargets"] = targets;
        switch (invalid)
        {
            case "identity":
                var sameId = target.DeepClone();
                sameId["y"] = 1;
                targets.Add(sameId);
                break;
            case "occupied":
                var sameTile = target.DeepClone();
                sameTile["targetId"] = "test:second";
                targets.Add(sameTile);
                break;
            case "generation": target["generation"] = 0; break;
            case "blocked": file["terrainPatches"] = JsonNode.Parse("[{\"terrain\":\"mountain\",\"x\":2,\"y\":0,\"width\":1,\"height\":1}]"); break;
            case "isolated": file["terrainPatches"] = JsonNode.Parse("[{\"terrain\":\"water\",\"x\":1,\"y\":0,\"width\":1,\"height\":4}]"); break;
        }
        Assert.Throws<InvalidDataException>(() => Parse(file));
    }

    [Theory]
    [InlineData("width", 0)]
    [InlineData("height", -1)]
    [InlineData("width", int.MaxValue)]
    [InlineData("chunkSize", 0)]
    [InlineData("chunkSize", 3)]
    [InlineData("registeredAccountTarget", 0)]
    [InlineData("registeredAccountTarget", 2)]
    [InlineData("schemaVersion", 2)]
    public void InvalidNumericPolicyFailsClosed(string property, int value)
    {
        var file = Small();
        file[property] = value;
        Assert.Throws<InvalidDataException>(() => Parse(file));
    }

    [Fact]
    public void MissingOrUnknownFieldsAndTerrainFailClosed()
    {
        var file = Small();
        file.Remove("startAnchor");
        Assert.Throws<InvalidDataException>(() => Parse(file));
        file = Small();
        file["clientTime"] = 1;
        Assert.Throws<InvalidDataException>(() => Parse(file));
        file = Small();
        file["defaultTerrain"] = "lava";
        Assert.Throws<InvalidDataException>(() => Parse(file));
        file = Small();
        file["terrainPatches"] = JsonNode.Parse("[{\"terrain\":\"forest\",\"x\":3,\"y\":0,\"width\":2,\"height\":1}]");
        Assert.Throws<InvalidDataException>(() => Parse(file));
    }

    [Fact]
    public void HashPinsExactBytesAndReloadKeepsTheSamePlacement()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Small().ToJsonString());
        var first = WorldMapContent.Parse(bytes);
        var restarted = WorldMapContent.Parse(bytes);
        Assert.Equal(first.Hash, restarted.Hash);
        Assert.Equal(first.CitySlots, restarted.CitySlots);
        var formatted = WorldMapContent.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes) + "\n"));
        Assert.NotEqual(first.Hash, formatted.Hash);
        Assert.Equal(first.CitySlots, formatted.CitySlots);
        Assert.Throws<NotSupportedException>(() => ((IList<WorldCoordinate>)first.CitySlots)[0] = new(1, 1));
    }
}
