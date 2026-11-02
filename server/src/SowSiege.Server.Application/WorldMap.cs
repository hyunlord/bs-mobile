using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SowSiege.Server.Application;

public readonly record struct WorldCoordinate([property: JsonRequired] int X, [property: JsonRequired] int Y);
public sealed record WorldTerrainPatch([property: JsonRequired] string Terrain, [property: JsonRequired] int X, [property: JsonRequired] int Y, [property: JsonRequired] int Width, [property: JsonRequired] int Height);
public sealed record WorldInitialTarget([property: JsonRequired] string TargetId, [property: JsonRequired] string Kind, [property: JsonRequired] string ContentId, [property: JsonRequired] int X, [property: JsonRequired] int Y, [property: JsonRequired] int Generation);
public sealed record WorldTile(int X, int Y, string Terrain, bool Passable);
public sealed record WorldStartRegion([property: JsonRequired] int X, [property: JsonRequired] int Y, [property: JsonRequired] int Width, [property: JsonRequired] int Height);

/// <summary>Immutable server map. Terrain and placement numbers come from opt-in data, not the historical Core catalog.</summary>
public sealed class WorldMapContent
{
    public const string DataPath = "opt-in/slg/world-map-v1.json";
    private static readonly JsonSerializerOptions Read = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly Regex NamespacedId = new("^[a-z][a-z0-9_]*:[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);
    private readonly string[] terrain;

    private sealed record File(
        [property: JsonRequired] string Id, [property: JsonRequired] string Kind, [property: JsonRequired] int SchemaVersion,
        [property: JsonRequired] string Status, [property: JsonRequired] string NumericStatus, [property: JsonRequired] string[] Sources,
        [property: JsonRequired] string WorldId, [property: JsonRequired] int Width, [property: JsonRequired] int Height,
        [property: JsonRequired] int ChunkSize, [property: JsonRequired] int RegisteredAccountTarget,
        [property: JsonRequired] string DefaultTerrain, [property: JsonRequired] WorldTerrainPatch[] TerrainPatches,
        [property: JsonRequired] WorldStartRegion StartRegion, [property: JsonRequired] WorldCoordinate StartAnchor,
        [property: JsonRequired] WorldInitialTarget[] InitialTargets, [property: JsonRequired] string Notes);

    public string WorldId { get; }
    public int Width { get; }
    public int Height { get; }
    public int ChunkSize { get; }
    public int RegisteredAccountTarget { get; }
    public string Hash { get; }
    public IReadOnlyList<WorldInitialTarget> InitialTargets { get; }
    public IReadOnlyList<WorldCoordinate> CitySlots { get; }
    public WorldCoordinate StartAnchor { get; }

    private WorldMapContent(File file, byte[] bytes)
    {
        Require(file.Kind == "slg-world-map" && file.SchemaVersion == 1 && IsId(file.Id) && IsId(file.WorldId), "World identity invalid.");
        Require(file.Status == "hypothesis" && file.NumericStatus == "[가설]" && file.Sources is { Length: > 0 } && file.Sources.All(s => !string.IsNullOrWhiteSpace(s)), "World provenance invalid.");
        // Resource bounds are parser safety limits, not gameplay dimensions or capacity promises.
        Require(file.Width > 0 && file.Height > 0 && (long)file.Width * file.Height <= 4_194_304, "World dimensions invalid.");
        Require(file.ChunkSize > 0 && file.ChunkSize <= file.Width && file.ChunkSize <= file.Height && file.Width % file.ChunkSize == 0 && file.Height % file.ChunkSize == 0, "World chunk dimensions invalid.");
        Require(file.RegisteredAccountTarget > 0 && file.TerrainPatches is not null && file.InitialTargets is not null && file.StartRegion is not null, "World placement missing.");
        WorldId = file.WorldId;
        Width = file.Width;
        Height = file.Height;
        ChunkSize = file.ChunkSize;
        RegisteredAccountTarget = file.RegisteredAccountTarget;
        Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        StartAnchor = file.StartAnchor;
        Require(IsTerrain(file.DefaultTerrain), "Unknown default terrain.");
        terrain = Enumerable.Repeat(file.DefaultTerrain, checked(Width * Height)).ToArray();
        foreach (var patch in file.TerrainPatches!)
        {
            Require(patch is not null && IsTerrain(patch.Terrain) && RectangleInside(patch.X, patch.Y, patch.Width, patch.Height), "Invalid terrain patch.");
            for (int x = patch!.X; x < patch.X + patch.Width; x++)
            {
                for (int y = patch.Y; y < patch.Y + patch.Height; y++) { terrain[Index(x, y)] = patch.Terrain; }
            }
        }
        var region = file.StartRegion!;
        Require(RectangleInside(region.X, region.Y, region.Width, region.Height) && IsPassable(StartAnchor.X, StartAnchor.Y), "Invalid start region or anchor.");
        Require(StartAnchor.X >= region.X && StartAnchor.X < region.X + region.Width && StartAnchor.Y >= region.Y && StartAnchor.Y < region.Y + region.Height, "Anchor outside start region.");
        var reachable = Reachable(StartAnchor);
        var targetIds = new HashSet<string>(StringComparer.Ordinal);
        var occupied = new HashSet<WorldCoordinate>();
        foreach (var target in file.InitialTargets!)
        {
            Require(target is not null && IsId(target.TargetId) && IsId(target.ContentId) && target.Generation > 0 && target.Kind is "resource" or "monster" or "stronghold", "Invalid initial target.");
            Require(IsPassable(target!.X, target.Y) && reachable[Index(target.X, target.Y)], "Initial target is blocked or isolated.");
            Require(targetIds.Add(target.TargetId) && occupied.Add(new(target.X, target.Y)), "Duplicate target ID or occupied tile.");
        }
        InitialTargets = Array.AsReadOnly(file.InitialTargets!.ToArray());
        var slots = new List<WorldCoordinate>();
        for (int x = region.X; x < region.X + region.Width; x++)
        {
            for (int y = region.Y; y < region.Y + region.Height; y++)
            {
                if (!IsPassable(x, y)) { continue; }
                Require(reachable[Index(x, y)], "Start region contains an isolated passable tile.");
                var point = new WorldCoordinate(x, y);
                if (!occupied.Contains(point)) { slots.Add(point); }
            }
        }
        Require(slots.Count >= RegisteredAccountTarget, "Connected starting capacity is below the data target.");
        CitySlots = new ReadOnlyCollection<WorldCoordinate>(slots);
    }

    public static WorldMapContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        var map = Parse(System.IO.File.ReadAllBytes(Path.Combine(data, DataPath)));
        map.ValidateTargets(System.IO.File.ReadAllBytes(Path.Combine(data, "meta/progression.json")),
            System.IO.File.ReadAllBytes(Path.Combine(data, "opt-in/slg/content-draft-v1.json")));
        return map;
    }

    /// <summary>Resolve marker references without loading combat rules. Launch resources follow the draft's material resource order.</summary>
    public void ValidateTargets(byte[] progressionBytes, byte[] slgBytes)
    {
        try
        {
            using var progression = JsonDocument.Parse(progressionBytes);
            using var slg = JsonDocument.Parse(slgBytes);
            var materials = progression.RootElement.GetProperty("materials").EnumerateArray()
                .Select(material => material.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
            var resources = slg.RootElement.GetProperty("contracts").GetProperty("materialReward").GetProperty("resourceOrder").EnumerateArray()
                .Select(resource => "meta:" + resource.GetString()).ToHashSet(StringComparer.Ordinal);
            var encounters = slg.RootElement.GetProperty("worldEncounters").EnumerateArray()
                .ToDictionary(encounter => encounter.GetProperty("id").GetString()!, encounter => encounter.GetProperty("category").GetString(), StringComparer.Ordinal);
            foreach (var target in InitialTargets)
            {
                bool valid = target.Kind == "resource"
                    ? materials.Contains(target.ContentId) && resources.Contains(target.ContentId)
                    : encounters.TryGetValue(target.ContentId, out string? category) && category == target.Kind;
                Require(valid, $"Unknown or mismatched world target content: {target.TargetId} ({target.Kind}, {target.ContentId}).");
            }
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("World target reference catalog malformed.", error);
        }
    }

    public static WorldMapContent Parse(byte[] bytes)
    {
        try
        {
            var file = JsonSerializer.Deserialize<File>(bytes, Read) ?? throw new InvalidDataException("World content missing.");
            return new(file, bytes);
        }
        catch (JsonException error) { throw new InvalidDataException("World content malformed.", error); }
    }

    public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    public string TerrainAt(int x, int y) => Contains(x, y) ? terrain[Index(x, y)] : throw new ArgumentOutOfRangeException(nameof(x), "Tile outside world.");
    public bool IsPassable(int x, int y) => Contains(x, y) && terrain[Index(x, y)] is "plain" or "forest" or "bridge";

    /// <summary>Includes both endpoints. Equal-length paths break ties by the ordinal (X, Y) order of each next coordinate.</summary>
    public WorldCoordinate[]? ShortestPath(WorldCoordinate start, WorldCoordinate end)
    {
        if (!IsPassable(start.X, start.Y) || !IsPassable(end.X, end.Y)) { return null; }
        var previous = new int[terrain.Length];
        Array.Fill(previous, -1);
        var queue = new Queue<WorldCoordinate>();
        int startIndex = Index(start.X, start.Y);
        previous[startIndex] = startIndex;
        queue.Enqueue(start);
        while (queue.TryDequeue(out var point))
        {
            if (point == end)
            {
                var path = new List<WorldCoordinate>();
                int index = Index(end.X, end.Y);
                while (index != startIndex)
                {
                    path.Add(new(index % Width, index / Width));
                    index = previous[index];
                }
                path.Add(start);
                path.Reverse();
                return path.ToArray();
            }
            foreach (var next in Neighbours(point))
            {
                int index = Index(next.X, next.Y);
                if (previous[index] != -1) { continue; }
                previous[index] = Index(point.X, point.Y);
                queue.Enqueue(next);
            }
        }
        return null;
    }

    public WorldTile[] Chunk(int chunkX, int chunkY)
    {
        if (chunkX < 0 || chunkY < 0 || chunkX >= Width / ChunkSize || chunkY >= Height / ChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkX), "Chunk outside world.");
        }
        var tiles = new WorldTile[checked(ChunkSize * ChunkSize)];
        int index = 0;
        for (int x = chunkX * ChunkSize; x < (chunkX + 1) * ChunkSize; x++)
        {
            for (int y = chunkY * ChunkSize; y < (chunkY + 1) * ChunkSize; y++)
            {
                tiles[index++] = new(x, y, TerrainAt(x, y), IsPassable(x, y));
            }
        }
        return tiles;
    }

    private bool[] Reachable(WorldCoordinate start)
    {
        var reached = new bool[terrain.Length];
        var queue = new Queue<WorldCoordinate>();
        reached[Index(start.X, start.Y)] = true;
        queue.Enqueue(start);
        while (queue.TryDequeue(out var point))
        {
            foreach (var next in Neighbours(point))
            {
                int index = Index(next.X, next.Y);
                if (reached[index]) { continue; }
                reached[index] = true;
                queue.Enqueue(next);
            }
        }
        return reached;
    }

    private IEnumerable<WorldCoordinate> Neighbours(WorldCoordinate p)
    {
        if (IsPassable(p.X - 1, p.Y)) { yield return new(p.X - 1, p.Y); }
        if (IsPassable(p.X, p.Y - 1)) { yield return new(p.X, p.Y - 1); }
        if (IsPassable(p.X, p.Y + 1)) { yield return new(p.X, p.Y + 1); }
        if (IsPassable(p.X + 1, p.Y)) { yield return new(p.X + 1, p.Y); }
    }

    private int Index(int x, int y) => y * Width + x;
    private bool RectangleInside(int x, int y, int width, int height) => width > 0 && height > 0 && Contains(x, y) && (long)x + width <= Width && (long)y + height <= Height;
    private static bool IsTerrain(string value) => value is "plain" or "forest" or "mountain" or "water" or "bridge";
    private static bool IsId(string? value) => value is not null && NamespacedId.IsMatch(value);
    private static void Require([DoesNotReturnIf(false)] bool valid, string message) { if (!valid) { throw new InvalidDataException(message); } }
}
