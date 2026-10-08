using System.Text.Json;
using System.Text.RegularExpressions;
using SowSiege.Core;

namespace SowSiege.Sim;

public static partial class ContentLoader
{
    private static WeaponCombatDefinition? LoadWeaponCombat(string directory, RuntimeProfile profile, string[] selectedWeapons)
    {
        if (profile.WeaponCombat is not { } extension) { return null; }
        Require(extension.ContractVersion == 1, "Unsupported weapon combat profile version.");
        Require(Regex.IsMatch(extension.DefinitionsFile, @"\Aweapon-growth-[A-Za-z0-9_-]+\.json\z", RegexOptions.CultureInvariant), "Unsafe weapon combat definitions filename.");
        var filename = Path.Combine(directory, extension.DefinitionsFile);
        Require((File.GetAttributes(filename) & FileAttributes.ReparsePoint) == 0, "Weapon combat symlink is forbidden.");
        using (var document = JsonDocument.Parse(File.ReadAllText(filename))) { UniqueProperties(document.RootElement); }
        var content = Read<WeaponCombatFile>(filename);
        Require(content.ContractVersion == 1, "Unsupported weapon combat definitions version.");
        var allowedWeapons = profile.Selection.Weapons.Concat(profile.TestSelection.Weapons).ToHashSet(StringComparer.Ordinal);
        Require(content.Weapons.Count > 0 && content.Weapons.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(allowedWeapons), "Weapon combat definitions must exactly cover primary and test weapons.");
        var selected = selectedWeapons.ToHashSet(StringComparer.Ordinal);
        Require(selected.IsSubsetOf(allowedWeapons), "Weapon combat definitions must cover currently selected weapons.");
        foreach (var weapon in content.Weapons.Values)
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
        return new(content.ContractVersion, content.Weapons.Where(pair => selected.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
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
