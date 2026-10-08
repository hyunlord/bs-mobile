using System.Text.Json;
using System.Text.RegularExpressions;
using SowSiege.Core;

namespace SowSiege.Sim;

public static partial class ContentLoader
{
    private static WeaponCombatDefinition? LoadWeaponCombat(string directory, RuntimeProfile profile, string[] selectedWeapons)
    {
        if (profile.WeaponCombat is not { } extension) { return null; }
        Require(extension.ContractVersion is 1 or 2, "Unsupported weapon combat profile version.");
        var allowedWeapons = profile.Selection.Weapons.Concat(profile.TestSelection.Weapons).ToHashSet(StringComparer.Ordinal);
        var records = LoadSelected<WeaponContent, WeaponContent>([directory, Path.Combine(directory, "test")], "weapons", allowedWeapons.ToArray(), weapon => weapon, profile.Runtime is not null);
        Dictionary<string, WeaponCombatWeaponDefinition> definitions;
        if (extension.ContractVersion == 1)
        {
            Require(extension.DefinitionsFile is not null, "Legacy weapon combat requires definitions file.");
            var content = Read<WeaponCombatFile>(ConfigurationPath(directory, extension.DefinitionsFile!, @"\A(?:experiments/)?weapon-growth-[A-Za-z0-9_-]+\.json\z"));
            Require(content.ContractVersion == 1, "Weapon combat profile and file versions differ.");
            Require(records.Values.All(weapon => weapon.Growth is null), "Legacy table and canonical growth cannot both define weapon numbers.");
            definitions = content.Weapons;
        }
        else
        {
            if (extension.DefinitionsFile is { } file)
            {
                var manifest = Read<WeaponCombatManifest>(ConfigurationPath(directory, file, @"\Aexperiments/weapon-growth-[A-Za-z0-9_-]+\.json\z"));
                Require(manifest.ContractVersion == 2 && manifest.Weapons.Length == allowedWeapons.Count && manifest.Weapons.ToHashSet(StringComparer.Ordinal).SetEquals(allowedWeapons), "Weapon growth manifest must exactly cover selected weapons.");
            }
            Require(records.Values.All(weapon => weapon.Growth is not null), "Canonical weapon growth is required.");
            definitions = records.ToDictionary(pair => pair.Key, pair => pair.Value.Growth!, StringComparer.Ordinal);
        }
        Require(definitions.Count > 0 && definitions.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(allowedWeapons), "Weapon combat definitions must exactly cover primary and test weapons.");
        var selected = selectedWeapons.ToHashSet(StringComparer.Ordinal);
        Require(selected.IsSubsetOf(allowedWeapons), "Weapon combat definitions must cover currently selected weapons.");
        foreach (var weapon in definitions.Values) { ValidateWeaponGrowth(weapon); }
        // Authoring version 2 changes storage only; the Core combat contract remains version 1.
        return new(1, definitions.Where(pair => selected.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private static void ValidateWeaponSource(WeaponContent weapon)
    {
        var values = new[] { weapon.Activation.Damage, weapon.Activation.Range, weapon.Activation.CooldownTicks, weapon.Activation.Knockback };
        Require(weapon.Growth is null ? values.All(value => value.HasValue) : values.All(value => !value.HasValue), "Weapon requires exactly one numeric source: activation or canonical growth.");
        if (weapon.Growth is { } growth) { ValidateWeaponGrowth(growth); }
    }

    private static void ValidateWeaponGrowth(WeaponCombatWeaponDefinition weapon)
    {
        Require(weapon.AttackModel is "sector90" or "sector180" or "rays" or "disk", "Unknown weapon attack model.");
        Require(weapon.BeamHalfWidth >= 0 && weapon.BeamHalfWidth <= 1000000, "Invalid beam half width.");
        Require(weapon.AttackModel == "rays" ? weapon.BeamHalfWidth > 0 : weapon.BeamHalfWidth == 0, "Beam width does not match attack model.");
        Require(weapon.Levels.Length == 12, "Weapon combat requires exactly twelve absolute levels.");
        for (var index = 0; index < weapon.Levels.Length; index++)
        {
            var level = weapon.Levels[index];
            Require(level.Level == index + 1, "Weapon levels must be contiguous and ordered from one.");
            Require(level.Count is >= 1 and <= 64 && level.Pierce is >= 0 and <= 64, "Weapon work budget exceeds bounds.");
            Require(weapon.AttackModel == "rays" || level.Pierce == 0, "Pierce is valid only for rays.");
            if (index == 0) { continue; }
            var previous = weapon.Levels[index - 1];
            Require(level.Damage >= previous.Damage && level.Range >= previous.Range && level.Count >= previous.Count
                && level.Pierce >= previous.Pierce && level.Knockback >= previous.Knockback && level.CooldownTicks <= previous.CooldownTicks, "Weapon growth cannot regress.");
            Require(level.Range > previous.Range || level.Count > previous.Count || level.Pierce > previous.Pierce
                || level.Knockback > previous.Knockback || level.CooldownTicks < previous.CooldownTicks, "Each weapon level requires a non-damage improvement.");
        }
    }

    private static void UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(seen.Add(property.Name), $"Duplicate weapon combat property: {property.Name}");
                UniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array) { foreach (var item in element.EnumerateArray()) { UniqueProperties(item); } }
    }
}
