using System.Text.Json;

namespace SowSiege.Sim;

public sealed record UnityExportCamera(int WorldUnitsPerUnityUnit, int MinHalfHeight, int MaxHalfHeight, int EstatePadding, int FollowMilliseconds, int ZoomMilliseconds);
public sealed record UnityExportPresentation(int ContractVersion, UnityExportCamera Camera)
{
    public static UnityExportPresentation Load(string directory)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        var presentation = JsonSerializer.Deserialize<UnityExportPresentation>(File.ReadAllText(Path.Combine(directory, "presentation.json")), options)
            ?? throw new InvalidDataException("Presentation config is required.");
        var camera = presentation.Camera;
        if (presentation.ContractVersion != 1 || camera is null || camera.WorldUnitsPerUnityUnit is < 1 or > 1000000 ||
            camera.MinHalfHeight is < 1 or > 1000000 || camera.MaxHalfHeight < camera.MinHalfHeight || camera.MaxHalfHeight > 1000000 ||
            camera.EstatePadding is < 1 or > 1000000 || camera.FollowMilliseconds is < 1 or > 60000 || camera.ZoomMilliseconds is < 1 or > 60000)
        {
            throw new InvalidDataException("Invalid presentation camera bounds.");
        }
        return presentation;
    }
}
