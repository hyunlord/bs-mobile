using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public static class MetaContentLoader
{
    public static MetaCatalog Read(string root) => SowSiege.Content.MetaCatalogReader.Read(root);

    public static MetaCatalog Load(string root)
    {
        var data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        var catalog = Read(data);
        var project = Directory.GetParent(Path.GetFullPath(data))!.FullName;
        using var art = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "unity", "Assets", "Art", "first-playable-manifest.json")));
        var roles = art.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).ToHashSet(StringComparer.Ordinal);
        var vassals = Directory.GetFiles(Path.Combine(data, "vassals"), "*.json").Select(file =>
        {
            using var value = JsonDocument.Parse(File.ReadAllText(file));
            return value.RootElement.GetProperty("id").GetString()!;
        }).ToHashSet(StringComparer.Ordinal);
        Validate(catalog, ContentLoader.Load(data, profileName: "first-playable"), roles, vassals);
        return catalog;
    }

    public static void Validate(MetaCatalog c, ContentCatalog run, IReadOnlySet<string> artRoles, IReadOnlySet<string> vassalIds) =>
        SowSiege.Content.MetaCatalogValidation.Validate(c, run, artRoles, vassalIds);
}
