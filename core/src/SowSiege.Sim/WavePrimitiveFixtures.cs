using System.Text.Json;
using SowSiege.Core;

namespace SowSiege.Sim;

public sealed record WavePrimitiveFixtureRecord(string Id, string Kind, string DesignRef, string CopyParametersFrom, string[] LinkedIds, WavePrimitiveProgram Program);
public sealed record WavePrimitiveFixtureFile(int Version, WavePrimitiveFixtureRecord[] Records);

public static class WavePrimitiveFixtures
{
    // Explicit diagnostic entry point; normal profile selection never calls this loader.
    public static ContentCatalog Load(string directory, Stream input)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
        var file = JsonSerializer.Deserialize<WavePrimitiveFixtureFile>(input, options) ?? throw new InvalidDataException("Missing primitive fixture.");
        if (file.Version != 1 || file.Records.Length == 0) { throw new InvalidDataException("Unsupported primitive fixture version or empty records."); }
        var catalog = ContentLoader.Load(directory, false, "wave-1a");
        var definition = catalog.WaveRuntime!;
        using var design = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "system-design-v1.json")));
        var designs = design.RootElement.GetProperty("content").EnumerateArray().ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var programs = WavePrimitiveSupport.Resolve(definition).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var gear = definition.Gear.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var items = definition.Items.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var weapons = catalog.Weapons.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        foreach (var record in file.Records)
        {
            if (!designs.TryGetValue(record.Id, out var designRecord) || record.DesignRef != $"data/system-design-v1.json@designed-v1.1#{record.Id}" || designRecord.GetProperty("bespoke").GetArrayLength() != 0 || programs.ContainsKey(record.Id))
            { throw new InvalidDataException("Fixture must reference new approved non-bespoke content."); }
            WavePrimitiveSupport.Validate(record.Program);
            var designParams = designRecord.GetProperty("params");
            if (designParams.EnumerateObject().Count() != record.Program.Params.Count) { throw new InvalidDataException("Fixture unit selection differs from design."); }
            foreach (var unit in record.Program.Params)
            {
                if (!designParams.TryGetProperty(unit.Key, out var expected) || expected.EnumerateObject().Count() != unit.Value.Count) { throw new InvalidDataException("Fixture unit parameter shape differs from design."); }
                foreach (var parameter in unit.Value)
                {
                    if (!expected.TryGetProperty(parameter.Key, out var value)) { throw new InvalidDataException("Unknown fixture parameter."); }
                    var token = value.ValueKind switch
                    {
                        JsonValueKind.Array => parameter.Key is "weaponIds" or "toolIds" ? "none" : string.Join('|', value.EnumerateArray().Select(v => v.GetString())),
                        JsonValueKind.Null => "inherit",
                        JsonValueKind.String => value.GetString(),
                        _ => value.GetRawText()
                    };
                    if (token != parameter.Value) { throw new InvalidDataException("Fixture executable parameter differs from approved design."); }
                }
            }
            if (record.Kind == "weapon" && designRecord.GetProperty("kind").GetString() == "weapon")
            {
                var original = gear[record.CopyParametersFrom];
                var composed = original with { Id = record.Id, DesignRef = record.DesignRef };
                gear.Add(record.Id, composed);
                var shape = record.Program.Value("unit:attack-shape", "shape");
                weapons.Add(record.Id, new(record.Id, [], new(composed.Damage, composed.Range, composed.CooldownTicks, shape, composed.Knockback)));
            }
            else if (record.Kind == "item" && designRecord.GetProperty("kind").GetString() == "item")
            {
                var original = items[record.CopyParametersFrom];
                var approvedLinks = designRecord.GetProperty("linkedToolIds").EnumerateArray().Concat(designRecord.GetProperty("linkedWeaponIds").EnumerateArray()).Select(id => id.GetString()!).OrderBy(id => id, StringComparer.Ordinal);
                if (!record.LinkedIds.OrderBy(id => id, StringComparer.Ordinal).SequenceEqual(approvedLinks) || record.LinkedIds.Any(id => !gear.ContainsKey(id))) { throw new InvalidDataException("Fixture references unavailable equipment."); }
                items.Add(record.Id, original with { Id = record.Id, DesignRef = record.DesignRef, EquipmentIds = record.LinkedIds });
            }
            else { throw new InvalidDataException("Fixture supports only weapon/item composition probes."); }
            programs.Add(record.Id, record.Program);
        }
        var composedDefinition = definition with { Gear = gear, Items = items, Programs = programs };
        WavePrimitiveSupport.Resolve(composedDefinition);
        return catalog with { Weapons = weapons, WaveRuntime = composedDefinition };
    }
}
