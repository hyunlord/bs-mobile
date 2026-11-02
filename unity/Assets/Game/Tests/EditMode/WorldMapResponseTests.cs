using System;
using System.Linq;
using Game.App;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class WorldMapResponseTests
    {
        const string Account = "67e4d559-9394-4780-a9fe-02e6ba7b0811";
        static string City => "{\"targetId\":\"city:test\",\"generation\":3,\"kind\":\"city\",\"contentId\":\"slg:city\",\"x\":4,\"y\":2,\"accountId\":\"" + Account + "\",\"allianceName\":null}";
        static string Chunk => "{\"worldId\":\"slg:test\",\"contentHash\":\"" + new string('a', 64) + "\",\"chunkX\":2,\"chunkY\":1,\"tiles\":[" + string.Join(",", new[] { (4,2), (4,3), (5,2), (5,3) }.Select(p => "{\"x\":" + p.Item1 + ",\"y\":" + p.Item2 + ",\"terrain\":\"plain\",\"passable\":true}")) + "],\"targets\":[" + City + "]}";
        [Test] public void ResponseOwnsChunkSizeAndCoordinatesWithoutAssumingProductionDimensions()
        {
            var chunk = WorldMapResponse.ReadChunk(Chunk);
            Assert.That(chunk.Size, Is.EqualTo(2)); Assert.That(chunk.X, Is.EqualTo(2));
            Assert.That(chunk.Tile(5, 3).Passable, Is.True);
            Assert.That(chunk.Target(4, 2).Generation, Is.EqualTo(3));
            Assert.That(chunk.Target(4, 2).AccountId, Is.EqualTo(Account));
            Assert.That(chunk.Tile(0, 0), Is.Null);
        }
        [Test] public void MissingCoordinatesCannotBecomeAnInventedOrigin()
        {
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadCity(City.Replace("\"x\":4,", "")));
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadChunk(Chunk.Replace("\"chunkY\":1,", "")));
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadCity(City.Replace("\"x\":4", "\"x\":-1")));
        }
        [Test] public void DuplicateTilesAndTargetsCannotPaintASuccessfulMap()
        {
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadChunk(Chunk.Replace("\"x\":5,\"y\":3", "\"x\":4,\"y\":3")));
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadChunk(Chunk.Replace("\"targets\":[" + City + "]", "\"targets\":[" + City + "," + City + "]")));
        }
        [Test] public void WrongTypesOverflowAndMismatchedCityAreRejected()
        {
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadChunk(Chunk.Replace("\"passable\":true", "\"passable\":\"true\"")));
            Assert.Throws<OverflowException>(() => WorldMapResponse.ReadCity(City.Replace("\"x\":4", "\"x\":2147483648")));
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadCity(City.Replace("\"kind\":\"city\"", "\"kind\":\"resource\"")));
            Assert.Throws<FormatException>(() => WorldMapResponse.ReadChunk(Chunk.Replace("\"terrain\":\"plain\"", "\"terrain\":\"unknown\"")));
        }
        [Test] public void StaleOrMissingNetworkCannotOfferGameplayActions()
        {
            var client = new BarracksServerClient("http://127.0.0.1:55443");
            Assert.That(client.LoadWorldCity().MoveNext(), Is.False);
            Assert.That(client.LoadWorldChunk(0, 0).MoveNext(), Is.False);
            Assert.That(client.WorldCity, Is.Null); Assert.That(client.WorldChunk, Is.Null);
            Assert.That(PortraitMetaPanels.WorldStatus("connection-failed"), Does.Contain("확인하지 못했습니다"));
        }
    }
}
