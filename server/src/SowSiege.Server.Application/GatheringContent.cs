using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SowSiege.Server.Application;

public sealed record GatheringResource([property: JsonRequired] string ContentId, [property: JsonRequired] string MaterialId);

public sealed class GatheringContent
{
    public const string DataPath = "opt-in/slg/gathering-v1.json";
    private sealed record File([property: JsonRequired] string Id, [property: JsonRequired] string Kind,
        [property: JsonRequired] int SchemaVersion, [property: JsonRequired] string Status,
        [property: JsonRequired] string NumericStatus, [property: JsonRequired] string[] Sources,
        [property: JsonRequired] GatheringPolicy Policy, [property: JsonRequired] GatheringResource[] Resources);
    public GatheringPolicy Policy { get; }
    public string Hash { get; }
    public IReadOnlyDictionary<string, string> Materials { get; }
    private GatheringContent(File file, byte[] bytes)
    {
        if (file.Id != "slg:gathering_v1" || file.Kind != "slg-gathering" || file.SchemaVersion != 1 ||
            file.Status != "hypothesis" || file.NumericStatus != "[가설]" || file.Sources is not { Length: > 0 } ||
            file.Sources.Any(string.IsNullOrWhiteSpace) || file.Resources is not { Length: > 0 } ||
            file.Resources.Any(r => r is null || string.IsNullOrWhiteSpace(r.ContentId) || !r.MaterialId.StartsWith("meta:", StringComparison.Ordinal)) ||
            file.Resources.Select(r => r.ContentId).Distinct(StringComparer.Ordinal).Count() != file.Resources.Length)
        { throw new InvalidDataException("Invalid gathering content."); }
        WorldGathering.Create(file.Id, 1, file.Policy, 0);
        Policy = file.Policy;
        Hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Materials = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(file.Resources.ToDictionary(r => r.ContentId, r => r.MaterialId, StringComparer.Ordinal));
    }
    public static GatheringContent Parse(byte[] bytes)
    {
        try
        {
            return new(JsonSerializer.Deserialize<File>(bytes, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Missing gathering content."), bytes);
        }
        catch (JsonException e) { throw new InvalidDataException("Invalid gathering JSON.", e); }
    }
    public static GatheringContent Load(string root)
    {
        string data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        var content = Parse(System.IO.File.ReadAllBytes(Path.Combine(data, DataPath)));
        using var progression = JsonDocument.Parse(System.IO.File.ReadAllBytes(Path.Combine(data, "meta/progression.json")));
        var materials = progression.RootElement.GetProperty("materials").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
        if (content.Materials.Values.Any(m => !materials.Contains(m))) { throw new InvalidDataException("Unknown gathering material."); }
        return content;
    }
}
