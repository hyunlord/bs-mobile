using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

// M3a account estate prototype settings (#361). They are opt-in data outside the data tree hash (ADR0069), versioned by file name.
public static class AccountEstateConfigLoader
{
    public const string PrototypeVersion = "m3a-prototype-r2";
    // The design r3 centre + inner ring grid with the r2 economy (#374), kept for its tests.
    public const string WideMapVersion = "m3a-prototype-r3";
    // The r3 centre with its zones laid along the portrait screen's vertical (#375), kept for its tests.
    public const string VerticalMapVersion = "m3a-prototype-r4";
    // The first night map (#379), retained for historical estate saves.
    public const string NightMapVersion = "m3a-prototype-r5";
    // The first-login map (#385): an 8x8 lit start with unchanged buildings and economy.
    public const string FirstLoginMapVersion = "m3a-prototype-r6";
    // Layout A (#389): separated reclaimable sites around a village plaza; economics unchanged.
    public const string MapPrototypeVersion = "m3a-prototype-r7";
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static string PathFor(string root, string version = PrototypeVersion)
    {
        var data = Directory.Exists(Path.Combine(root, "data")) ? Path.Combine(root, "data") : root;
        return Path.Combine(data, ContentLoader.OptInDirectory, "account-estate", version + ".json");
    }

    public static AccountEstateConfig Load(string root, MetaCatalog catalog, string version = PrototypeVersion)
    {
        var config = JsonSerializer.Deserialize<AccountEstateConfig>(File.ReadAllText(PathFor(root, version)), Options)
            ?? throw new InvalidDataException("Missing account estate config.");
        if (config.Version != version) { throw new InvalidDataException("Account estate config version does not match its file name."); }
        AccountEstate.Validate(catalog, config);
        return config;
    }
}
