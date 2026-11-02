using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Game.App
{
    public sealed class WorldMapTile
    {
        public int X, Y;
        public string Terrain;
        public bool Passable;
    }
    public sealed class WorldMapTarget
    {
        public string Id, Kind, ContentId, AccountId, AllianceName;
        public long Generation;
        public int X, Y;
    }
    public sealed class WorldMapChunk
    {
        public string WorldId, ContentHash;
        public int X, Y, Size;
        public WorldMapTile[] Tiles;
        public WorldMapTarget[] Targets;
        public WorldMapTile Tile(int x, int y) => Tiles.FirstOrDefault(t => t.X == x && t.Y == y);
        public WorldMapTarget Target(int x, int y) => Targets.FirstOrDefault(t => t.X == x && t.Y == y);
    }
    public static class WorldMapResponse
    {
        static string Text(XElement node, string name) => AccountJson.StringValue(AccountJson.Field(node, name));
        static int Number(XElement node, string name)
        {
            var value = checked((int)AccountJson.LongValue(AccountJson.Field(node, name)));
            if (value < 0) throw new FormatException("Negative world coordinate.");
            return value;
        }
        static string OptionalText(XElement node, string name)
        {
            var value = AccountJson.Field(node, name);
            return (string)value.Attribute("type") == "null" ? null : AccountJson.StringValue(value);
        }
        static WorldMapTarget Target(XElement node)
        {
            var result = new WorldMapTarget { Id = Text(node, "targetId"), Generation = AccountJson.LongValue(AccountJson.Field(node, "generation")),
                Kind = Text(node, "kind"), ContentId = Text(node, "contentId"), X = Number(node, "x"), Y = Number(node, "y"),
                AccountId = OptionalText(node, "accountId"), AllianceName = OptionalText(node, "allianceName") };
            if (string.IsNullOrWhiteSpace(result.Id) || string.IsNullOrWhiteSpace(result.ContentId) || result.Generation < 1
                || !new[] { "city", "resource", "monster", "stronghold" }.Contains(result.Kind)
                || (result.Kind == "city") != (result.AccountId != null)
                || result.AccountId != null && !Guid.TryParse(result.AccountId, out _)) throw new FormatException("Invalid world target.");
            return result;
        }
        public static WorldMapTarget ReadCity(string json)
        {
            var city = Target(AccountJson.Parse(json));
            if (city.Kind != "city") throw new FormatException("Expected city.");
            return city;
        }
        public static WorldMapChunk ReadChunk(string json)
        {
            var node = AccountJson.Parse(json);
            var result = new WorldMapChunk { WorldId = Text(node, "worldId"), ContentHash = Text(node, "contentHash"), X = Number(node, "chunkX"), Y = Number(node, "chunkY") };
            result.Tiles = AccountJson.Array(node, "tiles", tile =>
            {
                var passable = AccountJson.Field(tile, "passable", "boolean");
                if (!bool.TryParse(passable.Value, out var walkable)) throw new FormatException("Invalid tile passability.");
                var terrain = Text(tile, "terrain");
                if (!new[] { "plain", "forest", "mountain", "water", "bridge" }.Contains(terrain)) throw new FormatException("Unknown terrain.");
                return new WorldMapTile { X = Number(tile, "x"), Y = Number(tile, "y"), Terrain = terrain, Passable = walkable };
            });
            result.Targets = AccountJson.Array(node, "targets", Target);
            // This is a response safety bound, not a gameplay map size. The server owns chunk dimensions.
            if (result.Tiles.Length == 0 || result.Tiles.Length > 4096 || string.IsNullOrWhiteSpace(result.WorldId)
                || result.ContentHash.Length != 64 || !result.ContentHash.All(Uri.IsHexDigit)) throw new FormatException("Invalid chunk identity.");
            result.Size = (int)Math.Sqrt(result.Tiles.Length);
            if (result.Size * result.Size != result.Tiles.Length) throw new FormatException("Incomplete square chunk.");
            var originX = checked(result.X * result.Size); var originY = checked(result.Y * result.Size);
            var positions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tile in result.Tiles)
                if ((long)tile.X - originX < 0 || (long)tile.X - originX >= result.Size || (long)tile.Y - originY < 0 || (long)tile.Y - originY >= result.Size
                    || !positions.Add(Coordinate(tile.X, tile.Y))) throw new FormatException("Invalid or duplicate tile coordinates.");
            var targetPositions = new HashSet<string>(StringComparer.Ordinal); var targetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in result.Targets)
                if (!positions.Contains(Coordinate(target.X, target.Y)) || !targetPositions.Add(Coordinate(target.X, target.Y)) || !targetIds.Add(target.Id))
                    throw new FormatException("Invalid or duplicate target coordinates.");
            return result;
        }
        public static string Coordinate(int x, int y) => x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture);
    }
}
